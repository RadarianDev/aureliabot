using Discord;
using Discord.Interactions;

[Group("interior", "The Minister of the Interior's commands for the gendarmerie")]
public class InteriorModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly DatabaseService _data;
    private readonly ConstantsStore _constants;
    private readonly Confirmations _confirmations;

    public InteriorModule(DatabaseService data, ConstantsStore constants, Confirmations confirmations)
    {
        _data = data;
        _constants = constants;
        _confirmations = confirmations;
    }

    [SlashCommand("raise", "Raise a new gendarmerie corps, paid now and formed at the next resolution")]
    [RequireFaction]
    public async Task RaiseAsync(
        [Summary("name", "The new corps's name")] string name,
        [Summary("province", "Where it is raised"), Autocomplete(typeof(ProvinceAutocomplete))] string province) =>
        await Reply(ForcesCommands.Raise(_data, _constants.Config, UnitType.Gendarmerie, Context.User.Id, name, province));

    [SlashCommand("train", "Train a corps one step, paid now and done at the next resolution")]
    [RequireFaction]
    public async Task TrainAsync([Summary("corps", "The corps to train"), Autocomplete(typeof(GendarmerieAutocomplete))] string corps) =>
        await Reply(ForcesCommands.Train(_data, _constants.Config, UnitType.Gendarmerie, Context.User.Id, corps));

    [SlashCommand("station", "Move a corps to another province at the next resolution, for free")]
    [RequireFaction]
    public async Task StationAsync(
        [Summary("corps", "The corps to move"), Autocomplete(typeof(GendarmerieAutocomplete))] string corps,
        [Summary("province", "Where to station it"), Autocomplete(typeof(ProvinceAutocomplete))] string province) =>
        await Reply(ForcesCommands.Station(_data, _constants.Config, UnitType.Gendarmerie, Context.User.Id, corps, province));

    [SlashCommand("disband", "Disband a corps at once; nothing is refunded")]
    [RequireFaction]
    public async Task DisbandAsync([Summary("corps", "The corps to disband"), Autocomplete(typeof(GendarmerieAutocomplete))] string corps) =>
        await ForcesCommands.DisbandAsync(_data, _constants.Config, _confirmations, UnitType.Gendarmerie, Context.User.Id, corps,
            (text, components) => RespondAsync(text, components: components, ephemeral: true, allowedMentions: AllowedMentions.None));

    [SlashCommand("crackdown", "Order a crackdown in a province, rolled at the next resolution")]
    [RequireFaction]
    public async Task CrackdownAsync(
        [Summary("province", "A province with gendarmerie"), Autocomplete(typeof(CrackdownProvinceAutocomplete))] string province) =>
        await Reply(ForcesCommands.Crackdown(_data, _constants.Config, Context.User.Id, province));

    private Task Reply(string text) => RespondAsync(Display.Truncate(text), ephemeral: true, allowedMentions: AllowedMentions.None);
}
