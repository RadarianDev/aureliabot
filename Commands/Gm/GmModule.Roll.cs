using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;

public class BattleSideAutocomplete : AutocompleteHandler
{
    public override Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context, IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services)
    {
        var typed = autocompleteInteraction.Data.Current.Value?.ToString() ?? "";
        var cut = typed.LastIndexOf(',');
        var prefix = cut < 0 ? "" : typed[..(cut + 1)] + " ";
        var current = (cut < 0 ? typed : typed[(cut + 1)..]).Trim();
        var config = services.GetRequiredService<ConstantsStore>().Config;

        var results = new List<AutocompleteResult>();
        if (BattleRolls.TryParseDice(current, out var dice))
            results.Add(Suggestion(prefix, Texts.Get("roll.battle.autocompleteDice", ("dice", dice.Label)), dice.Label));
        results.AddRange(services.GetRequiredService<DatabaseService>().GetUnits()
            .Select(u => (Unit: u, Label: Texts.Get("roll.battle.autocompleteUnit",
                ("id", u.Id), ("name", u.Name), ("sides", u.Sides), ("province", Display.Province(config, u.ProvinceId)))))
            .Where(x => x.Label.Contains(current, StringComparison.OrdinalIgnoreCase))
            .Select(x => Suggestion(prefix, x.Label, x.Unit.Id.ToString())));
        return Task.FromResult(AutocompletionResult.FromSuccess(results.Where(r => ((string)r.Value).Length <= 100).Take(25)));
    }

    private static AutocompleteResult Suggestion(string prefix, string label, string value) =>
        new(Display.Truncate(prefix + label, 100), prefix + value);
}

public partial class GmModule
{
    [Group("roll", "Roll a battle or a riot")]
    [RequireUserPermission(GuildPermission.ManageGuild)]
    public class RollGroup : InteractionModuleBase<SocketInteractionContext>
    {
        private readonly DatabaseService _data;
        private readonly ConstantsStore _constants;
        private readonly Confirmations _confirmations;

        public RollGroup(DatabaseService data, ConstantsStore constants, Confirmations confirmations)
        {
            _data = data;
            _constants = constants;
            _confirmations = confirmations;
        }

        [SlashCommand("battle", "Roll a battle between two sides of unit ids or dice such as 6d12+2")]
        public async Task BattleAsync(
            [Summary("attackers", "Unit ids or names and dice, separated by commas"), Autocomplete(typeof(BattleSideAutocomplete))] string attackers,
            [Summary("defenders", "Unit ids or names and dice, separated by commas"), Autocomplete(typeof(BattleSideAutocomplete))] string defenders)
        {
            var units = _data.GetUnits();
            var attack = BattleRolls.ParseSide(attackers, units);
            var defence = BattleRolls.ParseSide(defenders, units);
            if (attack.Side is null || defence.Side is null)
            {
                await Reply(attack.Side is null
                    ? Texts.Get("roll.battle.badSide", ("side", Texts.Get("roll.battle.attackers")), ("error", attack.Error))
                    : Texts.Get("roll.battle.badSide", ("side", Texts.Get("roll.battle.defenders")), ("error", defence.Error)));
                return;
            }
            if (BattleRolls.SharedUnit(attack.Side, defence.Side) is { } shared)
            {
                await Reply(Texts.Get("roll.battle.bothSides", ("name", shared)));
                return;
            }

            var result = BattleRolls.Fight(attack.Side, defence.Side, new SeededRandomSource(SeededRandomSource.NewSeed()));
            var text = Describe(result);
            _data.Log(LogKinds.GmOverride, "gm roll battle", text, Context.User.Id);

            if (result.Losses.Count == 0)
            {
                await Reply(text);
                return;
            }
            var gmId = Context.User.Id;
            var ids = result.Losses.Select(u => u.Id).ToList();
            await RespondAsync(Display.Truncate(text + "\n" + Texts.Get("roll.battle.confirmHint")), components: _confirmations.Create(gmId, () => Task.FromResult(text + "\n\n" + RemoveLosses(ids, gmId)),
                Texts.Get("roll.battle.button")), ephemeral: true, allowedMentions: AllowedMentions.None);
        }

        [SlashCommand("riot", "Roll the gendarmerie of a province against its discontent")]
        public async Task RiotAsync(
            [Summary("province", "The rioting province"), Autocomplete(typeof(ProvinceAutocomplete))] string province)
        {
            var config = _constants.Config;
            if (ProvinceAutocomplete.Find(config, province) is not { } definition || _data.GetProvince(definition.Id) is not { } target)
            {
                await Reply(Texts.Get("roll.riot.noProvince", ("province", province)));
                return;
            }

            var gendarmerie = _data.GetUnits(UnitType.Gendarmerie).Where(u => u.ProvinceId == target.Id).ToList();
            var result = BattleRolls.Riot(gendarmerie, target.Discontent, new SeededRandomSource(SeededRandomSource.NewSeed()));
            var lines = new List<string>
            {
                Texts.Get("roll.riot.heading", ("province", definition.Name), ("discontent", Display.Number(target.Discontent)), ("sides", result.DieSides)),
            };
            if (result.Rolls.Count == 0)
                lines.Add(Texts.Get("roll.riot.noGendarmerie"));
            else
                lines.AddRange(result.Rolls.Select(UnitLine));
            lines.Add(result.Held
                ? Texts.Get("roll.riot.held", ("total", result.Total), ("roll", result.DieRoll))
                : Texts.Get("roll.riot.overwhelmed", ("total", result.Total), ("roll", result.DieRoll)));
            lines.Add(Texts.Get("roll.riot.hint"));
            var text = string.Join("\n", lines);
            _data.Log(LogKinds.GmOverride, "gm roll riot", text, Context.User.Id);
            await Reply(text);
        }

        private string RemoveLosses(IReadOnlyList<long> ids, ulong gmId)
        {
            var removed = _data.RemoveUnitsAsGm(ids, gmId, "gm roll battle");
            return removed.Count == 0
                ? Texts.Get("roll.battle.nothingRemoved")
                : Texts.Get("roll.battle.removed", ("units", string.Join(", ", removed.Select(u =>
                    Texts.Get("roll.battle.removedUnit", ("name", u.Name), ("id", u.Id))))));
        }

        private static string Describe(BattleResult result)
        {
            var lines = new List<string> { Texts.Get("roll.battle.heading") };
            lines.AddRange(SideLines(Texts.Get("roll.battle.attackers"), result.Attackers));
            lines.AddRange(SideLines(Texts.Get("roll.battle.defenders"), result.Defenders));

            var winner = result.Winner.Total;
            var loser = result.Loser.Total;
            lines.Add(result.AttackersWin
                ? Texts.Get("roll.battle.attackersWin", ("winner", winner), ("loser", loser))
                : winner == loser
                    ? Texts.Get("roll.battle.tie", ("total", winner))
                    : Texts.Get("roll.battle.defendersWin", ("winner", winner), ("loser", loser)));
            if (result.Rout) lines.Add(Texts.Get("roll.battle.rout"));

            if (result.Losses.Count == 0)
                lines.Add(Texts.Get("roll.battle.noLosses"));
            else
            {
                var rolls = result.Loser.Units.ToDictionary(u => u.Unit.Id, u => u.Roll);
                lines.Add(Texts.Get("roll.battle.losses", ("units", string.Join(", ", result.Losses.Select(u =>
                    Texts.Get("roll.battle.lossUnit", ("name", u.Name), ("id", u.Id), ("roll", rolls[u.Id])))))));
            }
            return string.Join("\n", lines);
        }

        private static IEnumerable<string> SideLines(string side, SideRoll roll)
        {
            yield return Texts.Get("roll.battle.side", ("side", side), ("total", roll.Total));
            foreach (var unit in roll.Units)
                yield return UnitLine(unit);
            foreach (var dice in roll.Dice)
                yield return Texts.Get("roll.battle.diceLine",
                    ("dice", dice.Dice.Label), ("rolls", string.Join(", ", dice.Rolls)), ("total", dice.Total));
        }

        private static string UnitLine(UnitRoll roll) =>
            Texts.Get("roll.unitLine", ("name", roll.Unit.Name), ("id", roll.Unit.Id), ("sides", roll.Unit.Sides), ("roll", roll.Roll));

        private Task Reply(string text) => RespondAsync(Display.Truncate(text), ephemeral: true, allowedMentions: AllowedMentions.None);
    }
}
