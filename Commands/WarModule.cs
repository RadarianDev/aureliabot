using Discord;
using Discord.Interactions;

[Group("war", "The Minister of War's commands for the divisions")]
public class WarModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly DatabaseService _data;
    private readonly ConstantsStore _constants;
    private readonly Confirmations _confirmations;

    public WarModule(DatabaseService data, ConstantsStore constants, Confirmations confirmations)
    {
        _data = data;
        _constants = constants;
        _confirmations = confirmations;
    }

    [SlashCommand("raise", "Raise a new division, paid now and formed at the next resolution")]
    [RequireFaction]
    public async Task RaiseAsync(
        [Summary("name", "The new division's name")] string name,
        [Summary("province", "Where it is raised"), Autocomplete(typeof(ProvinceAutocomplete))] string province) =>
        await Reply(ForcesCommands.Raise(_data, _constants.Config, UnitType.Division, Context.User.Id, name, province));

    [SlashCommand("train", "Train a division one step, paid now and done at the next resolution")]
    [RequireFaction]
    public async Task TrainAsync([Summary("division", "The division to train"), Autocomplete(typeof(DivisionAutocomplete))] string division) =>
        await Reply(ForcesCommands.Train(_data, _constants.Config, UnitType.Division, Context.User.Id, division));

    [SlashCommand("station", "Move a division to another province at the next resolution, for free")]
    [RequireFaction]
    public async Task StationAsync(
        [Summary("division", "The division to move"), Autocomplete(typeof(DivisionAutocomplete))] string division,
        [Summary("province", "Where to station it"), Autocomplete(typeof(ProvinceAutocomplete))] string province) =>
        await Reply(ForcesCommands.Station(_data, _constants.Config, UnitType.Division, Context.User.Id, division, province));

    [SlashCommand("disband", "Disband a division at once; nothing is refunded")]
    [RequireFaction]
    public async Task DisbandAsync([Summary("division", "The division to disband"), Autocomplete(typeof(DivisionAutocomplete))] string division) =>
        await ForcesCommands.DisbandAsync(_data, _constants.Config, _confirmations, UnitType.Division, Context.User.Id, division,
            (text, components) => RespondAsync(text, components: components, ephemeral: true, allowedMentions: AllowedMentions.None));

    private Task Reply(string text) => RespondAsync(Display.Truncate(text), ephemeral: true, allowedMentions: AllowedMentions.None);
}
