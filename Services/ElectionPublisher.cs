using System.Globalization;
using Discord;
using Discord.WebSocket;

public sealed record ElectionImages(string? SeatChartPath, string? MapPath) : IDisposable
{
    public const string SeatChartFile = "seats.png";
    public const string MapFile = "map.png";

    public List<FileAttachment> Attachments()
    {
        var list = new List<FileAttachment>();
        if (SeatChartPath is not null) list.Add(new FileAttachment(SeatChartPath, SeatChartFile));
        if (MapPath is not null) list.Add(new FileAttachment(MapPath, MapFile));
        return list;
    }

    public void Dispose()
    {
        foreach (var path in new[] { SeatChartPath, MapPath })
            if (path is not null)
                try { File.Delete(path); } catch (Exception) { }
    }
}

public class ElectionPublisher
{
    private static readonly Color EmbedColour = new(0xB8, 0x86, 0x0B);

    private readonly DiscordSocketClient _client;
    private readonly DatabaseService _data;
    private readonly ConstantsStore _constants;
    private readonly AlertService _alerts;

    public ElectionPublisher(DiscordSocketClient client, DatabaseService data, ConstantsStore constants, AlertService alerts)
    {
        _client = client;
        _data = data;
        _constants = constants;
        _alerts = alerts;
    }

    public async Task PublishAsync(ElectionReport report, string source)
    {
        if (_data.GetChannel(ChannelRole.News) is not { } channelId || _client.GetChannel(channelId) is not IMessageChannel channel)
        {
            await _alerts.PostAsync(Texts.Get("election.alert.noNews", ("term", report.TermNumber)));
            return;
        }

        var config = _constants.Config;
        try
        {
            var text = ResultsText(report, config, id => Display.Faction(_data, id));
            foreach (var chunk in Publisher.Chunk(text))
                await SendAsync(channel, channelId, source, chunk);

            var winners = WinnersFromResult(report.Result);
            using var images = Render(report.Result, report.Parties, winners, config, out var failure);
            if (failure is not null)
                await _alerts.PostAsync(Texts.Get("election.alert.renderFailed", ("term", report.TermNumber), ("error", failure)));

            var attachments = images.Attachments();
            try
            {
                var embeds = Embeds(images, winners, report.Parties, config, Texts.Get("election.chartTitle", ("term", report.TermNumber)));
                if (attachments.Count > 0)
                {
                    var message = await channel.SendFilesAsync(attachments, embeds: embeds, allowedMentions: AllowedMentions.None);
                    _data.LogPost(channelId, message.Id, source);
                }
            }
            finally
            {
                foreach (var attachment in attachments) attachment.Dispose();
            }

            await SendAsync(channel, channelId, source, Proclamation(report));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ElectionPublisher] {ex}");
            await _alerts.PostAsync(Texts.Get("election.alert.postFailed", ("term", report.TermNumber), ("error", ex.Message)));
        }
    }

    private async Task SendAsync(IMessageChannel channel, ulong channelId, string source, string text)
    {
        var message = await channel.SendMessageAsync(text, allowedMentions: AllowedMentions.None);
        _data.LogPost(channelId, message.Id, source);
    }

    public static Embed[] Embeds(
        ElectionImages images,
        IReadOnlyDictionary<string, long?> winners,
        IReadOnlyList<ElectionPartySnapshot> parties,
        GameConfig config,
        string chartTitle)
    {
        var embeds = new List<Embed>();
        if (images.SeatChartPath is not null)
            embeds.Add(new EmbedBuilder()
                .WithTitle(chartTitle)
                .WithColor(EmbedColour)
                .WithImageUrl($"attachment://{ElectionImages.SeatChartFile}")
                .Build());
        if (images.MapPath is not null)
            embeds.Add(MapEmbed(winners, parties, config, Texts.Get("election.mapTitle")));
        return embeds.ToArray();
    }

    public static Embed MapEmbed(IReadOnlyDictionary<string, long?> winners, IReadOnlyList<ElectionPartySnapshot> parties, GameConfig config, string title) =>
        new EmbedBuilder()
            .WithTitle(title)
            .WithColor(EmbedColour)
            .WithDescription(Display.Truncate(Legend(winners, parties, config), 4096))
            .WithImageUrl($"attachment://{ElectionImages.MapFile}")
            .Build();

    public static ElectionImages Render(
        ElectionResult? result,
        IReadOnlyList<ElectionPartySnapshot> parties,
        IReadOnlyDictionary<string, long?> winners,
        GameConfig config,
        out string? failure)
    {
        failure = null;
        string? chart = null;
        string? map = null;
        if (result is not null)
        {
            try
            {
                chart = RenderSeatChart(SeatChartParties(result, parties));
            }
            catch (Exception ex)
            {
                failure = ex.Message;
            }
        }
        try
        {
            map = RenderMap(MapColours(winners, parties, config));
        }
        catch (Exception ex)
        {
            failure = failure is null ? ex.Message : failure + "; " + ex.Message;
        }
        return new ElectionImages(chart, map);
    }

    private static string TempPng(string kind) => Path.Combine(Path.GetTempPath(), $"aurelia-{kind}-{Guid.NewGuid():N}.png");

    public static string? RenderSeatChart(List<PartyData> parties)
    {
        if (parties.Count == 0) return null;
        var path = TempPng("seats");
        try
        {
            new ArchGenerator().Generate(parties, path);
        }
        catch
        {
            try { File.Delete(path); } catch (Exception) { }
            throw;
        }
        return File.Exists(path) ? path : null;
    }

    public static string RenderMap(IReadOnlyDictionary<string, string> colours)
    {
        var path = TempPng("map");
        try
        {
            new ProvinceMapGenerator().Generate(colours, path);
        }
        catch
        {
            if (File.Exists(path)) File.Delete(path);
            throw;
        }
        return path;
    }

    public static List<PartyData> SeatChartParties(ElectionResult result, IReadOnlyList<ElectionPartySnapshot> parties)
    {
        var byId = parties.ToDictionary(p => p.Id);
        return result.PartySeats
            .Where(kv => kv.Value > 0)
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key)
            .Select(kv => new PartyData
            {
                Name = byId.TryGetValue(kv.Key, out var party) ? party.Name : kv.Key.ToString(CultureInfo.InvariantCulture),
                SeatCount = kv.Value,
                HexColor = byId.TryGetValue(kv.Key, out var coloured) ? coloured.Colour : "#808080",
            })
            .ToList();
    }

    public static IReadOnlyDictionary<string, long?> WinnersFromResult(ElectionResult result) =>
        result.Provinces.ToDictionary(p => p.ProvinceId, p => p.WinnerPartyId);

    public static Dictionary<string, long?> WinnersFromRows(IEnumerable<ProvinceResultRow> rows) =>
        rows.GroupBy(r => r.ProvinceId)
            .ToDictionary(
                g => g.Key,
                g => g.FirstOrDefault(r => r.Winner) is { } winner
                    ? winner.PartyId
                    : g.Where(r => r.Strength > 0).OrderByDescending(r => r.Strength).ThenByDescending(r => r.Seats).ThenBy(r => r.PartyId)
                        .Select(r => (long?)r.PartyId).FirstOrDefault());

    public static Dictionary<string, string> MapColours(
        IReadOnlyDictionary<string, long?> provinceWinners,
        IReadOnlyList<ElectionPartySnapshot> parties,
        GameConfig config)
    {
        var colours = parties.ToDictionary(p => p.Id, p => p.Colour);
        var result = new Dictionary<string, string>();
        foreach (var (provinceId, winner) in provinceWinners)
        {
            if (winner is not { } partyId || !colours.TryGetValue(partyId, out var colour)) continue;
            if (config.Province(provinceId) is not { } province || string.IsNullOrEmpty(province.MapKey)) continue;
            result[province.MapKey] = colour;
        }
        return result;
    }

    public static string PartyLabel(ElectionPartySnapshot party) =>
        party.EmojiId is { } emoji ? $"<:p:{emoji}> {party.Name}" : party.Name;

    private static string PartyLabel(IReadOnlyDictionary<long, ElectionPartySnapshot> parties, long id) =>
        parties.TryGetValue(id, out var party) ? PartyLabel(party) : Texts.Get("election.unknownParty", ("id", id));

    private static string ProvinceName(GameConfig config, string id) => config.Province(id)?.Name ?? id;

    private static string Percent(double share) => (share * 100).ToString("0.0", CultureInfo.InvariantCulture);

    public static string Legend(IReadOnlyDictionary<string, long?> winners, IReadOnlyList<ElectionPartySnapshot> parties, GameConfig config)
    {
        var lines = parties
            .Select(p => (Party: p, Provinces: OrderedProvinces(winners.Where(w => w.Value == p.Id).Select(w => w.Key), config)))
            .Where(x => x.Provinces.Count > 0)
            .OrderByDescending(x => x.Provinces.Count)
            .ThenBy(x => x.Party.Id)
            .Select(x => Texts.Get("election.legendLine",
                ("party", PartyLabel(x.Party)),
                ("colour", x.Party.Colour.ToUpperInvariant()),
                ("count", x.Provinces.Count),
                ("provinces", string.Join(", ", x.Provinces.Select(id => ProvinceName(config, id))))))
            .ToList();
        var known = parties.Select(p => p.Id).ToHashSet();
        var unclaimed = OrderedProvinces(winners.Where(w => w.Value is not { } id || !known.Contains(id)).Select(w => w.Key), config);
        if (unclaimed.Count > 0)
            lines.Add(Texts.Get("election.legendUnclaimed", ("provinces", string.Join(", ", unclaimed.Select(id => ProvinceName(config, id))))));
        return lines.Count == 0 ? Texts.Get("election.legendEmpty") : string.Join("\n", lines);
    }

    private static List<string> OrderedProvinces(IEnumerable<string> ids, GameConfig config)
    {
        var order = config.Provinces.Select((p, i) => (p.Id, i)).ToDictionary(x => x.Id, x => x.i);
        return ids.OrderBy(id => order.GetValueOrDefault(id, int.MaxValue)).ThenBy(id => id, StringComparer.Ordinal).ToList();
    }

    public static string ResultsText(ElectionReport report, GameConfig config, Func<ulong, string> factionName) =>
        Texts.Get("election.header", ("term", report.TermNumber)) + "\n" + ResultsBody(report.Result, report.Parties, config, factionName);

    public static string ResultsBody(ElectionResult result, IReadOnlyList<ElectionPartySnapshot> parties, GameConfig config, Func<ulong, string> factionName)
    {
        var byId = parties.ToDictionary(p => p.Id);
        var lines = new List<string> { Texts.Get("election.partiesHeading") };

        var partyIds = result.PartySeats.Keys.Union(result.VoteShare.Keys)
            .OrderByDescending(id => result.PartySeats.GetValueOrDefault(id))
            .ThenByDescending(id => result.VoteShare.GetValueOrDefault(id))
            .ThenBy(id => id)
            .ToList();
        lines.AddRange(partyIds.Select(id => Texts.Get("election.partyLine",
            ("party", PartyLabel(byId, id)),
            ("seats", result.PartySeats.GetValueOrDefault(id)),
            ("share", Percent(result.VoteShare.GetValueOrDefault(id))))));
        if (partyIds.Count == 0) lines.Add(Texts.Get("election.nobody"));

        lines.Add("");
        lines.Add(Texts.Get("election.factionsHeading"));
        var factions = result.FactionSeats.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).ToList();
        lines.AddRange(factions.Select(kv => Texts.Get("election.factionLine", ("faction", factionName(kv.Key)), ("seats", kv.Value))));
        if (factions.Count == 0) lines.Add(Texts.Get("election.nobody"));

        lines.Add("");
        lines.Add(Texts.Get("election.provincesHeading"));
        var outcomes = result.Provinces.ToDictionary(p => p.ProvinceId);
        foreach (var provinceId in OrderedProvinces(outcomes.Keys, config))
        {
            var outcome = outcomes[provinceId];
            var seats = outcome.ContestedSeatsByParty.Keys.Union(outcome.BoroughSeatsByParty.Keys)
                .Select(id => (Id: id, Seats: outcome.ContestedSeatsByParty.GetValueOrDefault(id) + outcome.BoroughSeatsByParty.GetValueOrDefault(id)))
                .Where(x => x.Seats > 0)
                .OrderByDescending(x => x.Seats)
                .ThenBy(x => x.Id)
                .Select(x => Texts.Get("election.provinceSeat", ("party", PartyLabel(byId, x.Id)), ("seats", x.Seats)))
                .ToList();
            lines.Add(Texts.Get("election.provinceLine",
                ("province", ProvinceName(config, provinceId)),
                ("seats", seats.Count == 0 ? Texts.Get("election.provinceEmpty") : string.Join(", ", seats))));
        }
        return string.Join("\n", lines);
    }

    public static string Proclamation(ElectionReport report)
    {
        var seats = report.Result.PartySeats.Values.Sum();
        return Texts.Get("election.proclamation", ("term", report.TermNumber), ("seats", seats));
    }
}
