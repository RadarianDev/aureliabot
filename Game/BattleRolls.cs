using System.Globalization;
using System.Text.RegularExpressions;

public sealed record BattleDice(int Count, int Sides, int Bonus)
{
    public string Label =>
        $"{Count.ToString(CultureInfo.InvariantCulture)}d{Sides.ToString(CultureInfo.InvariantCulture)}"
        + (Bonus switch
        {
            > 0 => "+" + Bonus.ToString(CultureInfo.InvariantCulture),
            < 0 => Bonus.ToString(CultureInfo.InvariantCulture),
            _ => "",
        });
}

public sealed record BattleSide(IReadOnlyList<Unit> Units, IReadOnlyList<BattleDice> Dice);

public sealed record BattleSideParse(BattleSide? Side, string? Error);

public sealed record UnitRoll(Unit Unit, int Roll);

public sealed record DiceRoll(BattleDice Dice, IReadOnlyList<int> Rolls)
{
    public int Total => Rolls.Sum() + Dice.Bonus;
}

public sealed record SideRoll(IReadOnlyList<UnitRoll> Units, IReadOnlyList<DiceRoll> Dice)
{
    public int Total => Units.Sum(u => u.Roll) + Dice.Sum(d => d.Total);
}

public sealed record BattleResult(SideRoll Attackers, SideRoll Defenders, bool AttackersWin, bool Rout, IReadOnlyList<Unit> Losses)
{
    public SideRoll Winner => AttackersWin ? Attackers : Defenders;
    public SideRoll Loser => AttackersWin ? Defenders : Attackers;
}

public sealed record RiotResult(IReadOnlyList<UnitRoll> Rolls, int DieSides, int DieRoll)
{
    public int Total => Rolls.Sum(r => r.Roll);
    public bool Held => Total >= DieRoll;
}

public static partial class BattleRolls
{
    public const int MaxDiceCount = 100;
    public const int MaxDiceSides = 1000;
    public const int MaxBonus = 1000;

    public static bool TryParseDice(string text, out BattleDice dice)
    {
        dice = new BattleDice(0, 0, 0);
        var match = DicePattern().Match(text.Trim());
        if (!match.Success) return false;
        var count = match.Groups[1].Success ? ParseInt(match.Groups[1].Value) : 1;
        var sides = ParseInt(match.Groups[2].Value);
        var bonus = match.Groups[3].Success ? ParseInt(match.Groups[4].Value) * (match.Groups[3].Value == "-" ? -1 : 1) : 0;
        if (count is < 1 or > MaxDiceCount || sides is < 1 or > MaxDiceSides || Math.Abs(bonus) > MaxBonus) return false;
        dice = new BattleDice(count, sides, bonus);
        return true;
    }

    public static BattleSideParse ParseSide(string text, IReadOnlyList<Unit> units)
    {
        var chosen = new List<Unit>();
        var dice = new List<BattleDice>();
        foreach (var part in text.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var tokens = part.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var resolved = tokens.Select(t => Resolve(t, units)).ToList();
            if (resolved.All(r => r is not null))
            {
                foreach (var item in resolved)
                {
                    if (item is Unit unit) chosen.Add(unit);
                    else dice.Add((BattleDice)item!);
                }
                continue;
            }

            if (units.FirstOrDefault(u => u.Name.Equals(part, StringComparison.OrdinalIgnoreCase)) is { } named)
            {
                chosen.Add(named);
                continue;
            }
            return new BattleSideParse(null, Texts.Get("roll.battle.unknownEntry", ("entry", part)));
        }

        if (chosen.Count == 0 && dice.Count == 0) return new BattleSideParse(null, Texts.Get("roll.battle.emptySide"));
        if (chosen.GroupBy(u => u.Id).FirstOrDefault(g => g.Count() > 1) is { } twice)
            return new BattleSideParse(null, Texts.Get("roll.battle.unitTwice", ("name", twice.First().Name)));
        return new BattleSideParse(new BattleSide(chosen, dice), null);
    }

    private static object? Resolve(string token, IReadOnlyList<Unit> units)
    {
        var trimmed = token.TrimStart('#');
        if (long.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            return units.FirstOrDefault(u => u.Id == id);
        return TryParseDice(token, out var dice) ? dice : null;
    }

    public static string? SharedUnit(BattleSide attackers, BattleSide defenders) =>
        attackers.Units.FirstOrDefault(a => defenders.Units.Any(d => d.Id == a.Id))?.Name;

    public static BattleResult Fight(BattleSide attackers, BattleSide defenders, IRandomSource random)
    {
        var attack = Roll(attackers, random);
        var defence = Roll(defenders, random);
        var attackersWin = attack.Total > defence.Total;
        var winner = attackersWin ? attack : defence;
        var loser = attackersWin ? defence : attack;
        var rout = winner.Total > loser.Total && winner.Total >= 2 * loser.Total;
        var losses = loser.Units
            .OrderBy(u => u.Roll)
            .ThenBy(u => u.Unit.Sides)
            .ThenByDescending(u => u.Unit.Id)
            .Take(rout ? 2 : 1)
            .Select(u => u.Unit)
            .ToList();
        return new BattleResult(attack, defence, attackersWin, rout, losses);
    }

    public static RiotResult Riot(IEnumerable<Unit> gendarmerie, double discontent, IRandomSource random)
    {
        var rolls = gendarmerie.Select(u => new UnitRoll(u, random.RollDie(u.Sides))).ToList();
        var sides = DatabaseService.CrackdownDieSides(discontent);
        return new RiotResult(rolls, sides, random.RollDie(sides));
    }

    private static SideRoll Roll(BattleSide side, IRandomSource random)
    {
        var units = side.Units.Select(u => new UnitRoll(u, random.RollDie(u.Sides))).ToList();
        var dice = side.Dice
            .Select(d => new DiceRoll(d, Enumerable.Range(0, d.Count).Select(_ => random.RollDie(d.Sides)).ToList()))
            .ToList();
        return new SideRoll(units, dice);
    }

    private static int ParseInt(string text) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : int.MaxValue;

    [GeneratedRegex(@"^(\d+)?[dD](\d+)(?:\s*([+-])\s*(\d+))?$")]
    private static partial Regex DicePattern();
}
