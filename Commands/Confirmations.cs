using System.Collections.Concurrent;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;

public sealed record ConfirmedResult(string Text, string? FileName = null, string? FileText = null);

public class Confirmations
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    private sealed record Pending(ulong UserId, DateTimeOffset ExpiresAt, Func<Task<ConfirmedResult>> OnConfirm);

    private readonly ConcurrentDictionary<string, Pending> _pending = new();

    public MessageComponent Create(ulong userId, Func<Task<string>> onConfirm, string? confirmLabel = null, ButtonStyle style = ButtonStyle.Danger) =>
        CreateWithResult(userId, async () => new ConfirmedResult(await onConfirm()), confirmLabel, style);

    public MessageComponent CreateWithResult(
        ulong userId, Func<Task<ConfirmedResult>> onConfirm, string? confirmLabel = null, ButtonStyle style = ButtonStyle.Danger)
    {
        foreach (var expired in _pending.Where(p => p.Value.ExpiresAt < DateTimeOffset.UtcNow).Select(p => p.Key).ToList())
            _pending.TryRemove(expired, out _);

        var id = Guid.NewGuid().ToString("N");
        _pending[id] = new Pending(userId, DateTimeOffset.UtcNow + Lifetime, onConfirm);
        return new ComponentBuilder()
            .WithButton(confirmLabel ?? Texts.Get("confirm.button"), $"confirm:yes:{id}", style)
            .WithButton(Texts.Get("confirm.cancel"),$"confirm:no:{id}", ButtonStyle.Secondary)
            .Build();
    }

    public (bool Found, bool WrongUser, Func<Task<ConfirmedResult>>? Action) Take(string id, ulong userId)
    {
        if (!_pending.TryGetValue(id, out var pending) || pending.ExpiresAt < DateTimeOffset.UtcNow)
        {
            _pending.TryRemove(id, out _);
            return (false, false, null);
        }
        if (pending.UserId != userId) return (true, true, null);
        _pending.TryRemove(id, out _);
        return (true, false, pending.OnConfirm);
    }
}

public class ConfirmationModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly Confirmations _confirmations;

    public ConfirmationModule(Confirmations confirmations)
    {
        _confirmations = confirmations;
    }

    [ComponentInteraction("confirm:yes:*", ignoreGroupNames: true)]
    public async Task ConfirmAsync(string id)
    {
        var (found, wrongUser, action) = _confirmations.Take(id, Context.User.Id);
        if (wrongUser)
        {
            await RespondAsync(Texts.Get("confirm.onlyConfirmer"), ephemeral: true);
            return;
        }
        if (!found || action is null)
        {
            await Finish(Texts.Get("confirm.expired"));
            return;
        }

        await DeferAsync();
        ConfirmedResult result;
        try
        {
            result = await action();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Confirmations] Confirmed action failed: {ex}");
            result = new ConfirmedResult(Texts.Get("confirm.failed", ("error", ex.Message)));
        }

        using var file = result is { FileName: { } name, FileText: { } text }
            ? new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text))
            : null;
        await ModifyOriginalResponseAsync(p =>
        {
            p.Content = Truncate(result.Text);
            p.Components = new ComponentBuilder().Build();
            p.Embeds = Array.Empty<Embed>();
            if (file is not null) p.Attachments = new[] { new FileAttachment(file, result.FileName!) };
        });
    }

    [ComponentInteraction("confirm:no:*", ignoreGroupNames: true)]
    public async Task CancelAsync(string id)
    {
        var (_, wrongUser, _) = _confirmations.Take(id, Context.User.Id);
        if (wrongUser)
        {
            await RespondAsync(Texts.Get("confirm.onlyCanceller"), ephemeral: true);
            return;
        }
        await Finish(Texts.Get("confirm.cancelled"));
    }

    private Task Finish(string content) =>
        ((SocketMessageComponent)Context.Interaction).UpdateAsync(p =>
        {
            p.Content = content;
            p.Components = new ComponentBuilder().Build();
            p.Embeds = Array.Empty<Embed>();
        });

    private static string Truncate(string text) => text.Length > 2000 ? text[..1997] + "..." : text;
}
