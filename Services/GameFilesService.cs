using System.Security.Cryptography;
using System.Text;

public sealed record ContentLoadResult(
    IReadOnlyList<ImportOutcome> Imported, IReadOnlyList<string> MadeReady, IReadOnlyList<string> NotReady, int Unchanged)
{
    public bool Changed => Imported.Count > 0;
}

public sealed record PruneResult(
    IReadOnlyList<string> Deleted, IReadOnlyList<string> Archived, int BillsWithdrawn, int EventsCancelled, IReadOnlyList<InPlay> InPlay)
{
    public int Count => Deleted.Count + Archived.Count;
}

public class GameFilesService : IDisposable
{
    private const string HashKeyPrefix = "content_file:";
    private const string FileTemplateKeyPrefix = "content_template:";
    private static readonly TimeSpan Debounce = TimeSpan.FromSeconds(2);

    private readonly BotConfig _config;
    private readonly DatabaseService _data;
    private readonly ConstantsStore _constants;
    private readonly AlertService _alerts;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<FileSystemWatcher> _watchers = [];
    private CancellationTokenSource? _contentPending;
    private CancellationTokenSource? _configPending;

    public GameFilesService(BotConfig config, DatabaseService data, ConstantsStore constants, AlertService alerts)
    {
        _config = config;
        _data = data;
        _constants = constants;
        _alerts = alerts;
    }

    public string ContentFolder => _config.ContentFolder;

    public string ConfigFolder => _constants.Folder;

    public void StartWatching()
    {
        if (_watchers.Count > 0) return;
        if (Directory.Exists(ConfigFolder))
            _watchers.Add(Watch(ConfigFolder, includeSubdirectories: false, () => Schedule(ref _configPending, ReloadConfigAsync)));
        if (Directory.Exists(ContentFolder))
            _watchers.Add(Watch(ContentFolder, includeSubdirectories: true, () => Schedule(ref _contentPending, ReportContentAsync)));
        Console.WriteLine($"[GameFiles] Watching {ConfigFolder} and {ContentFolder}.");
    }

    private static FileSystemWatcher Watch(string folder, bool includeSubdirectories, Action onChange)
    {
        var watcher = new FileSystemWatcher(folder)
        {
            IncludeSubdirectories = includeSubdirectories,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
        };
        watcher.Filters.Add("*.yaml");
        watcher.Filters.Add("*.yml");
        watcher.Changed += (_, _) => onChange();
        watcher.Created += (_, _) => onChange();
        watcher.Renamed += (_, _) => onChange();
        watcher.EnableRaisingEvents = true;
        return watcher;
    }

    private static void Schedule(ref CancellationTokenSource? pending, Func<Task> work)
    {
        var next = new CancellationTokenSource();
        Interlocked.Exchange(ref pending, next)?.Cancel();
        var token = next.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(Debounce, token);
                await work();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GameFiles] Reload failed: {ex}");
            }
        });
    }

    public async Task ReloadConfigAsync()
    {
        var error = _constants.Reload();
        await _alerts.PostAsync(error is null
            ? Texts.Get("files.config.reloaded")
            : Texts.Get("files.config.failed", ("error", error)));
    }

    public async Task ReportContentAsync()
    {
        var result = await LoadContentAsync();
        if (result.Changed) await _alerts.PostAsync(Describe(result));
    }

    public async Task<ContentLoadResult> LoadContentAsync()
    {
        await _gate.WaitAsync();
        try
        {
            return LoadContent();
        }
        finally
        {
            _gate.Release();
        }
    }

    private ContentLoadResult LoadContent()
    {
        if (!Directory.Exists(ContentFolder)) return new([], [], [], 0);
        var config = _constants.Config;
        var context = ContentRules.ImportContext(_data, config);
        var imported = new List<ImportOutcome>();
        var unchanged = 0;

        foreach (var path in Directory.EnumerateFiles(ContentFolder, "*.*", SearchOption.AllDirectories)
                     .Where(p => p.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".yml", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(p => p, StringComparer.Ordinal))
        {
            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch (IOException)
            {
                continue;
            }

            var relative = Path.GetRelativePath(ContentFolder, path).Replace('\\', '/');
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
            if (_data.GetSetting(HashKeyPrefix + relative) == hash)
            {
                unchanged++;
                continue;
            }

            var outcome = ContentRules.ImportOne(_data, context, new ImportFile(relative, text));
            imported.Add(outcome);
            if (!outcome.Imported) continue;
            _data.SetSetting(HashKeyPrefix + relative, hash);
            if (outcome.Id is { } id) _data.SetSetting(FileTemplateKeyPrefix + id, relative);
        }

        var pending = imported.Where(o => o.Imported && o.Id is not null).Select(o => o.Id!).Distinct().ToList();
        if (imported.Count > 0)
            pending.AddRange(_data.GetTemplates(status: TemplateStatus.Draft).Select(t => t.Id)
                .Where(id => !pending.Contains(id) && _data.GetSetting(FileTemplateKeyPrefix + id) is not null));
        var ready = new List<string>();
        var problems = new Dictionary<string, string>();
        while (pending.Count > 0)
        {
            var progress = false;
            foreach (var id in pending.ToList())
            {
                var result = ContentRules.MakeReady(_data, config, id);
                if (result.Ok)
                {
                    ready.Add(id);
                    pending.Remove(id);
                    problems.Remove(id);
                    progress = true;
                }
                else problems[id] = result.Message;
            }
            if (!progress) break;
        }

        return new ContentLoadResult(imported, ready, pending.Select(id => problems.GetValueOrDefault(id, id)).ToList(), unchanged);
    }

    public IReadOnlyList<TemplateRow> Orphans() =>
        _data.GetSettingsWithPrefix(FileTemplateKeyPrefix)
            .Where(s => !File.Exists(Path.Combine(ContentFolder, s.Value)))
            .Select(s => _data.GetTemplate(s.Key[FileTemplateKeyPrefix.Length..]))
            .OfType<TemplateRow>()
            .Where(t => t.Status != TemplateStatus.Archived)
            .OrderBy(t => t.Kind).ThenBy(t => t.Id, StringComparer.Ordinal)
            .ToList();

    public async Task<PruneResult> PruneAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var deleted = new List<string>();
            var archived = new List<string>();
            var bills = 0;
            var events = 0;
            var inPlay = new List<InPlay>();
            foreach (var row in Orphans())
            {
                inPlay.AddRange(_data.StillInPlay(row.Id));
                if (row.Kind == TemplateKind.Bill && _data.WithdrawIdleBill(row.Id)) bills++;
                if (row.Kind == TemplateKind.Event) events += _data.CancelQueuedEvents(row.Id);
                var neverPosted = row.Status == TemplateStatus.Draft && row.TimesPosted == 0;
                ContentRules.Remove(_data, row.Id);
                (neverPosted ? deleted : archived).Add(row.Id);
                if (_data.GetSetting(FileTemplateKeyPrefix + row.Id) is { } path) _data.SetSetting(HashKeyPrefix + path, null);
                _data.SetSetting(FileTemplateKeyPrefix + row.Id, null);
            }
            return new PruneResult(deleted, archived, bills, events, inPlay);
        }
        finally
        {
            _gate.Release();
        }
    }

    public static string Describe(ContentLoadResult result)
    {
        var sb = new StringBuilder();
        var ok = result.Imported.Count(o => o.Imported);
        sb.AppendLine(Texts.Get("files.content.summary", ("ok", ok), ("ready", result.MadeReady.Count), ("unchanged", result.Unchanged)));
        foreach (var failed in result.Imported.Where(o => !o.Imported))
            sb.AppendLine(ContentRules.Describe(failed));
        foreach (var problem in result.NotReady)
            sb.AppendLine(Texts.Get("files.content.leftAsDraft", ("problem", problem)));
        return Display.Truncate(sb.ToString().TrimEnd());
    }

    public void Dispose()
    {
        foreach (var watcher in _watchers) watcher.Dispose();
    }
}
