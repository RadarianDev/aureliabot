using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

public record PendingAction(
    long Id,
    ulong FactionId,
    ActionKind Kind,
    string IssueId,
    string? ProvinceId,
    Stance? Stance,
    bool OffProgramme,
    ulong? TargetFactionId,
    DateTimeOffset SubmittedAt);

public partial class DatabaseService
{
    public List<PendingAction> GetPendingActions(ulong factionId) => throw new NotImplementedException();

    public bool CancelAction(long actionId, ulong factionId) => throw new NotImplementedException();

    private static void ResolveActionsTx(
        SqliteConnection conn, SqliteTransaction tx, GameState state, GameConfig config, IRandomSource random, ResolutionReport report) =>
        throw new NotImplementedException();
}
