public enum AgendaTrack { Economy, Reserve, Army, Discontent, Favour, Threat }

public enum AgendaDirection { Up, Down }

public sealed record AgendaWish(AgendaTrack Track, AgendaDirection Direction);

public sealed record AgendaDefinition(string Id, string Name, string Description, IReadOnlyList<AgendaWish> Wishes);

public sealed record AgendaRewardFile
{
    public double PerWish { get; init; }
    public double AllBonus { get; init; }
}

public sealed record AgendaEntryFile
{
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public Dictionary<string, string> Wants { get; init; } = [];
}

public sealed record AgendasYaml
{
    public AgendaRewardFile Reward { get; init; } = new();
    public Dictionary<string, double> Margins { get; init; } = [];
    public Dictionary<string, AgendaEntryFile> Agendas { get; init; } = [];
}

public sealed class AgendaConfig
{
    public static readonly AgendaConfig None = new(new AgendaRewardFile(), new Dictionary<AgendaTrack, double>(), []);

    public AgendaRewardFile Reward { get; }
    public IReadOnlyDictionary<AgendaTrack, double> Margins { get; }
    public IReadOnlyList<AgendaDefinition> Agendas { get; }

    public AgendaConfig(AgendaRewardFile reward, IReadOnlyDictionary<AgendaTrack, double> margins, IReadOnlyList<AgendaDefinition> agendas)
    {
        Reward = reward;
        Margins = margins;
        Agendas = agendas;
    }

    public AgendaDefinition? Find(string id) => Agendas.FirstOrDefault(a => a.Id == id);

    public double MarginOf(AgendaTrack track) => Margins.GetValueOrDefault(track);

    public static (AgendaConfig Config, List<string> Errors) From(AgendasYaml file)
    {
        var errors = new List<string>();
        if (file.Reward.PerWish < 0 || file.Reward.AllBonus < 0) errors.Add("agendas.yaml: reward.perWish and reward.allBonus must not be negative.");

        var margins = new Dictionary<AgendaTrack, double>();
        foreach (var (name, margin) in file.Margins)
        {
            if (!Enum.TryParse<AgendaTrack>(name, ignoreCase: true, out var track)) errors.Add($"agendas.yaml: margins names unknown track '{name}'.");
            else if (margin < 0) errors.Add($"agendas.yaml: margin for '{name}' must not be negative.");
            else margins[track] = margin;
        }

        var agendas = new List<AgendaDefinition>();
        foreach (var (id, entry) in file.Agendas)
        {
            if (string.IsNullOrWhiteSpace(entry.Name)) errors.Add($"agendas.yaml: agenda '{id}' has no name.");
            if (entry.Wants.Count == 0) errors.Add($"agendas.yaml: agenda '{id}' wants nothing.");
            var wishes = new List<AgendaWish>();
            foreach (var (trackName, directionName) in entry.Wants)
            {
                if (!Enum.TryParse<AgendaTrack>(trackName, ignoreCase: true, out var track))
                    errors.Add($"agendas.yaml: agenda '{id}' wants unknown track '{trackName}'.");
                else if (!Enum.TryParse<AgendaDirection>(directionName, ignoreCase: true, out var direction))
                    errors.Add($"agendas.yaml: agenda '{id}' wants '{trackName}' to go '{directionName}'; use up or down.");
                else
                    wishes.Add(new AgendaWish(track, direction));
            }
            agendas.Add(new AgendaDefinition(id, entry.Name, entry.Description, wishes));
        }
        return (new AgendaConfig(file.Reward, margins, agendas), errors);
    }
}

public static class AgendaRules
{
    public static bool Met(AgendaWish wish, double start, double end, double margin) =>
        wish.Direction == AgendaDirection.Up ? end - start > margin : start - end > margin;

    public static double Reward(int met, int wishes, AgendaRewardFile reward) =>
        met * reward.PerWish + (wishes > 0 && met == wishes ? reward.AllBonus : 0);

    public static AgendaDefinition? Pick(
        IReadOnlyList<AgendaDefinition> agendas, string? previous, IReadOnlyDictionary<string, int> held, Random random)
    {
        var candidates = agendas.Where(a => a.Id != previous).ToList();
        if (candidates.Count == 0) candidates = [.. agendas];
        if (candidates.Count == 0) return null;
        var fewest = candidates.Min(a => held.GetValueOrDefault(a.Id));
        var balanced = candidates.Where(a => held.GetValueOrDefault(a.Id) == fewest).ToList();
        return balanced[random.Next(balanced.Count)];
    }
}
