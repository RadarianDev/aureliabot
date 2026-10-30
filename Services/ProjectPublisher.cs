using Discord;
using Discord.WebSocket;

public class ProjectPublisher
{
    private readonly DiscordSocketClient _client;
    private readonly DatabaseService _data;
    private readonly ConstantsStore _constants;
    private readonly AlertService _alerts;

    public ProjectPublisher(DiscordSocketClient client, DatabaseService data, ConstantsStore constants, AlertService alerts)
    {
        _client = client;
        _data = data;
        _constants = constants;
        _alerts = alerts;
    }

    public async Task PublishAsync(ResolutionReport report, string source)
    {
        foreach (var finished in report.ProjectsFinished)
            await Safely(() => PostAsync(FinishedPost(finished), source));

        foreach (var started in report.ProjectsStarted)
            await PublishStartedAsync(started.ProjectId, source);
    }

    public async Task PublishStartedAsync(long projectId, string source)
    {
        if (_data.GetProject(projectId) is not { } project) return;
        await Safely(() => PostAsync(BegunPost(project, _data.GetGameState(), _constants.Config), source));
    }

    public static string BegunPost(LiveProject project, GameState state, GameConfig config)
    {
        var preamble = new List<string>
        {
            Texts.Get("projects.post.meta", ("province", Display.Province(config, project.ProvinceId)), ("ministry", project.Purse.DisplayName()),
                ("price", Display.Crowns(project.Price)), ("term", project.Term)),
        };
        if (project.BonusDueResolution is { } due)
            preamble.Add(Texts.Get("projects.post.bonus", ("deadline", TurnLabel(state, due))));
        return Publisher.FormatEffectPost(Texts.Get("projects.post.begun", ("project", project.Title)), preamble, null, []);
    }

    public string FinishedPost(ProjectFinishedReport finished)
    {
        var config = _constants.Config;
        var project = _data.GetProject(finished.ProjectId);
        var province = Display.Province(config, project?.ProvinceId);
        var ministry = (project?.Purse ?? Ministry.CommerceLabor).DisplayName();
        var preamble = finished.MinisterFactionId is { } minister
            ? Texts.Get("projects.post.minister", ("province", province), ("minister", Display.Faction(_data, minister)), ("ministry", ministry),
                ("change", Display.Signed(config.Constants.Project.Popularity)))
            : Texts.Get("projects.post.noMinister", ("province", province), ("ministry", ministry));
        return Publisher.FormatEffectPost(Texts.Get("projects.post.finished", ("project", finished.Title)), [preamble],
            finished.BonusEarned ? finished.BonusText : null,
            EffectSummary.Lines(finished.Effects, Publisher.Names(_data, config), config.Constants.Display));
    }

    public static string TurnLabel(GameState state, long resolution)
    {
        var (season, year) = ProjectForecast.TurnEndedBy(state, resolution);
        return GameCalendar.Label(season, year);
    }

    private async Task Safely(Func<Task> post)
    {
        try
        {
            await post();
        }
        catch (Exception ex)
        {
            await _alerts.PostAsync(Texts.Get("projects.alert.postFailed", ("error", ex.Message)));
        }
    }

    private async Task PostAsync(string text, string source)
    {
        if (_data.GetChannel(ChannelRole.News) is not { } channelId || _client.GetChannel(channelId) is not IMessageChannel channel)
        {
            Console.WriteLine($"[ProjectPublisher] No News channel is set; skipped a post from {source}.");
            return;
        }

        foreach (var chunk in Publisher.Chunk(text))
        {
            var message = await channel.SendMessageAsync(chunk, allowedMentions: AllowedMentions.None);
            _data.LogPost(channelId, message.Id, source);
        }
    }
}
