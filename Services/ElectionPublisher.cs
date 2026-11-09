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
    private const string UnaffiliatedColour = "#808080";
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

    public Task PublishAsync(ElectionReport report, string source) =>
        PublishResultsAsync(report, ResultsText(report, _constants.Config), Texts.Get("election.chartTitle", ("term", report.TermNumber)), Proclamation(report), source);

    public Task PublishSenateAsync(ElectionReport report, string source)
    {
        var config = _constants.Config;
        var results = Texts.Get("election.senateHeader") + "\n" + ResultsBody(report.Result, report.Parties, config);
        var proclamation = Texts.Get("election.senateProclamation", ("seats", report.Result.PartySeats.Values.Sum() + report.Result.IndependentSeats));
        return PublishResultsAsync(report, results, Texts.Get("election.senateChartTitle"), proclamation, source);
    }

    private async Task PublishResultsAsync(ElectionReport report, string results, string chartTitle, string proclamation, string source)
    {
        if (_data.GetChannel(ChannelRole.News) is not { } channelId || _client.GetChannel(channelId) is not IMessageChannel channel)
        {
            await _alerts.PostAsync(Texts.Get("election.alert.noNews", ("term", report.TermNumber)));
            return;
        }

        var config = _constants.Config;
        try
        {
            foreach (var chunk in Publisher.Chunk(results))
                await SendAsync(channel, channelId, source, chunk);

            var winners = WinnersFromResult(report.Result);
            using var images = Render(report.Result, report.Parties, winners, config, out var failure);
            if (failure is not null)
                await _alerts.PostAsync(Texts.Get("election.alert.renderFailed", ("term", report.TermNumber), ("error", failure)));

            var attachments = images.Attachments();
            try
            {
                var embeds = Embeds(images, winners, report.Parties, config, chartTitle);
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

            await SendAsync(channel, channelId, source, proclamation);
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
        var chart = result.PartySeats
            .Where(kv => kv.Value > 0)
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key)
            .Select(kv => new PartyData
            {
                Name = byId.TryGetValue(kv.Key, out var party) ? party.Name : kv.Key.ToString(CultureInfo.InvariantCulture),
                SeatCount = kv.Value,
                HexColor = byId.TryGetValue(kv.Key, out var coloured) ? coloured.Colour : UnaffiliatedColour,
            })
            .ToList();
        if (result.IndependentSeats > 0)
            chart.Add(new PartyData { Name = Display.Independent, SeatCount = result.IndependentSeats, HexColor = UnaffiliatedColour });
        return chart;
    }

    public static IReadOnlyDictionary<string, long?> WinnersFromResult(ElectionResult result) =>
        result.Provinces.ToDictionary(p => p.ProvinceId, p => p.WinnerPartyId);

    public static Dictionary<string, long?> WinnersFromRows(IEnumerable<ProvinceResultRow> rows) =>
        rows.GroupBy(r => r.ProvinceId)
            .ToDictionary(
                g => g.Key,
                g => g.FirstOrDefault(r => r.Winner) is { } winner
                    ? winner.PartyId
                    : g.Any(r => r.Strength > 0)
                        ? (long?)g.OrderByDescending(r => r.Seats).ThenByDescending(r => r.Strength).ThenBy(r => r.PartyId).First().PartyId
                        : null);

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

    private static string PartyEmoji(IReadOnlyDictionary<long, ElectionPartySnapshot> parties, long id) =>
        parties.TryGetValue(id, out var party) ? PartyEmoji(party) : Texts.Get("election.unknownParty", ("id", id));

    public static string PartyEmoji(ElectionPartySnapshot party) =>
        party.EmojiId is { } emoji ? $"<:p:{emoji}>" : ColourSquare(party.Colour);

    public static string ColourSquare(string hex)
    {
        if (!ConstantsStore.IsHexColour(hex)) return "⬜";
        var r = Convert.ToInt32(hex[1..3], 16) / 255.0;
        var g = Convert.ToInt32(hex[3..5], 16) / 255.0;
        var b = Convert.ToInt32(hex[5..7], 16) / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var chroma = max - min;
        if (max < 0.2) return "⬛";
        if (max == 0 || chroma / max < 0.2) return max > 0.6 ? "⬜" : "⬛";

        var hue = max == r ? 60 * ((g - b) / chroma % 6)
            : max == g ? 60 * ((b - r) / chroma + 2)
            : 60 * ((r - g) / chroma + 4);
        if (hue < 0) hue += 360;

        return hue switch
        {
            < 15 or >= 345 => "🟥",
            < 40 => max < 0.55 ? "🟫" : "🟧",
            < 70 => "🟨",
            < 170 => "🟩",
            < 260 => "🟦",
            _ => "🟪",
        };
    }

    public static string PartyName(ElectionPartySnapshot party) => $"{PartyEmoji(party)} {party.Name}";

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
                ("party", PartyName(x.Party)),
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

    public static string ResultsText(ElectionReport report, GameConfig config) =>
        Texts.Get("election.header", ("term", report.TermNumber)) + "\n" + ResultsBody(report.Result, report.Parties, config);

    public static string ResultsBody(ElectionResult result, IReadOnlyList<ElectionPartySnapshot> parties, GameConfig config)
    {
        var byId = parties.ToDictionary(p => p.Id);
        var lines = new List<string> { Texts.Get("election.partiesHeading") };

        var partyIds = result.PartySeats.Keys.Union(result.VoteShare.Keys)
            .OrderByDescending(id => result.PartySeats.GetValueOrDefault(id))
            .ThenByDescending(id => result.VoteShare.GetValueOrDefault(id))
            .ThenBy(id => id)
            .ToList();
        lines.AddRange(partyIds.Select(id =>
        {
            var party = byId.TryGetValue(id, out var named) ? PartyName(named) : Texts.Get("election.unknownParty", ("id", id));
            var share = Percent(result.VoteShare.GetValueOrDefault(id));
            return result.BelowThreshold?.Contains(id) == true
                ? Texts.Get("election.partyLineBelowThreshold", ("party", party), ("share", share), ("threshold", Percent(config.Constants.Election.Threshold)))
                : Texts.Get("election.partyLine", ("party", party), ("seats", result.PartySeats.GetValueOrDefault(id)), ("share", share));
        }));
        if (result.IndependentSeats == 1) lines.Add(Texts.Get("election.independentLine"));
        if (result.IndependentSeats > 1) lines.Add(Texts.Get("election.independentsLine", ("seats", result.IndependentSeats)));
        if (partyIds.Count == 0 && result.IndependentSeats == 0) lines.Add(Texts.Get("election.nobody"));

        lines.Add("");
        lines.Add(Texts.Get("election.provincesHeading"));
        var outcomes = result.Provinces.ToDictionary(p => p.ProvinceId);
        foreach (var provinceId in OrderedProvinces(outcomes.Keys, config))
        {
            var outcome = outcomes[provinceId];
            var seats = outcome.ContestedSeatsByParty.Keys.Union(outcome.BoroughSeatsByParty.Keys)
                .Select(id => (Id: id, Seats: outcome.ContestedSeatsByParty.GetValueOrDefault(id) + outcome.BoroughSeatsByParty.GetValueOrDefault(id)))
                .Where(x => x.Seats > 0)
                .OrderByDescending(x => x.Id == outcome.WinnerPartyId)
                .ThenByDescending(x => x.Seats)
                .ThenByDescending(x => outcome.Strength.GetValueOrDefault(x.Id))
                .ThenBy(x => x.Id)
                .Select(x => Texts.Get("election.provinceSeat", ("party", PartyEmoji(byId, x.Id)), ("seats", x.Seats)))
                .ToList();
            if (outcome.IndependentBoroughSeats > 0)
                seats.Add(Texts.Get("election.provinceIndependent", ("seats", outcome.IndependentBoroughSeats)));
            lines.Add(Texts.Get("election.provinceLine",
                ("province", ProvinceName(config, provinceId)),
                ("seats", seats.Count == 0 ? Texts.Get("election.provinceEmpty") : string.Join(Texts.Get("election.provinceSeparator"), seats))));
        }
        return string.Join("\n", lines);
    }

    public static string Proclamation(ElectionReport report)
    {
        var seats = report.Result.PartySeats.Values.Sum();
        return Texts.Get("election.proclamation", ("term", report.TermNumber), ("seats", seats));
    }
}
