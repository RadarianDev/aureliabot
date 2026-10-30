# Implementation Plan

How to build the bot described in [Aurelian Assembly Bot Spec.md](Aurelian%20Assembly%20Bot%20Spec.md).
The spec says **what** the game does; this file says **how** the code is laid out, what gets built
in which order, and how the work splits across Claude sessions and subagents.

Read first: [CLAUDE.md](CLAUDE.md) (conventions), [Human Rules.md](Human%20Rules.md) (the player-facing
rules, which win over the spec where they differ), the spec, and
[DiscordBotOne/CLAUDE.md](DiscordBotOne/CLAUDE.md) (a larger bot built on the same skeleton, used
as the pattern library below).

## 1. Before the first session

Things only the GM can do, all needed before stage 2 can be tested live:

- [x] Bot token in `appsettings.Local.json` (`Discord:Token`). The guild id is already set.
- [x] In the Discord developer portal, enable the **Server Members** and **Message Content**
      privileged intents. The second lets `/gm party programme` read the linked message's text, and
      both go into `GatewayIntents` in `Bot.cs`.
- [x] Give the bot **Manage Channels** and **Manage Roles**, and drag its role above where the
      party roles will go. Move any other coloured roles below that point.
- [x] Create the categories (factions, parties, archive), the channels (#news, #dashboard,
      #assembly, #campaign-log, #cabinet, #gm), a GM role and a Cabinet role (`/gm setup` does all of this). Deny Connect on the turn tracker
      voice channel `1565059818731995166`.
- [x] Province populations and geography: in [Lore.md](Lore.md).
- [x] Province ids, starting conditions and coastal flags: in [config/provinces.yaml](config/provinces.yaml).
- [x] Condition catalogue: in [config/conditions.yaml](config/conditions.yaml).
- [x] Opening issues, bills, projects and event: in `content/`.
- [x] UP and DOWN custom emoji: `<:stat_raise:1565442139855392808>` and
      `<:stat_lower:1565442180057927860>`, in the spec's `constants.yaml` as `display.upEmoji` /
      `display.downEmoji`.

## 2. Patterns to reuse

Everything below already exists in this repo or in DiscordBotOne. Copy the pattern, don't invent a
new one.

| Need | Pattern | Where to look |
| --- | --- | --- |
| Persistence | `partial class DatabaseService`, one file per domain; positional `record` + `SelectXColumns` + `ReadX(reader)` | [Database/DatabaseService.Parties.cs](Database/DatabaseService.Parties.cs) |
| Multi-step writes | One connection and transaction; private static `…Tx(SqliteConnection conn, SqliteTransaction tx, …)` helpers | `DiscordBotOne/Database/DatabaseService.Navy.cs`, `…Orders.cs` |
| Schema changes | `CREATE TABLE IF NOT EXISTS` in `InitializeDatabase()`; `AddColumnIfMissing` for existing tables | [Database/DatabaseService.cs](Database/DatabaseService.cs) |
| Scheduled work | `PeriodicTimer` poller started with a fire-and-forget `Task.Run` from `Bot.RunAsync`, catching every exception inside the loop, checking once on startup | `DiscordBotOne/AutoAdvanceTurnService.cs` |
| Discord side effects | A service holding `DiscordSocketClient`, registered in DI; `DatabaseService` never touches Discord types | `DiscordBotOne/MarketBoardService.cs` |
| Randomness | `IRandomSource` injected into resolution; a scripted fake in tests | `DiscordBotOne/Database/DatabaseService.Combat.cs`, `DiscordBotOne/SundamarBot.Tests/ScriptedRandomSource.cs` |
| Slow commands | `await DeferAsync()` first, then `FollowupAsync` | `DiscordBotOne/Commands/DealsModule.cs` |
| Autocomplete | Separate `…Autocomplete` handler classes | `DiscordBotOne/Commands/*Autocomplete.cs` |
| Buttons | `[ComponentInteraction("prefix:action:*")]` in the owning module | `DiscordBotOne/Commands/DealsModule.cs` |
| Tests | xUnit project in a subfolder; `DatabaseFixture` on a temp SQLite file via the `internal` connection-string constructor | `DiscordBotOne/SundamarBot.Tests/` |

House rules from CLAUDE.md that every session must follow: no code comments, mechanics documented
in `.md` files, Discord ids stored as `TEXT`, invariant culture (already pinned in `Program.cs`),
and never edit `DiscordBotOne/` or `DiscordBotTwo/`.

## 3. Layout

New and changed files, by folder. Names are binding, because parallel sessions code against them.

```
Program.cs, Bot.cs                 DI registrations, poller start-up, member join/leave handlers
BotConfig.cs                       unchanged
config/                            constants.yaml, provinces.yaml, conditions.yaml (copied to output;
                                   provinces and conditions already written)
content/                           starting templates for /content import: issues/, bills/, projects/,
                                   events/ (not copied)
assets/map/                        already done
Game/
  GameConstants.cs                 records mirroring constants.yaml, plus ConstantsStore (load, reload, Current)
  GameEnums.cs                     Season, Stance, Lean, Ministry, IssueStatus, BillStatus, ProjectStatus,
                                   CrisisStatus, VoteChoice, TemplateKind, …
  GameCalendar.cs                  season/year arithmetic, term and pre-election logic (incl. snap
                                   elections), next-resolution time
  Effects.cs                       Effect record, EffectParser (text and YAML), key validation
  ProgrammeParser.cs               pure: message text + open issues -> parsed programme or line errors
  EffectSummary.cs                 pure: effects -> player-facing summary lines (emoji, names, the
                                   people's mood and Threat as direction only); used for events, crises,
                                   laws, projects
  Wording.cs                       pure: opinion, salience, discontent and Favour numbers -> words
                                   (spec section 9)
  Formulas.cs                      pure static: provincial opinion, stance score, salience factor, campaign,
                                   attack, crisis, law, drift (opinion, discontent, Favour, popularity),
                                   programme flips, crackdown, fiscal (with the unrest drag) and navy maths
  ElectionCalculator.cs            pure: spec section 8, input records in, ElectionResult out
  SeededRandomSource.cs            IRandomSource over System.Random(seed)
Database/
  DatabaseService.cs               InitializeDatabase() holds the whole schema (stage 0)
  DatabaseService.Settings.cs      channel/category/role settings, game state row
  DatabaseService.Factions.cs      extended
  DatabaseService.Parties.cs       extended (role/channel ids, join applications, programmes)
  DatabaseService.Issues.cs        issues, bills, templates, stance log, province conditions
  DatabaseService.Events.cs        events, templates, the queue
  DatabaseService.Actions.cs       campaign/attack/poll submissions
  DatabaseService.Government.cs    cabinet, readings, votes, opposition actions, questions, no confidence
  DatabaseService.Crises.cs        crises, answers, templates, responses, condemnations
  DatabaseService.Treasury.cs      purses, upkeep, reserve, the ledger, projects (spending, graft,
                                   bonus deadlines)
  DatabaseService.Forces.cs        divisions and gendarmerie (one unit model), pending orders, crackdowns
  DatabaseService.Resolution.cs    Resolve(...) and its …Tx steps; campaign-day variant
  DatabaseService.Log.cs           LogEntry writes and reads
Services/
  ResolutionService.cs             the poller: due check, snapshot, Resolve, then publish the report
  SnapshotService.cs               backup, prune to 30, restore for undo
  Publisher.cs                     turns a ResolutionReport into posts, private messages, dashboard edit
  TurnTrackerService.cs            rename queue, latest-name-wins, rate-limit aware
  PartySyncService.cs              idempotent role + chat reconciliation per party
  CabinetSyncService.cs            idempotent Cabinet role membership from the sitting cabinet
  FactionChannelService.cs         create/repair private faction channels
  AlertService.cs                  posts to #gm
Commands/
  FactionModule.cs, PartyModule.cs, CampaignModule.cs, AssemblyModule.cs (/bill, /vote, buttons),
  OppositionModule.cs, MinisterModules.cs, MapModule.cs, InfoModule.cs (/info, one autocomplete
  across every kind), StatusModule.cs (/me, /help), …Autocomplete.cs
  Preconditions.cs                 role checks shared by every module
  Confirmations.cs                 shared Confirm/Cancel button flow for guarded commands
  Gm/GmModule.cs                   [Group("gm")] partial class; each subgroup a nested class in its own file:
  Gm/GmModule.Party.cs, Gm/GmModule.Event.cs, Gm/GmModule.Resolve.cs, Gm/GmModule.Setup.cs, …
  Content/ContentModule.cs         [Group("content")]: import, edit (YAML in a modal), preview, ready,
                                   list, remove, export; one YAML codec for every template kind
Rendering/                         ArchGenerator (seat chart), ProvinceMapGenerator (map), unchanged
AureliaBot.Tests/                  xUnit, mirrors SundamarBot.Tests
```

`/gm` and `/content` each have to be one partial class because Discord.Net cannot spread one
top-level group over several module classes. Splitting authoring into `/content` keeps both well
under Discord's 25-entry limit per group (`/gm` has 18 entries, `/content` 7).

## 4. Key designs

### 4.1 Config

`ConstantsStore` loads `config/*.yaml` with YamlDotNet (camelCase naming convention) into immutable
records, validates them (for example: every province id in `startingForces` exists, every `mapKey`
is unique), and exposes `Current`. `/gm reload` builds a new instance and swaps it atomically;
a validation failure keeps the old one and reports the error. Values already in the database are
never touched by a reload.

### 4.2 Calendar and scheduling

- `GameCalendar` is pure: `Next(season, year)`, `IsPreElection(state)`, `TermEnd(state)`,
  `NextResolutionAt(lastResolution, constants, seasonLengthDays)`. Time zone via
  `TimeZoneInfo.FindSystemTimeZoneById(constants.TimeZone)` (IANA ids work on Windows .NET).
- `ResolutionService` polls every minute. Each tick:
  1. skip if paused;
  2. if a campaign day is due, run the campaign-day variant;
  3. if `nextResolutionAt <= now`, run one full resolution, then schedule the next one from the
     current time, so a missed day runs once and is never replayed (alert the GM when the
     resolution was more than one poll interval late).
- `/gm resolve now` calls the same entry point as the poller, behind a single `SemaphoreSlim`, so
  the two can never overlap.

### 4.3 Resolution pipeline

```
ResolutionService
  -> SnapshotService.Backup()                     before anything else
  -> DatabaseService.Resolve(constants, random)   one connection, one transaction, spec section 7 steps 1-11
       returns ResolutionReport                   nothing is sent to Discord inside
  -> Publisher.Publish(report)                    after commit: news, results, private reports, dashboard, map
  -> TurnTrackerService.Request(name)
  -> AlertService for report.Alerts
```

- `ResolutionReport` is a plain record holding everything to post: news lines, fired events, crisis outcomes,
  vote results and laws, started and finished projects, election result (with `ElectionResult` and the province winners), private messages
  keyed by faction, cabinet report, alerts, and the seed.
- Each spec step is one `…Tx` method in `DatabaseService.Resolution.cs`, which loads what it needs,
  calls `Formulas`/`ElectionCalculator`, writes back, and appends to the report.
- **Simultaneous actions** (spec section 5): load all pending actions and the start-of-turn state
  first, compute every delta, then apply them in one pass, then raise salience.
- If any step throws, the transaction rolls back, the snapshot is left as the latest one, nothing
  is published, and the GM gets an alert with the exception.
- If publishing fails half way, the game state is already correct. Log it and alert the GM; there
  is no automatic re-post.
- The seed is generated per resolution, stored in `GameState.rngSeed`, and reused on a re-run after
  undo.

### 4.4 Discord synchronisation

Party roles and chats, faction channels and the turn tracker follow one rule: **the database is
the truth, and a sync method makes Discord match it.** Each sync method is idempotent, so the same
code runs after a command, on startup, and from `/gm setup`.

`PartySyncService.SyncAsync(partyId)`:
1. Role: create if `roleId` is missing or the role is gone; set name, colour, hoisted.
2. Members: add the role to every active member, remove it from everyone else who has it.
3. Chat: create if missing under the party category; set overwrites (deny @everyone, allow the party
   role, the GM role and the bot; Pin Messages for the party role).
4. Store any new ids. Any Discord refusal goes to `AlertService` and leaves the database unchanged.

Deletion archives the chat (move to the archive category, read-only for the GM role) and deletes the
role. `UserJoined` re-runs the sync for the rejoining player's party and the cabinet; `UserLeft` raises the alert.

`CabinetSyncService.SyncAsync()` works the same way for the one Cabinet role: holders are the
sitting cabinet's Chancellor and ministers (spec section 5, Cabinet role). It runs after every
cabinet change (approval, imposition, appointment, resignation, retirement), on startup and from
`/gm setup`. It only manages membership: the role is uncoloured and not hoisted, and #cabinet's
overwrites (the Cabinet role, the GM role and the bot) are set once by `/gm setup`.
Both need the Server Members intent added to `GatewayIntents` in `Bot.cs`.

`TurnTrackerService` keeps only the latest requested name and a timestamp list of recent renames,
and waits when two renames already happened in the last 10 minutes.

### 4.5 Snapshots and undo

- Backup with `SqliteConnection.BackupDatabase` into `snapshots/<yyyy-MM-dd-HHmm>-<Season><Year>.db`;
  keep the newest 30.
- Undo: pause the schedule, `SqliteConnection.ClearAllPools()`, copy the snapshot over the live
  file, delete that resolution's posts (message ids are kept in the log), re-pin the old dashboard,
  list the lost commands from `LogEntry` to the GM, and leave the game paused.

### 4.6 Effects

`EffectParser` handles both `discontent.north_arpal -2, economy -1, open.public_health` (modal text)
and YAML maps, returns `IReadOnlyList<Effect>`, and rejects unknown keys and unknown ids. The
`open.*`, `close.*`, `bill.*`, `project.*` and `crisis.*` action keys take no value, `here` resolves to
the project's province inside project effects, and `condition.*` accepts only +1 or −1. Applying
effects is one `ApplyEffectsTx` method used by laws, events, crisis answers, projects and `/gm set`,
which clamps every value. Every purse or reserve change, there and everywhere else, goes through one
`PostLedgerTx(account, amount, kind, label, source)` helper, so nothing moves money without a ledger line.

### 4.7 Commands

- Every command that creates channels or roles, renders an image, or touches many rows starts with
  `DeferAsync`.
- Permission checks: GM commands use `[RequireUserPermission(GuildPermission.ManageGuild)]`; role
  checks (minister, Chancellor, party member, opposition) are small precondition attributes in
  `Commands/Preconditions.cs`, so every module shares them.
- Long text goes through modals (`[ModalInteraction]`), never through slash options.

## 5. Stages

Each stage ends with `dotnet build` clean, `dotnet test` green, and a manual check on the real
server. Stage numbers match spec section 11, with stage 0 added.

### Stage 0: contracts (one session, no subagents)

Everything later work codes against. Small but must be right first.

- `Game/GameEnums.cs`, `Game/GameConstants.cs` + YAML files with the spec's values, `ConstantsStore`.
- The **full schema** in `InitializeDatabase()`, all tables from spec section 3, plus
  `AddColumnIfMissing` for the existing `factions` and `parties` tables. Writing it once up front
  avoids every parallel session editing the same method.
- `IRandomSource` (`RollDie(int faces)`, `NextDouble()`), `SeededRandomSource`.
- `ResolutionReport` record and an empty `Resolve` that only advances the calendar.
- `AureliaBot.Tests` project (csproj `Remove` globs and `InternalsVisibleTo` as CLAUDE.md
  describes), `DatabaseFixture`, `ScriptedRandomSource`, one smoke test.
- Service skeletons registered in `Bot.cs`.

Done when: build and smoke test pass, and the bot starts against a fresh database.

### Stage 1: foundation

- `ResolutionService` poller, `/gm resolve` (now, delay, pause, resume), season length override.
- `SnapshotService`, `/gm undo`.
- `/gm channel set`, whitelist integration.
- `TurnTrackerService`, wired to resolution, undo, pause and resume.
- `/gm reload`, `/gm set` for game-state fields.
- Test command that advances the season.

Tests: `GameCalendar` (term boundaries, pre-election length, a snap election moving the
pre-election and restarting the term, year roll-over after Winter, next-resolution time across a
DST change), undo restoring a known state.

### Stage 2: factions and parties

- `/info` for factions, `/gm faction create` (the only way a faction is made), the shared "not registered
  yet" refusal for players without a faction, `FactionChannelService`, `/gm setup`.
- `Confirmations` and `Preconditions`, used by every later stage.
- `/gm party create|edit|move|delete` (create is the only way a party is founded; colour-similarity
  warning on create and edit; move overrides the joining rules), party repair in `/gm setup`,
  `/gm faction edit|retire`, `/gm reward` (a royal commendation in #news).
- `/party apply|leave`, join applications with Accept/Decline buttons in the party chat, decided by a
  majority of active members (`PartyJoinAnswer`); applying only during a pre-election.
- Dissolution: one `DissolveIfUnderTwoTx` check run after every leave, retire and GM move, sharing
  the `/gm party delete` path.
- `/gm party programme party message`: resolve the message link (guild, channel and message ids) to
  a message, run `ProgrammeParser`, show the GM a private preview (with flips and their cost) and
  Record/Cancel buttons. On record, replace the programme, apply flip penalties outside a
  pre-election, log it, post it as a manifesto or a line change, and reply to the original message.
- `/info` for parties, and first versions of `/me` and `/help` (each later stage adds its own lines).
- `PartySyncService`, member join/leave handlers.
- Dashboard first version (parties, seats, calendar).

Tests: membership rules (creation needs two independent founders, applying is refused outside a
pre-election, a join needs a majority of accepts and a majority of declines refuses it, a GM move
works in any season, retire clears party and ministry, a party left with one active member dissolves and its last member
keeps its seats), programme flips (only sign changes cost, nothing costs during a pre-election), and
`ProgrammeParser`: the spec's example message, header lines ignored, title and id matching, emphasis
and capitalisation variants, missing issues having no stance, and every error case (unknown, closed
or repeated issue, bad opinion including *Silent*) reported together.

### Stage 3: issues, events and campaigning

- `/content import|edit|preview|ready|list|remove` for every kind of template, all as YAML: kind
  recognised from the fields, per-file and per-line errors, a replaced Ready template returning to
  Draft, a changed id in `edit` saving a copy, `edit live` for live copies; and the readiness rule (a
  template is Ready only when everything its effects reference is Ready).
- Event queue, `/gm event queue|cancel` with delays, events firing at the start of a turn,
  opening or closing issues and making bills Available through `bill.*` (closing an issue withdraws
  its bills); `project.*` is parsed and validated from here, and applied from stage 7; province conditions (biases, resting discontent, growth),
  `/info` for provinces, issues and bills, `/gm apply`.
- `/gm start`: game state from constants, provinces and conditions, the fiscal game start, Spring
  1850 as the founding pre-election, and the events queued with no delay fired at once.
- Event `schedule` (`{ season, year }` or `{ delay }`): queued on `/content ready`, moved or
  cancelled when the schedule changes, written back by `/content export`.
- `/campaign`, `/attack`, `/poll` (answered in words through `Wording`), simultaneous resolution
  with campaign crowding, stance log, drift and decay; campaigns and attacks posted to
  #campaign-log with the resolution.

Tests: `Formulas` (every branch of the stance score, population-weighted national opinion, campaign crowding, attack backfire, condition
biases and resting discontent, popularity drifting toward the start from above and below and never on
campaign days), the two-actions rule (a second action of the same kind replaces the
first, a third kind is refused while two are pending), simultaneous resolution being independent of
submission order,
events firing in queue order and never on campaign days, `/gm start` (the first turn is a
pre-election, events queued with no delay fire at the start and delayed ones that many turns later,
a second start is refused), event schedules (a date converted to the right turn, Spring 1850
firing at the start, a past date refused on Ready, a changed schedule moving the queued event), `Wording` thresholds, and `EffectSummary`
(public numbers such as purses with amounts, mood values including discontent direction only,
opinion and baseline indistinguishable, action lines, zero effects dropped). The YAML codec: every
starting file in `content/` imports cleanly, each kind is recognised from its fields, a bad file
reports its line and blocks only itself, re-importing an id replaces it and returns a Ready template
to Draft, `edit` with a new id saves a copy, and an export imports back unchanged. Bills: `bill.*`
refused for a closed issue, and a closed issue withdrawing its bills.

### Stage 4: elections

- `ElectionCalculator` with the spec's worked example as the first test (A 15, B 10, C 5), plus
  apportionment, the √n crowding term, tie-breaks, zero popularity.
- Results posting: seat chart (`ArchGenerator`, parties largest first), province map
  (`ProvinceMapGenerator`), `ProvinceResult` rows, `/map`, `/gm peek election`.
- Election boundary cancellations.

### Stage 5: government

- Cabinet lifecycle, `/chancellor nominate`, `/gm cabinet chancellor|impose`,
  `/gm proclaim`, royal proclamation posts, `/ministry resign`, `/ministry status`,
  `CabinetSyncService` wired to every cabinet change.
- `/bill propose bill` as a new reading scheduled for the next turn (the Chancellor alone for
  government bills, `billsPerSeason` a turn, none while the Chancellorship is vacant; kind from the
  proposer's role; one bill per issue before the Assembly, scheduled ones included; withdrawal from
  `/me` until the vote opens; no proposing in a pre-election or the turn before a scheduled one),
  readings opened in resolution step 10 with vote buttons on #assembly posts, a running tally and
  `/vote` as fallback, laws with the bill's lean, per-bill cooldowns, a law closing its issue
  withdrawing the issue's other bills.
- `/noconfidence` for any active faction outside the cabinet, with Cosign buttons open to the same
  factions (repeated in `/me`), the share-based threshold and lapsing, and snap elections.
- `/opposition question|condemn` (one opposition action a season, spent on a
  question, a condemnation or a private bill through `/bill propose`; condemnations posted to #news
  at once), questions with the minister's penalty and the asker's reward.
- One `ResponsibleMinister(ministry)` lookup, the holder or else the Chancellor, used by crises,
  questions, autocomplete and reminders.

Tests: vote counting (abstentions, ties, filled seats for no confidence), votes logging the bill's
lean (Yes on an *Against* bill logs Against), the sponsor's reward signed by the lean, a rejected bill
resting while its rivals stay proposable, a proposed bill opening at the next resolution and counted
at the one after, ministers and a vacant Chancellorship unable to propose government bills, a
withdrawn bill refunding its slot, a bill scheduled into a snap pre-election cancelled without a
cooldown, a governing-party backbencher able to table and co-sign no confidence while a minister
cannot, the tabling threshold
following the Assembly size, a gathering motion lapsing without a cooldown, resignation (vacant
ministry, cancelled pending nomination), caretaker rules, Cabinet role holders (nominees excluded
until approval, a caretaker keeping it, an appointment to a vacant Chancellorship granting it at
once, a resignation removing it), a passed
no-confidence motion making the next season the pre-election, independents being neither government
nor opposition, the shared opposition action limit (a private bill uses it, and only one bill a
year), a vacant ministry's question going to the Chancellor, question deadline shifting on a change
of minister.

### Stage 6: crises

- `/info` for crises; `/crisis respond` with the purse check and complication warning.
- Crisis templates (two or more answers including Neutral, numbered by
  order, price defaulting to 0, the standard complication when none is written), posting through
  the `crisis.*` event effect with the deadline at the next resolution, responses kept private until
  resolution, clean/complicated/default outcomes with effect summaries, condemnation scoring, vacant
  ministries' crises answered by the Chancellor.

Tests: Neutral answers gain and lose nothing, a Neutral minister answer counts as not on the right
side for condemnations, the standard complication applying when a crisis has none, a vacant
ministry's crisis scores the
Chancellor's faction, and the default applies with no penalty only when the Chancellorship is vacant
too.

### Stage 7: budget and standing work

- Fiscal year, game-start budget, condition growth, purses, upkeep from the War and Interior purses,
  Navy contribution changes and Favour drift, reserve and grants; `/treasury allocate` as a
  pre-filled modal with a reserve and Favour preview.
- Divisions and gendarmerie as one unit model parameterised by their constants (raise, train,
  station, disband), pending orders, crackdown rolls, projects started by `project.*` effects or
  `/ministry project`, `/ministry fund` spending with graft above the pace, stalling, bonus deadlines,
  `/info` for projects, ministries and the budget, with the ledger behind them.

Tests: Favour reacting only to a change in the Navy share, the unrest drag (weighted by population,
only discontent above `discontent.rest` counts, the spec's whole-nation-at-7 example costing 2%), unpaid upkeep emptying the purse and
costing every unit of that branch one training step but never below base sides, retraining a lost
step at that step's cost, project progress (spending at the pace, graft on the excess, never paying
more than needed to finish, a partial payment from a short purse, the spec's fen drainage example
beating its deadline at 2 a season for 5 crowns), bonus deadlines by turn and within n seasons, the ledger (every purse change has an
entry, the entries sum to the balance, an entry stays private to the minister and the Chancellor
until the next resolution, a private `/gm apply` shows only as a royal adjustment), crackdown success and failure with a scripted random source.

### Stage 8: GM tools

- `/content export` (all templates or one, as a zip), `/gm agenda`, alerts
  list from spec section 10, reminders, `/gm roll battle|riot`, `/gm peek`, undo's Resubmit buttons.

## 6. Working with subagents

The main session owns the shared contracts and the merge; subagents get narrow, file-disjoint
tasks. Run parallel subagents with `isolation: "worktree"` so they never edit the same checkout.

**Never delegate:** stage 0, schema changes, `Bot.cs`, `GameConstants`, `ResolutionReport`, the
final merge, and anything that needs a live Discord test.

**Good parallel tasks**, by stage:

| Stage | Track A | Track B | Track C |
| --- | --- | --- | --- |
| 1 | `GameCalendar` + tests | `SnapshotService` + undo | `TurnTrackerService` |
| 2 | Faction commands + channel service | `PartySyncService` | `/party apply` with join buttons, and `/gm party programme` |
| 3 | `Formulas` + tests | YAML codec and `/content` | Campaign/attack/poll commands |
| 4 | `ElectionCalculator` + tests | Results posting and `/map` | |
| 5 | Vote counting + tests | Cabinet and question commands | Bills and no confidence |
| 6 | Crisis outcome formulas + tests | `/crisis respond` and condemnations | |
| 7 | Fiscal and Favour maths + tests | Forces, pending orders and crackdowns | Projects and graft + tests |
| 8 | `/content export` and `/gm agenda` | Alerts and reminders | Battle and riot rolls |

Pure-logic tracks (`Formulas`, `ElectionCalculator`, `GameCalendar`, fiscal maths) are the best
fit: they need no Discord, their tests prove them, and they touch no shared files.

**Brief template** for each subagent:

```
Goal: <one task from the table>, for AureliaBot.
Read: CLAUDE.md, Implementation.md sections 2-4, spec section(s) <n>.
Files you own: <list>. Do not edit any other file; if you need a change elsewhere
(schema, Bot.cs, GameConstants, ResolutionReport), stop and report what you need.
Follow the patterns in Implementation.md section 2. No code comments.
Done when: dotnet build has no new warnings, dotnet test passes, and you report
the public signatures you added and any spec ambiguity you hit.
```

**Review:** after each stage, give a fresh agent the diff and the relevant spec sections (not your
summary of them) and ask it to find mismatches. Fix, then do the live check on the server.

## 7. Open questions

None block a stage. Everything is decided in the spec, `config/` and `content/`.

**Tune in playtesting.** These have working values that should be checked on a real game:

| Value | Check at |
| --- | --- |
| Condition biases and the issues' starting opinion and salience (`config/conditions.yaml`, `content/issues/`): run `/gm peek election` and polls across provinces, and look for provinces that agree on everything | Stage 4 |
| Crackdown balance (`crackdown.cost`, `reduction`, `backlash`, `failurePopularity`) | Stage 7 |
| Wording thresholds for opinion, salience and discontent (spec section 9) | Stage 3 |
| Upkeep against the default War and Interior shares, so training is lost only when a Treasury underfunds on purpose | Stage 7 |
| Programme flip penalty and Favour drift | Stages 2 and 7 |
| Popularity drift against campaign gains: at 0.05 an idle faction keeps about 81% of its lead over 10 after a year and 44% after a term; a faction campaigning well every turn should still settle well above 10 | Stage 3 |
| Unrest drag (`discontent.growthDrag`) against base growth | Stage 7 |
| Project prices and terms against purse sizes (Commerce and Labor gets about 5 a year at the default split, while the starting projects' paces are 1 to 2 a season), and `project.graft` | Stage 7 |
| The size of bills' pass effects, especially the Act of Union's Favour hit and the Corn Duties' swing between town and farm | Stage 5 |
