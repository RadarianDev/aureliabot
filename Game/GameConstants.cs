using System.Globalization;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

public sealed record DisplayConstants
{
    public string UpEmoji { get; init; } = "";
    public string DownEmoji { get; init; } = "";
}

public sealed record StartConstants
{
    public Season Season { get; init; }
    public int Year { get; init; }
}

public sealed record PopularityConstants
{
    public double Min { get; init; }
    public double Max { get; init; }
    public double Start { get; init; }
    public double Drift { get; init; }
}

public sealed record CampaignConstants
{
    public double Factor { get; init; }
    public int ActionsPerTurn { get; init; }
    public bool Crowding { get; init; }
}

public sealed record AttackConstants
{
    public double Factor { get; init; }
    public double Backfire { get; init; }
}

public sealed record PollConstants
{
    public double OpinionNoise { get; init; }
    public double SalienceNoise { get; init; }
}

public sealed record ProgrammeConstants
{
    public double FlipPenalty { get; init; }
}

public sealed record OpinionConstants
{
    public double CrisisShift { get; init; }
}

public sealed record SalienceConstants
{
    public double Min { get; init; }
    public double Max { get; init; }
    public double CampaignRaise { get; init; }
    public double CrisisRaise { get; init; }
    public double Decay { get; init; }
    public double RejectDrop { get; init; }
}

public sealed record LawConstants
{
    public double SponsorFactor { get; init; }
}

public sealed record GovernmentConstants
{
    public int BillsPerSeason { get; init; }
}

public sealed record OppositionConstants
{
    public int ActionsPerSeason { get; init; }
    public int PrivateBillsPerYear { get; init; }
    public double UnansweredQuestion { get; init; }
    public double QuestionReward { get; init; }
    public double CondemnReward { get; init; }
    public double CondemnPenalty { get; init; }
}

public sealed record AssemblyConstants
{
    public double NoConfidenceShare { get; init; }
    public int NoConfidenceGatherResolutions { get; init; }
    public int NoConfidenceCooldownSeasons { get; init; }
}

public sealed record ElectionConstants
{
    public double BaseExponent { get; init; }
    public double HomeMultiplier { get; init; }
    public double ProgrammeWeight { get; init; }
    public double MultiplierMin { get; init; }
    public double MultiplierMax { get; init; }
    public bool Crowding { get; init; }
}

public sealed record EconomyConstants
{
    public double Start { get; init; }
    public double Growth { get; init; }
}

public sealed record ReserveConstants
{
    public decimal Start { get; init; }
    public double Interest { get; init; }
}

public sealed record FavourConstants
{
    public double Start { get; init; }
    public double Baseline { get; init; }
    public double Drift { get; init; }
    public double NavyFactor { get; init; }
    public double AlertBelow { get; init; }
}

public sealed record ThreatConstants
{
    public double Start { get; init; }
    public List<double> Alerts { get; init; } = [];
}

public sealed record DiscontentConstants
{
    public double Min { get; init; }
    public double Max { get; init; }
    public double Start { get; init; }
    public double Rest { get; init; }
    public double Drift { get; init; }
    public double AlertAt { get; init; }
    public double GrowthDrag { get; init; }
}

public sealed record UnitConstants
{
    public decimal Raise { get; init; }
    public decimal Upkeep { get; init; }
    public int BaseSides { get; init; }
    public int SidesStep { get; init; }
    public int MaxSides { get; init; }
    public List<decimal> TrainCosts { get; init; } = [];

    public int StepOf(int sides) => (sides - BaseSides) / SidesStep;

    public decimal? NextTrainCost(int sides) =>
        sides >= MaxSides ? null : TrainCosts[Math.Clamp(StepOf(sides), 0, TrainCosts.Count - 1)];
}

public sealed record CrackdownConstants
{
    public decimal Cost { get; init; }
    public double Reduction { get; init; }
    public double Backlash { get; init; }
    public double FailurePopularity { get; init; }
}

public sealed record ProjectConstants
{
    public double Popularity { get; init; }
    public decimal Graft { get; init; }
}

public sealed record StartingForcesConstants
{
    public Dictionary<string, int> Divisions { get; init; } = [];
    public List<string> Gendarmerie { get; init; } = [];
}

public sealed record GameConstants
{
    public int Seats { get; init; }
    public DisplayConstants Display { get; init; } = new();
    public StartConstants Start { get; init; } = new();
    public string ResolutionTime { get; init; } = "08:00";
    public string TimeZone { get; init; } = "UTC";
    public double MinHoursBetweenResolutions { get; init; }
    public int PreElectionDays { get; init; }
    public int SeasonsPerTerm { get; init; }
    public PopularityConstants Popularity { get; init; } = new();
    public CampaignConstants Campaign { get; init; } = new();
    public AttackConstants Attack { get; init; } = new();
    public PollConstants Poll { get; init; } = new();
    public double StrongStancePenalty { get; init; }
    public ProgrammeConstants Programme { get; init; } = new();
    public OpinionConstants Opinion { get; init; } = new();
    public SalienceConstants Salience { get; init; } = new();
    public int RejectedCooldownSeasons { get; init; }
    public LawConstants Law { get; init; } = new();
    public GovernmentConstants Government { get; init; } = new();
    public OppositionConstants Opposition { get; init; } = new();
    public AssemblyConstants Assembly { get; init; } = new();
    public ElectionConstants Election { get; init; } = new();
    public EconomyConstants Economy { get; init; } = new();
    public double TaxRate { get; init; }
    public ReserveConstants Reserve { get; init; } = new();
    public Dictionary<Ministry, double> DefaultAllocation { get; init; } = [];
    public FavourConstants Favour { get; init; } = new();
    public ThreatConstants Threat { get; init; } = new();
    public DiscontentConstants Discontent { get; init; } = new();
    public UnitConstants Division { get; init; } = new();
    public UnitConstants Gendarmerie { get; init; } = new();
    public CrackdownConstants Crackdown { get; init; } = new();
    public ProjectConstants Project { get; init; } = new();
    public StartingForcesConstants StartingForces { get; init; } = new();

    public UnitConstants UnitsOf(UnitType type) => type == UnitType.Division ? Division : Gendarmerie;

    public TimeOnly ResolutionTimeOfDay => TimeOnly.ParseExact(ResolutionTime, "HH:mm", CultureInfo.InvariantCulture);

    public TimeZoneInfo Zone => TimeZoneInfo.FindSystemTimeZoneById(TimeZone);
}

public sealed record ProvinceDefinition
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public double Population { get; init; }
    public string MapKey { get; init; } = "";
    public string Description { get; init; } = "";
    public List<string> Conditions { get; init; } = [];
}

public sealed record ConditionDefinition
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public Dictionary<string, double> Biases { get; init; } = [];
    public double DiscontentRest { get; init; }
    public double Growth { get; init; }

    public double BiasOn(string issueId) => Biases.TryGetValue(issueId, out var bias) ? bias : 0;
}

public sealed class GameConfig
{
    public GameConstants Constants { get; }
    public IReadOnlyList<ProvinceDefinition> Provinces { get; }
    public IReadOnlyList<ConditionDefinition> Conditions { get; }

    private readonly Dictionary<string, ProvinceDefinition> _provincesById;
    private readonly Dictionary<string, ConditionDefinition> _conditionsById;

    public GameConfig(GameConstants constants, IReadOnlyList<ProvinceDefinition> provinces, IReadOnlyList<ConditionDefinition> conditions)
    {
        Constants = constants;
        Provinces = provinces;
        Conditions = conditions;
        _provincesById = provinces.ToDictionary(p => p.Id);
        _conditionsById = conditions.ToDictionary(c => c.Id);
    }

    public ProvinceDefinition? Province(string id) => _provincesById.GetValueOrDefault(id);

    public ConditionDefinition? Condition(string id) => _conditionsById.GetValueOrDefault(id);
}

public sealed class ConfigException(string message) : Exception(message);

public sealed class ConstantsStore
{
    public const string ConstantsFile = "constants.yaml";
    public const string ProvincesFile = "provinces.yaml";
    public const string ConditionsFile = "conditions.yaml";

    private readonly string _directory;
    private GameConfig _current;

    public ConstantsStore(string directory)
    {
        _directory = directory;
        _current = Load(directory);
    }

    public ConstantsStore() : this(Path.Combine(AppContext.BaseDirectory, "config")) { }

    public GameConfig Config => Volatile.Read(ref _current);

    public GameConstants Current => Config.Constants;

    public string? Reload()
    {
        try
        {
            Volatile.Write(ref _current, Load(_directory));
            return null;
        }
        catch (Exception ex) when (ex is ConfigException or YamlException or IOException)
        {
            return ex.Message;
        }
    }

    private static IDeserializer CreateDeserializer() => new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    public static GameConfig Load(string directory)
    {
        var deserializer = CreateDeserializer();

        var constants = Read<GameConstants>(deserializer, directory, ConstantsFile);
        var provinces = Read<Dictionary<string, ProvinceDefinition>>(deserializer, directory, ProvincesFile)
            .Select(kv => kv.Value with { Id = kv.Key })
            .ToList();
        var conditions = Read<Dictionary<string, ConditionDefinition>>(deserializer, directory, ConditionsFile)
            .Select(kv => kv.Value with { Id = kv.Key })
            .ToList();

        var config = new GameConfig(constants, provinces, conditions);
        var errors = Validate(config);
        if (errors.Count > 0)
            throw new ConfigException("Config is invalid:\n" + string.Join("\n", errors.Select(e => $"- {e}")));
        return config;
    }

    private static T Read<T>(IDeserializer deserializer, string directory, string file)
    {
        var path = Path.Combine(directory, file);
        if (!File.Exists(path)) throw new ConfigException($"{file} is missing from {directory}.");
        try
        {
            return deserializer.Deserialize<T>(File.ReadAllText(path))
                ?? throw new ConfigException($"{file} is empty.");
        }
        catch (YamlException ex)
        {
            throw new ConfigException($"{file}, line {ex.Start.Line}: {(ex.InnerException ?? ex).Message}");
        }
    }

    public static List<string> Validate(GameConfig config)
    {
        var c = config.Constants;
        var errors = new List<string>();

        if (c.Seats <= 0) errors.Add("seats must be positive.");
        if (string.IsNullOrWhiteSpace(c.Display.UpEmoji) || string.IsNullOrWhiteSpace(c.Display.DownEmoji))
            errors.Add("display.upEmoji and display.downEmoji are required.");
        if (!TimeOnly.TryParseExact(c.ResolutionTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            errors.Add($"resolutionTime '{c.ResolutionTime}' is not HH:mm.");
        try { _ = c.Zone; }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            errors.Add($"timeZone '{c.TimeZone}' is not a known time zone.");
        }
        if (c.PreElectionDays < 1) errors.Add("preElectionDays must be at least 1.");
        if (c.SeasonsPerTerm < 2) errors.Add("seasonsPerTerm must be at least 2.");
        if (c.Popularity.Min >= c.Popularity.Max) errors.Add("popularity.min must be below popularity.max.");
        if (c.Salience.Min >= c.Salience.Max) errors.Add("salience.min must be below salience.max.");
        if (c.Discontent.Min >= c.Discontent.Max) errors.Add("discontent.min must be below discontent.max.");

        foreach (var ministry in Enum.GetValues<Ministry>())
            if (!c.DefaultAllocation.ContainsKey(ministry))
                errors.Add($"defaultAllocation is missing {ministry}.");
        var allocationTotal = c.DefaultAllocation.Values.Sum();
        if (Math.Abs(allocationTotal - 1) > 1e-6)
            errors.Add($"defaultAllocation totals {allocationTotal.ToString(CultureInfo.InvariantCulture)}, not 1.");

        ValidateUnits(errors, "division", c.Division);
        ValidateUnits(errors, "gendarmerie", c.Gendarmerie);

        foreach (var province in c.StartingForces.Divisions.Keys.Concat(c.StartingForces.Gendarmerie))
            if (config.Province(province) is null)
                errors.Add($"startingForces names unknown province '{province}'.");

        if (config.Provinces.Count == 0) errors.Add("provinces.yaml has no provinces.");
        foreach (var province in config.Provinces)
        {
            if (string.IsNullOrWhiteSpace(province.Name)) errors.Add($"Province '{province.Id}' has no name.");
            if (province.Population <= 0) errors.Add($"Province '{province.Id}' needs a positive population.");
            if (string.IsNullOrWhiteSpace(province.Description)) errors.Add($"Province '{province.Id}' has no description.");
            if (!IsHexColour(province.MapKey)) errors.Add($"Province '{province.Id}' mapKey '{province.MapKey}' is not #RRGGBB.");
            foreach (var condition in province.Conditions)
                if (config.Condition(condition) is null)
                    errors.Add($"Province '{province.Id}' has unknown condition '{condition}'.");
        }
        foreach (var group in config.Provinces.GroupBy(p => p.MapKey.ToUpperInvariant()).Where(g => g.Count() > 1))
            errors.Add($"mapKey {group.Key} is shared by {string.Join(", ", group.Select(p => p.Id))}.");

        foreach (var condition in config.Conditions)
            if (string.IsNullOrWhiteSpace(condition.Name))
                errors.Add($"Condition '{condition.Id}' has no name.");

        return errors;
    }

    private static void ValidateUnits(List<string> errors, string name, UnitConstants units)
    {
        if (units.BaseSides < 2 || units.SidesStep < 1 || units.MaxSides < units.BaseSides)
        {
            errors.Add($"{name} needs baseSides >= 2, sidesStep >= 1 and maxSides >= baseSides.");
            return;
        }
        if ((units.MaxSides - units.BaseSides) % units.SidesStep != 0)
            errors.Add($"{name}: maxSides - baseSides must be a multiple of sidesStep.");
        var steps = (units.MaxSides - units.BaseSides) / units.SidesStep;
        if (units.TrainCosts.Count != steps)
            errors.Add($"{name}.trainCosts needs {steps} entries, one per training step.");
    }

    public static bool IsHexColour(string value) =>
        value.Length == 7 && value[0] == '#' && value.Skip(1).All(Uri.IsHexDigit);
}
