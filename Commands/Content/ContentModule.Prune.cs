using System.Text;
using Discord.Interactions;

public partial class ContentModule
{
    private const int PruneListLimit = 1500;

    [SlashCommand("prune", "Remove every template whose file has been deleted from content/")]
    public async Task PruneAsync()
    {
        var orphans = _gameFiles.Orphans();
        if (orphans.Count == 0)
        {
            await RespondAsync(Texts.Get("content.prune.none"), ephemeral: true);
            return;
        }

        var lines = orphans.Select(o => Texts.Get("content.prune.line",
            ("id", o.Id), ("kind", TemplateCodec.KindName(o.Kind)), ("status", o.Status))).ToList();
        var message = Texts.Get("content.prune.confirm", ("count", orphans.Count), ("list", Listed(lines)));
        var components = _confirmations.Create(Context.User.Id, async () => DescribePrune(await _gameFiles.PruneAsync()),
            Texts.Get("content.prune.button"));
        await RespondAsync(message, components: components, ephemeral: true);
    }

    private static string DescribePrune(PruneResult result)
    {
        var sb = new StringBuilder(Texts.Get("content.prune.done",
            ("count", result.Count), ("deleted", result.Deleted.Count), ("archived", result.Archived.Count),
            ("bills", result.BillsWithdrawn), ("events", result.EventsCancelled)));
        if (result.InPlay.Count > 0)
        {
            var lines = result.InPlay.Select(p => p.Kind switch
            {
                InPlayKind.BillBeforeTheAssembly => Texts.Get("content.prune.inPlay.bill", ("id", p.TemplateId)),
                InPlayKind.LiveCrisis => Texts.Get("content.prune.inPlay.crisis", ("id", p.TemplateId)),
                _ => Texts.Get("content.prune.inPlay.project", ("id", p.TemplateId)),
            }).ToList();
            sb.Append('\n').Append(Texts.Get("content.prune.inPlay.heading", ("list", Listed(lines))));
        }
        return sb.ToString();
    }

    private static string Listed(IReadOnlyList<string> lines)
    {
        var sb = new StringBuilder();
        var shown = 0;
        foreach (var line in lines)
        {
            if (sb.Length + line.Length > PruneListLimit) break;
            sb.AppendLine(line);
            shown++;
        }
        if (shown < lines.Count) sb.Append(Texts.Get("content.prune.more", ("count", lines.Count - shown)));
        return sb.ToString().TrimEnd();
    }
}
