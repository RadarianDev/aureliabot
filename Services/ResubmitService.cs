using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Discord;
using Discord.Interactions;

public sealed record CommandPayload(string Command, IReadOnlyDictionary<string, string> Options)
{
    private sealed record Json(string Command, Dictionary<string, string> Options);

    public string ToJson() => JsonSerializer.Serialize(new Json(Command, new Dictionary<string, string>(Options)));

    public static CommandPayload? TryParse(string payload)
    {
        if (!payload.StartsWith('{')) return null;
        try
        {
            return JsonSerializer.Deserialize<Json>(payload) is { Command: { Length: > 0 } command } json
                ? new CommandPayload(command, json.Options ?? new Dictionary<string, string>())
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public string Display =>
        "/" + string.Join(" ", Options.Select(o => $"{o.Key}:{o.Value}").Prepend(Command));

    public static string Describe(string payload) => TryParse(payload)?.Display ?? payload;
}

public sealed record ResubmitTarget(Type Module, MethodInfo Method);

public enum ResubmitClaimOutcome { Claimed, Gone, NotYours, Used }

public sealed record ResubmitClaim(ResubmitClaimOutcome Outcome, CommandPayload? Payload = null, ulong? Owner = null);

public class ResubmitService
{
    public const string ButtonPrefix = "resubmit";
    private const int ButtonsPerMessage = 25;
    private const int ButtonLabelLimit = 80;
    private const int CommandDisplayLimit = 300;

    public static readonly IReadOnlySet<string> Covered = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "campaign", "attack", "poll", "vote", "noconfidence",
        "bill propose",
        "crisis respond",
        "opposition condemn", "opposition question",
        "chancellor nominate",
        "ministry project", "ministry fund", "ministry resign",
        "treasury allocate", "treasury grant",
        "war raise", "war train", "war station", "war disband",
        "interior raise", "interior train", "interior station", "interior disband", "interior crackdown",
        "party apply", "party leave",
    };

    private static readonly IReadOnlySet<string> NotPlayerActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "info", "me", "help", "map", "ministry status",
    };

    private static readonly IReadOnlySet<string> GmGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "gm", "content", "whitelist",
    };

    private static readonly Lazy<IReadOnlyDictionary<string, ResubmitTarget>> CatalogCache = new(BuildCatalog);

    private static readonly object ClaimGate = new();

    private readonly DatabaseService _data;
    private readonly FactionChannelService? _factionChannels;

    public ResubmitService(DatabaseService data, FactionChannelService? factionChannels)
    {
        _data = data;
        _factionChannels = factionChannels;
    }

    public static IReadOnlyDictionary<string, ResubmitTarget> Catalog => CatalogCache.Value;

    public static bool IsPlayerAction(string command) =>
        !NotPlayerActions.Contains(command) && !GmGroups.Contains(command.Split(' ')[0]);

    public static ResubmitTarget? Find(string command) =>
        Covered.Contains(command) && Catalog.TryGetValue(command, out var target) ? target : null;

    public static string ButtonId(LogEntry lost) =>
        $"{ButtonPrefix}:{lost.Id.ToString(CultureInfo.InvariantCulture)}:{lost.At.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}";

    public static object?[]? Arguments(MethodInfo method, IReadOnlyDictionary<string, string> options)
    {
        var lookup = new Dictionary<string, string>(options, StringComparer.OrdinalIgnoreCase);
        var arguments = new List<object?>();
        foreach (var parameter in method.GetParameters())
        {
            var name = parameter.GetCustomAttribute<SummaryAttribute>()?.Name ?? parameter.Name ?? "";
            if (!lookup.TryGetValue(name, out var text))
            {
                if (!parameter.HasDefaultValue) return null;
                arguments.Add(parameter.DefaultValue);
                continue;
            }
            if (!TryConvert(text, parameter.ParameterType, out var value)) return null;
            arguments.Add(value);
        }
        return arguments.ToArray();
    }

    public static bool CanConvert(Type type)
    {
        var target = Nullable.GetUnderlyingType(type) ?? type;
        return target == typeof(string) || target.IsEnum || target == typeof(bool) || target == typeof(int) || target == typeof(long)
            || target == typeof(double) || target == typeof(float) || target == typeof(decimal);
    }

    private static bool TryConvert(string text, Type type, out object? value)
    {
        var target = Nullable.GetUnderlyingType(type) ?? type;
        var invariant = CultureInfo.InvariantCulture;
        value = null;
        if (target == typeof(string)) value = text;
        else if (target.IsEnum && Enum.TryParse(target, text, true, out var parsed) && Enum.IsDefined(target, parsed)) value = parsed;
        else if (target == typeof(bool) && bool.TryParse(text, out var flag)) value = flag;
        else if (target == typeof(int) && int.TryParse(text, NumberStyles.Integer, invariant, out var i)) value = i;
        else if (target == typeof(long) && long.TryParse(text, NumberStyles.Integer, invariant, out var l)) value = l;
        else if (target == typeof(double) && double.TryParse(text, NumberStyles.Float, invariant, out var d)) value = d;
        else if (target == typeof(float) && float.TryParse(text, NumberStyles.Float, invariant, out var f)) value = f;
        else if (target == typeof(decimal) && decimal.TryParse(text, NumberStyles.Float, invariant, out var m)) value = m;
        return value is not null;
    }

    private static IReadOnlyDictionary<string, ResubmitTarget> BuildCatalog()
    {
        var catalog = new Dictionary<string, ResubmitTarget>(StringComparer.OrdinalIgnoreCase);
        var modules = typeof(ResubmitService).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(IInteractionModuleBase).IsAssignableFrom(t));
        foreach (var module in modules)
        {
            var groups = new List<string>();
            for (var type = module; type is not null; type = type.DeclaringType)
                if (type.GetCustomAttribute<GroupAttribute>() is { } group) groups.Insert(0, group.Name);
            foreach (var method in module.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (method.GetCustomAttribute<SlashCommandAttribute>() is not { } command) continue;
                var path = string.Join(" ", (command.IgnoreGroupNames ? [] : groups).Append(command.Name));
                catalog[path] = new ResubmitTarget(module, method);
            }
        }
        return catalog;
    }

    public async Task<int> SendLostCommandsAsync(IEnumerable<LogEntry> lostCommands, string label)
    {
        if (_factionChannels is null) return 0;
        var sent = 0;
        var byFaction = lostCommands
            .Where(c => c.FactionId is not null)
            .Select(c => (Entry: c, Payload: CommandPayload.TryParse(c.Payload)))
            .Where(c => c.Payload is not null && IsPlayerAction(c.Payload.Command))
            .GroupBy(c => c.Entry.FactionId!.Value);
        foreach (var faction in byFaction)
        {
            if (_data.GetFaction(faction.Key) is not { IsActive: true }) continue;
            var commands = faction.ToList();
            var delivered = true;
            for (var start = 0; start < commands.Count; start += ButtonsPerMessage)
            {
                var lines = new List<string>();
                if (start == 0) lines.Add(Texts.Get("resubmit.header", ("label", label)));
                var buttons = new ComponentBuilder();
                var count = 0;
                foreach (var (index, (entry, payload)) in commands.Skip(start).Take(ButtonsPerMessage).Select((c, i) => (start + i + 1, c)))
                {
                    var display = Display.Truncate(payload!.Display, CommandDisplayLimit);
                    var at = Display.Timestamp(entry.At, 'f');
                    if (Find(payload.Command) is null)
                    {
                        lines.Add(Texts.Get("resubmit.lineManual", ("n", index), ("command", display), ("at", at)));
                        continue;
                    }
                    var lost = _data.LogAndRead(LogKinds.LostCommand, payload.Command, entry.Payload, faction.Key);
                    lines.Add(Texts.Get("resubmit.line", ("n", index), ("command", display), ("at", at)));
                    buttons.WithButton(
                        Display.Truncate(Texts.Get("resubmit.button", ("n", index), ("command", "/" + payload.Command)), ButtonLabelLimit),
                        ButtonId(lost), ButtonStyle.Primary, row: count / 5);
                    count++;
                }
                delivered &= await _factionChannels.SendAsync(faction.Key, string.Join("\n", lines), count > 0 ? buttons.Build() : null);
            }
            if (delivered) sent++;
        }
        return sent;
    }

    public ResubmitClaim Peek(long lostId, long at, ulong userId) => Check(lostId, at, userId, claim: false);

    public ResubmitClaim Claim(long lostId, long at, ulong userId) => Check(lostId, at, userId, claim: true);

    public void Release(long lostId) => _data.DeleteLogEntries(LogKinds.Resubmitted, lostId.ToString(CultureInfo.InvariantCulture));

    private ResubmitClaim Check(long lostId, long at, ulong userId, bool claim)
    {
        lock (ClaimGate)
        {
            if (_data.GetLogEntry(lostId) is not { Kind: LogKinds.LostCommand } lost || lost.At.ToUnixTimeSeconds() != at
                || CommandPayload.TryParse(lost.Payload) is not { } payload)
                return new ResubmitClaim(ResubmitClaimOutcome.Gone);
            if (lost.FactionId != userId)
                return new ResubmitClaim(ResubmitClaimOutcome.NotYours, payload, lost.FactionId);
            var id = lostId.ToString(CultureInfo.InvariantCulture);
            if (_data.GetLogEntries(LogKinds.Resubmitted, lost.At).Any(e => e.Payload == id))
                return new ResubmitClaim(ResubmitClaimOutcome.Used, payload, lost.FactionId);
            if (claim)
                _data.Log(LogKinds.Resubmitted, payload.Command, id, userId);
            return new ResubmitClaim(ResubmitClaimOutcome.Claimed, payload, lost.FactionId);
        }
    }
}
