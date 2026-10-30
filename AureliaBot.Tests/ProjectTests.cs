using Xunit;

namespace AureliaBot.Tests;

public class ProjectTests : AssemblyTestBase
{
    private const string Works = """
        id: works
        title: The Works
        purse: CommerceLabor
        province: aurenten
        price: 6
        term: 3
        effects: { economy: 1, discontent.here: -2 }
        """;

    private const string FenDrainage = """
        id: fen_drainage
        title: The Tovrerie Fen Drainage
        purse: Interior
        province: tovrerie
        price: 4
        term: 4
        effects: { condition.here.marshland: -1, discontent.here: -2, economy: 1 }
        bonus:
          by: { season: Summer, year: 1851 }
          text: The fens are dry before the fever season.
          effects: { economy: 1 }
        """;

    private const string Harbour = """
        id: harbour
        title: Naval harbour
        ministerial: true
        purse: CommerceLabor
        requires: coastal
        price: 5
        term: 3
        effects: { economy: 1 }
        bonus:
          within: 2
          text: The harbour is finished early.
          effects: { favour: 2 }
        """;

    private static decimal Graft => C.Project.Graft;

    public ProjectTests()
    {
        SeatCabinet(Chancellor, (Ministry.Treasury, Opposer), (Ministry.CommerceLabor, Minister), (Ministry.Interior, Backbencher));
        Ready("works", Works);
        Ready("fen_drainage", FenDrainage);
        Ready("harbour", Harbour);
        SetPurse(Ministry.CommerceLabor, 100);
        SetPurse(Ministry.Interior, 100);
    }

    private void Ready(string id, string yaml)
    {
        Db.SaveTemplate(id, TemplateKind.Project, yaml);
        Db.SetTemplateStatus(id, TemplateStatus.Ready);
    }

    private void Apply(params Effect[] effects) => Db.ApplyEffects(effects, Config, new EffectSource("test", "test", "1"));

    private void SetPurse(Ministry ministry, decimal amount) =>
        Apply(new Effect($"purse.{ministry}", (double)(amount - Db.GetPurseBalance(ministry))));

    private LiveProject Start(string templateId)
    {
        Apply(new Effect($"project.{templateId}", null));
        return Db.GetProjects().Last(p => p.TemplateId == templateId);
    }

    private LiveProject Reload(LiveProject project) => Db.GetProject(project.Id)!;

    private List<LedgerEntry> Payments(LiveProject project) =>
        Db.GetLedger().Where(e => e.SourceType == "project" && e.SourceId == project.Id.ToString()).ToList();

    private void Fund(LiveProject project, decimal spending, ulong minister = Minister) =>
        Assert.Null(Db.FundProject(minister, project.Id, spending, Config).Refusal);

    [Fact]
    public void AProjectStartedByAnEffect_TakesTheTemplate_AndFirstPaysAtTheNextResolution()
    {
        var state = Db.GetGameState();
        var project = Start("works");

        Assert.Equal(("The Works", Ministry.CommerceLabor, "aurenten"), (project.Title, project.Purse, project.ProvinceId));
        Assert.Equal((6m, 3, 0m, 2m), (project.Price, project.Term, project.Progress, project.Spending));
        Assert.Equal(state.ResolutionNumber + 1, project.FirstPaymentResolution);
        Assert.Equal(ProjectStatus.Active, project.Status);
        Assert.Null(project.BonusDueResolution);
    }

    [Fact]
    public void ASecondLiveCopyOfATemplate_IsRefused()
    {
        Start("works");
        var result = Db.ApplyEffects([new Effect("project.works", null)], Config, new EffectSource("test", "test", "1"));

        Assert.Single(result.Warnings);
        Assert.Single(Db.GetProjects(), p => p.TemplateId == "works");
    }

    [Fact]
    public void SpendingAtThePace_PaysThePace_WithNoGraft()
    {
        var project = Start("works");

        Resolve();

        Assert.Equal(2m, Reload(project).Progress);
        var entry = Assert.Single(Payments(project));
        Assert.Equal((LedgerKind.Project, -2m), (entry.Kind, entry.Amount));
        Assert.Equal(98m, Db.GetPurseBalance(Ministry.CommerceLabor));
    }

    [Fact]
    public void SpendingAboveThePace_LosesTheGraftShareOfTheExcess()
    {
        var project = Start("works");
        Fund(project, 3);

        Resolve();

        var lost = Graft * 1;
        Assert.Equal(2 + (1 - lost), Reload(project).Progress);
        var entries = Payments(project);
        Assert.Equal(-(3 - lost), entries.Single(e => e.Kind == LedgerKind.Project).Amount);
        Assert.Equal(-lost, entries.Single(e => e.Kind == LedgerKind.Graft).Amount);
        Assert.Equal(97m, Db.GetPurseBalance(Ministry.CommerceLabor));
    }

    [Fact]
    public void APayment_NeverExceedsWhatIsNeededToFinish()
    {
        var project = Start("works");
        Fund(project, 5);

        Resolve();
        Assert.Equal(3.5m, Reload(project).Progress);

        Resolve();

        var finished = Reload(project);
        Assert.Equal(ProjectStatus.Finished, finished.Status);
        Assert.Equal(6m, finished.Progress);
        Assert.Equal(-8m, Payments(project).Sum(e => e.Amount));
    }

    [Fact]
    public void AShortPurse_PaysWhatItHolds_ThenTheProjectStalls_AndTheMinisterIsTold()
    {
        var project = Start("works");
        SetPurse(Ministry.CommerceLabor, 1.5m);

        Resolve();

        Assert.Equal(1.5m, Reload(project).Progress);
        Assert.Equal(ProjectStatus.Active, Reload(project).Status);
        Assert.Equal(0m, Db.GetPurseBalance(Ministry.CommerceLabor));

        var report = Resolve();

        Assert.Equal(ProjectStatus.Stalled, Reload(project).Status);
        Assert.Equal(1.5m, Reload(project).Progress);
        Assert.True(report.PrivateMessages.ContainsKey(Minister));

        SetPurse(Ministry.CommerceLabor, 10);
        Resolve();
        Assert.Equal(ProjectStatus.Active, Reload(project).Status);
        Assert.Equal(3.5m, Reload(project).Progress);
    }

    [Fact]
    public void ZeroSpending_StallsTheProject_WithoutPaying()
    {
        var project = Start("works");
        Fund(project, 0);

        var report = Resolve();

        Assert.Equal(ProjectStatus.Stalled, Reload(project).Status);
        Assert.Empty(Payments(project));
        Assert.True(report.PrivateMessages.ContainsKey(Minister));
    }

    [Fact]
    public void OnlyThePursesMinister_OrTheChancellorWhenVacant_CanFund()
    {
        var project = Start("works");

        Assert.NotNull(Db.FundProject(Backbencher, project.Id, 3, Config).Refusal);
        Assert.NotNull(Db.FundProject(Chancellor, project.Id, 3, Config).Refusal);
        Assert.NotNull(Db.FundProject(Minister, project.Id, -1, Config).Refusal);

        SeatCabinet(Chancellor, (Ministry.Interior, Backbencher));
        Assert.Null(Db.FundProject(Chancellor, project.Id, 3, Config).Refusal);
        Assert.Equal(3m, Reload(project).Spending);
    }

    [Fact]
    public void FenDrainage_AtThePace_MissesItsBonusByASeason()
    {
        Db.SaveGameState(Db.GetGameState() with { Season = Season.Winter, Year = 1850 });
        var state = Db.GetGameState();
        var project = Start("fen_drainage");
        Assert.Equal(GameCalendar.ResolutionForTurn(state, Season.Summer, 1851) + 1, project.BonusDueResolution);
        Assert.Equal(state.ResolutionNumber + 3, project.BonusDueResolution);

        var forecast = ProjectForecast.For(project, 1, Graft, state);
        Assert.Equal(4, forecast.SeasonsToFinish);
        Assert.Equal((Season.Autumn, 1851), forecast.FinishTurn);
        Assert.False(forecast.BeatsBonus);

        ProjectFinishedReport? finished = null;
        for (var season = 0; season < 4; season++)
        {
            SetPurse(Ministry.Interior, 10);
            finished = Resolve().ProjectsFinished.SingleOrDefault();
        }

        Assert.NotNull(finished);
        Assert.False(finished.BonusEarned);
        Assert.Equal(-4m, Payments(project).Sum(e => e.Amount));
    }

    [Fact]
    public void FenDrainage_AtTwoASeason_BeatsItsBonus_ForFiveCrowns()
    {
        Db.SaveGameState(Db.GetGameState() with { Season = Season.Winter, Year = 1850 });
        var state = Db.GetGameState();
        var project = Start("fen_drainage");
        Fund(project, 2, Backbencher);

        var forecast = ProjectForecast.For(Reload(project), 2, Graft, state);
        Assert.Equal(3, forecast.SeasonsToFinish);
        Assert.Equal((Season.Summer, 1851), forecast.FinishTurn);
        Assert.True(forecast.BeatsBonus);
        Assert.Equal(1.5m, forecast.WorkPerSeason);

        var reports = new List<ResolutionReport>();
        for (var season = 0; season < 3; season++)
        {
            SetPurse(Ministry.Interior, 10);
            reports.Add(Resolve());
        }

        var finished = Assert.Single(reports.SelectMany(r => r.ProjectsFinished));
        Assert.True(finished.BonusEarned);
        Assert.Equal("The fens are dry before the fever season.", finished.BonusText);
        Assert.Equal(Backbencher, finished.MinisterFactionId);
        Assert.Equal(-5m, Payments(project).Sum(e => e.Amount));
        Assert.Equal(-1m, Payments(project).Where(e => e.Kind == LedgerKind.Graft).Sum(e => e.Amount));
        Assert.DoesNotContain("marshland", Db.GetProvinceConditions().GetValueOrDefault("tovrerie", []));
    }

    [Fact]
    public void AWithinDeadline_CountsResolutionsFromTheStart()
    {
        var state = Db.GetGameState();
        var result = Db.StartMinisterialProject(Minister, "harbour", "tovrerie", Config);

        Assert.Null(result.Refusal);
        Assert.Equal(state.ResolutionNumber, result.Project!.StartedResolution);
        Assert.Equal(state.ResolutionNumber + 1, result.Project.FirstPaymentResolution);
        Assert.Equal(state.ResolutionNumber + 2, result.Project.BonusDueResolution);
    }

    [Fact]
    public void AMinisterialStart_NeedsTheMinister_AMinisterialTemplate_AndTheRequiredCondition()
    {
        Assert.NotNull(Db.StartMinisterialProject(Backbencher, "harbour", "tovrerie", Config).Refusal);
        Assert.NotNull(Db.StartMinisterialProject(Minister, "harbour", "aurenten", Config).Refusal);
        Assert.NotNull(Db.StartMinisterialProject(Minister, "works", "aurenten", Config).Refusal);
        Assert.Empty(Db.GetProjects());

        var started = Db.StartMinisterialProject(Minister, "harbour", "north_arpal", Config).Project!;
        Assert.Equal("north_arpal", started.ProvinceId);
        Assert.Equal(ProjectStatus.Active, started.Status);
    }

    [Fact]
    public void AProjectStartedByALaw_FirstPaysAtTheResolutionAfter()
    {
        AddBill("works_bill", "labour", Lean.For, "project.works");
        var reading = Propose(Chancellor, "works_bill");
        Resolve();
        Vote(VoteSubject.Reading, reading.Id, Chancellor, VoteChoice.Yes);
        Vote(VoteSubject.Reading, reading.Id, Minister, VoteChoice.Yes);

        var passed = Resolve();

        var started = Assert.Single(passed.ProjectsStarted);
        var project = Db.GetProject(started.ProjectId)!;
        Assert.Equal(0m, project.Progress);
        Assert.Equal(passed.ResolutionNumber + 1, project.FirstPaymentResolution);
        Assert.Empty(Payments(project));

        Resolve();
        Assert.Equal(2m, Reload(project).Progress);
    }

    [Fact]
    public void Finishing_AppliesItsEffectsHere_AndTheMinisterGainsPopularity()
    {
        var project = Start("works");
        Fund(project, 4);
        Resolve();
        Db.SaveGameState(Db.GetGameState() with { Season = Season.Spring });
        var economy = Db.GetGameState().Economy;
        var before = Db.GetFaction(Minister)!.Popularity;
        var p = C.Popularity;
        var expected = Math.Clamp(Formulas.DriftToward(Math.Clamp(before + C.Project.Popularity, p.Min, p.Max), p.Start, p.Drift), p.Min, p.Max);

        var report = Resolve();

        var finished = Assert.Single(report.ProjectsFinished);
        Assert.Equal(Minister, finished.MinisterFactionId);
        Assert.False(finished.BonusEarned);
        Assert.Contains(finished.Effects, e => e.Key == "discontent.aurenten");
        Assert.Contains(finished.Effects, e => e.Key == "economy" && e.Value == 1);
        Assert.Equal(economy + 1, Db.GetGameState().Economy, 6);
        Assert.Equal(expected, Db.GetFaction(Minister)!.Popularity, 6);
    }

    [Fact]
    public void TheLedgerEntries_SumToThePurseChange()
    {
        var project = Start("works");
        Fund(project, 4);
        var before = Db.GetPurseBalance(Ministry.CommerceLabor);
        var lastId = Db.GetLedger().Max(e => e.Id);

        Resolve();

        var after = Db.GetPurseBalance(Ministry.CommerceLabor);
        var entries = Db.GetLedger(LedgerAccount.CommerceLabor).Where(e => e.Id > lastId).ToList();
        Assert.Equal(after - before, entries.Sum(e => e.Amount));
        Assert.Equal(-4m, Payments(project).Sum(e => e.Amount));
    }

    [Fact]
    public void ProgressAndHaltEffects_ActOnTheLiveProject()
    {
        var project = Start("works");

        Apply(new Effect("progress.works", 3));
        Assert.Equal(3m, Reload(project).Progress);
        Apply(new Effect("progress.works", 10));
        Assert.Equal(6m, Reload(project).Progress);

        var report = Resolve();
        Assert.Equal(ProjectStatus.Finished, Reload(project).Status);
        Assert.Single(report.ProjectsFinished);
        Assert.Empty(Payments(project));

        var second = Start("works");
        Apply(new Effect("halt.works", null));
        Assert.Equal(ProjectStatus.Halted, Reload(second).Status);
        Resolve();
        Assert.Empty(Payments(second));
        Assert.Equal(ProjectStatus.Halted, Reload(second).Status);
    }

    [Fact]
    public void TheGmIsAlerted_OneResolutionBeforeABonusDeadlineItWillMiss()
    {
        var project = Db.StartMinisterialProject(Minister, "harbour", "tovrerie", Config).Project!;

        var report = Resolve();

        Assert.Contains(report.Alerts, a => a.Contains("Naval harbour"));
        Fund(project, 10);
        Assert.True(ProjectForecast.For(Reload(project), 10, Graft, Db.GetGameState()).BeatsBonus);
    }
}
