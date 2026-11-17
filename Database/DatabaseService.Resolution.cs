using System;
using System.Linq;
using Microsoft.Data.Sqlite;

public partial class DatabaseService
{
    public ResolutionReport Resolve(GameConfig config, IRandomSource random, int seed, DateTimeOffset now)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();

        var state = GetGameStateTx(conn, tx);
        var before = state;
        var discontentBefore = GetProvincesTx(conn, tx).ToDictionary(p => p.Id, p => p.Discontent);
        var report = new ResolutionReport
        {
            ResolutionNumber = state.ResolutionNumber + 1,
            FromSeason = state.Season,
            FromYear = state.Year,
            Seed = seed,
        };

        if (state.Started)
        {
            MarkPopularityTx(conn, tx);
            CountDueReadingsTx(conn, tx, GetGameStateTx(conn, tx), config, report);
            CountDueNoConfidenceTx(conn, tx, GetGameStateTx(conn, tx), config, report);
            CountDueCabinetVotesTx(conn, tx, GetGameStateTx(conn, tx), config, report);
            ResolveDueCrisesTx(conn, tx, GetGameStateTx(conn, tx), config, report);
            ResolveDueQuestionsTx(conn, tx, GetGameStateTx(conn, tx), config, report);
            ResolveActionsTx(conn, tx, GetGameStateTx(conn, tx), config, random, report);
            ApplyOrdersTx(conn, tx, GetGameStateTx(conn, tx), config, random, report);
            PayProjectsTx(conn, tx, GetGameStateTx(conn, tx), config, report);
            UnrestCostsGovernmentTx(conn, tx, config, report);
            SeasonalChangesTx(conn, tx, GetGameStateTx(conn, tx), config);
            FiscalStepTx(conn, tx, GetGameStateTx(conn, tx), config, report);
        }

        state = GetGameStateTx(conn, tx);
        if (state.Started && state.IsPreElection)
        {
            CancelAtElectionTx(conn, tx, report);
            RunElectionTx(conn, tx, state, config, report);
            ScoreAndDealAgendasTx(conn, tx, config, seed, report);
        }
        if (state.Started && before.IsSenateElectionTurn && state.SenateEstablished)
            RunSenateElectionTx(conn, tx, state, config, report);

        AdvanceSeasonTx(conn, tx, state, config, report, now);

        if (state.Started)
        {
            if (report.PreElectionOpened) DrawFutureAgendasTx(conn, tx, config, seed, report);
            FireDueEventsTx(conn, tx, config, report);
            OpenScheduledReadingsTx(conn, tx, config, report);
            ThresholdAlertsTx(conn, tx, before, discontentBefore, config, report);
            RecordHistoryTx(conn, tx);
        }

        LogTx(conn, tx, LogKinds.Resolution, "resolution", $"Resolved {GameCalendar.Label(report.FromSeason, report.FromYear)} with seed {seed}.");
        tx.Commit();
        return report;
    }

    public ResolutionReport ResolveCampaignDay(GameConfig config, IRandomSource random, int seed, DateTimeOffset now)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();

        var state = GetGameStateTx(conn, tx);
        var report = new ResolutionReport
        {
            ResolutionNumber = state.ResolutionNumber,
            FromSeason = state.Season,
            FromYear = state.Year,
            ToSeason = state.Season,
            ToYear = state.Year,
            Seed = seed,
            CampaignDay = true,
        };

        MarkPopularityTx(conn, tx);
        ResolveActionsTx(conn, tx, state, config, random, report);
        state = GetGameStateTx(conn, tx);
        SaveGameStateTx(conn, tx, state with { CampaignDaysRun = state.CampaignDaysRun + 1, RngSeed = null });
        LogTx(conn, tx, LogKinds.Resolution, "campaign-day", $"Campaign day {state.CampaignDaysRun + 1} of {state.Label} with seed {seed}.");
        tx.Commit();
        return report;
    }

    private static void SeasonalChangesTx(SqliteConnection conn, SqliteTransaction tx, GameState state, GameConfig config)
    {
        var c = config.Constants;

        foreach (var issue in QueryIssuesTx(conn, tx, "status = 'Open'"))
            if (issue.SalienceRaisedResolution != state.ResolutionNumber)
                Exec(conn, tx, "UPDATE issues SET salience = $value WHERE id = $id;",
                    ("$id", issue.Id), ("$value", Math.Clamp(issue.Salience - c.Salience.Decay, c.Salience.Min, c.Salience.Max)));

        var conditions = GetProvinceConditionsTx(conn, tx);
        foreach (var province in GetProvincesTx(conn, tx))
        {
            var definitions = conditions.GetValueOrDefault(province.Id, []).Select(config.Condition).OfType<ConditionDefinition>();
            var rest = Math.Clamp(Formulas.RestingDiscontent(c.Discontent, definitions), c.Discontent.Min, c.Discontent.Max);
            SetDiscontentTx(conn, tx, province.Id, Formulas.StepToward(province.Discontent, rest, c.Discontent.Drift));
        }
        SampleUnrestTx(conn, tx, state, config);

        SetStateTx(conn, tx, "favour", Math.Clamp(Formulas.StepToward(state.Favour, state.FavourBaseline, c.Favour.Drift), 0, 100));

        foreach (var faction in QueryFactionsTx(conn, tx, "status = 'Active'"))
            Exec(conn, tx, "UPDATE factions SET popularity = $value WHERE user_id = $id;",
                ("$id", faction.UserId.ToString()),
                ("$value", Math.Clamp(Formulas.DriftToward(faction.Popularity, c.Popularity.Start, c.Popularity.Drift), c.Popularity.Min, c.Popularity.Max)));
        SpreadRegionalPopularityTx(conn, tx, c.Popularity);
        GrantRenownTx(conn, tx, c);
    }

    private static void GrantRenownTx(SqliteConnection conn, SqliteTransaction tx, GameConstants c)
    {
        var factions = QueryFactionsTx(conn, tx, "status = 'Active' AND prestige > 0");
        foreach (var faction in factions)
        {
            var place = Formulas.PrestigePlace(faction.Prestige, factions.Select(f => f.Prestige));
            if (place < c.Prestige.Renown.Count && c.Prestige.Renown[place] > 0)
                AddPopularityTx(conn, tx, faction.UserId, c.Prestige.Renown[place], c.Popularity);
        }
    }

    private static void AdvanceSeasonTx(SqliteConnection conn, SqliteTransaction tx, GameState state, GameConfig config, ResolutionReport report, DateTimeOffset now)
    {
        var c = config.Constants;
        var (season, year) = GameCalendar.Next(state.Season, state.Year);
        report.ToSeason = season;
        report.ToYear = year;

        var advanced = state with
        {
            Season = season,
            Year = year,
            ResolutionNumber = state.ResolutionNumber + 1,
            CampaignDaysRun = 0,
            SeasonLengthDays = null,
            LastResolutionAt = now,
            RngSeed = null,
        };

        if (state.Started && state.IsPreElection)
        {
            var (preSeason, preYear) = GameCalendar.PreElectionOfTermStarting(season, year, c);
            advanced = advanced with
            {
                TermNumber = state.TermNumber + 1,
                TermStartSeason = season,
                TermStartYear = year,
                PreElectionSeason = preSeason,
                PreElectionYear = preYear,
            };
        }

        report.PreElectionOpened = advanced.IsPreElection;
        report.SenateCampaignOpened = advanced.IsSenateCampaign;
        advanced = advanced with { NextResolutionAt = GameCalendar.NextResolutionAt(now, c, GameCalendar.SeasonLengthDays(advanced, c)) };
        SaveGameStateTx(conn, tx, advanced);

        foreach (var bill in QueryBillsTx(conn, tx, "status = 'Cooldown' AND cooldown_until <= $resolution", ("$resolution", advanced.ResolutionNumber)))
        {
            Exec(conn, tx, "UPDATE bills SET status = 'Available', cooldown_until = NULL WHERE id = $id;", ("$id", bill.Id));
            report.Alerts.Add(Texts.Get("alerts.billCooldownOver", ("bill", bill.Title)));
        }
    }

    private static void FireDueEventsTx(SqliteConnection conn, SqliteTransaction tx, GameConfig config, ResolutionReport report)
    {
        var state = GetGameStateTx(conn, tx);
        foreach (var gameEvent in QueryEventsTx(conn, tx, "status = 'Queued' AND fire_at_resolution <= $resolution", "fire_at_resolution, id",
                     ("$resolution", state.ResolutionNumber)))
        {
            if (QueryEventsTx(conn, tx, "id = $id AND status = 'Queued'", "id", ("$id", gameEvent.Id)).Count == 0) continue;
            var source = new EffectSource(gameEvent.Title, "event", gameEvent.Id.ToString(), PublicLedger: true);
            if (EventSkipReasonTx(conn, tx, config, source, report, gameEvent) is { } skipped)
            {
                Exec(conn, tx, "UPDATE events SET status = 'Cancelled' WHERE id = $id;", ("$id", gameEvent.Id));
                report.Alerts.Add(skipped);
                continue;
            }
            var result = ApplyEffectsTx(conn, tx, gameEvent.ParsedEffects, config, source, report);
            Exec(conn, tx, "UPDATE events SET status = 'Fired', fired_resolution = $resolution WHERE id = $id;",
                ("$id", gameEvent.Id), ("$resolution", state.ResolutionNumber));
            report.FiredEvents.Add(new FiredEventReport(gameEvent.Id, gameEvent.Title, gameEvent.Text, result.Applied));
        }

        if (!QueryEventsTx(conn, tx, "status = 'Queued' AND fire_at_resolution <= $next", "id", ("$next", state.ResolutionNumber + 1)).Any())
            report.Alerts.Add(Texts.Get("alerts.noEventQueued", ("label", state.Label)));
    }

    private static string? EventSkipReasonTx(
        SqliteConnection conn, SqliteTransaction tx, GameConfig config, EffectSource source, ResolutionReport report, GameEvent gameEvent)
    {
        if (gameEvent.When is not { } when) return null;
        var formula = Formula.TryParse(when, out var error);
        if (formula is null) return Texts.Get("alerts.eventWhenBroken", ("title", gameEvent.Title), ("when", when), ("error", error));
        var check = source with { SourceType = "event-when" };
        var store = new TxEffectStore(conn, tx, config, check, report, EffectRandom(report, check));
        try
        {
            return formula.IsTrue(new StoreFormulaScope(store)) ? null : Texts.Get("alerts.eventSkipped", ("title", gameEvent.Title), ("when", when));
        }
        catch (FormulaException ex)
        {
            return Texts.Get("alerts.eventWhenBroken", ("title", gameEvent.Title), ("when", when), ("error", ex.Message));
        }
    }
}
