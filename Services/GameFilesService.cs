using System.Security.Cryptography;
using System.Text;

public sealed record ContentLoadResult(
    IReadOnlyList<ImportOutcome> Imported, IReadOnlyList<string> MadeReady, IReadOnlyList<string> NotReady, int Unchanged)
{
    public bool Changed => Imported.Count > 0;
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
            ? "Config files changed: `config/` reloaded. Values already in play are unchanged."
            : $"⚠️ Config files changed, but they don't load; the old config stays in force.\n{error}");
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
        for (var pass = 0; pass < 6 && pending.Count > 0; pass++)
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

    public static string Describe(ContentLoadResult result)
    {
        var sb = new StringBuilder();
        var ok = result.Imported.Count(o => o.Imported);
        sb.AppendLine($"**Content files loaded:** {ok} imported or updated, {result.MadeReady.Count} marked Ready, {result.Unchanged} unchanged.");
        foreach (var failed in result.Imported.Where(o => !o.Imported))
            sb.AppendLine(ContentRules.Describe(failed));
        foreach (var problem in result.NotReady)
            sb.AppendLine($"- Left as Draft: {problem}");
        return Display.Truncate(sb.ToString().TrimEnd());
    }

    public void Dispose()
    {
        foreach (var watcher in _watchers) watcher.Dispose();
    }
}
