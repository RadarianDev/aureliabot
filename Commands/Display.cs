using System.Globalization;

public static class Display
{
    public static string Faction(Faction faction) =>
        string.IsNullOrWhiteSpace(faction.CharacterName)
            ? $"<@{faction.UserId}>"
            : $"<@{faction.UserId}> ({faction.CharacterName})";

    public static string Faction(DatabaseService data, ulong factionId) =>
        data.GetFaction(factionId) is { } faction ? Faction(faction) : $"<@{factionId}>";

    public static string Party(Party party) =>
        party.EmojiId is { } emoji ? $"<:p:{emoji}> {party.Name}" : party.Name;

    public static string Province(GameConfig config, string? provinceId) =>
        provinceId is null ? Texts.Get("display.none") : config.Province(provinceId)?.Name ?? provinceId;

    public static string? RegionalPopularity(DatabaseService data, GameConfig config, ulong factionId)
    {
        var regional = data.GetRegionalPopularity(factionId, config.Constants.Popularity);
        if (regional.Count == 0) return null;
        var entries = config.Provinces
            .Where(p => regional.ContainsKey(p.Id))
            .Select(p => Texts.Get("info.faction.regionalEntry", ("province", p.Name), ("popularity", Number(regional[p.Id]))));
        return Texts.Get("info.faction.regional", ("provinces", string.Join(" · ", entries)));
    }

    public static string Timestamp(DateTimeOffset at, char style = 'F') => $"<t:{at.ToUnixTimeSeconds()}:{style}>";

    public static string When(DateTimeOffset at) => $"{Timestamp(at)} ({Timestamp(at, 'R')})";

    public static string Number(double value) => value.ToString("0.#", CultureInfo.InvariantCulture);

    public static string Signed(double value) =>
        (value >= 0 ? "+" : "−") + Math.Abs(value).ToString("0.#", CultureInfo.InvariantCulture);

    public static string Crowns(decimal value) => Texts.Get("display.crowns", ("amount", value.ToString("0.0", CultureInfo.InvariantCulture)));

    public static string NextResolution(GameState state) =>
        state.NextResolutionAt is { } next ? When(next) : Texts.Get("display.notScheduled");

    public static string Independent => Texts.Get("display.independent");

    public static string Truncate(string text, int limit = 2000) =>
        text.Length > limit ? text[..(limit - 3)] + "..." : text;
}
