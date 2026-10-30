# Aurelian Assembly: Bot Spec

Oct 28, 2026 · @eM

Implementation spec for the Discord bot that runs Aurelian Assembly. Read with [Human Rules.md](Human%20Rules.md), the player-facing rules. Where they differ, the Human Rules win, and this spec is fixed to match. Every number lives in config and can be rebalanced without code changes.

## 1. Overview

**What the bot does**

- Tracks game state: factions, parties, seats, provinces and their conditions, issues, events, crises, the budget, forces and projects.
- Collects player actions through slash commands.
- Resolves the game once a day, and posts the results.
- Runs elections.
- Alerts the GM when thresholds are crossed.

**What the bot does not do**

- It never writes story content. The GM writes every event, issue and crisis, in YAML or inline, and decides all follow-ups. The bot only fires queued events at the start of a turn (section 7), and events bring the issues and crises with them.
- It keeps no story flags.

**Design principles**

- **Events drive the story.** Issues, crises and changes to provinces all enter play through events, so every turn opens with something happening in the world.
- **The state is public, the people's mood is told in words.** Money, prices, popularity, seats and forces are public numbers. The people's mood (opinion, salience, discontent, Favour) reaches players only as words or as a direction, and Threat only as a direction. Conditions and events are public, so players infer the mood from what they can see.
- **Party politics stay in roleplay.** Each party has its own written constitution, which no bot could enforce. The GM founds parties and records programmes; the only party vote the bot holds is on admitting a new member. Every other party decision is up to its members.
- **Few decisions for players, heavy lifting for the bot.** A faction makes a couple of decisions a day. Upkeep, drift, budgets and elections are the bot's job.
- **Everything speaks in character.** Posts are written as the world would report them: royal proclamations, news, dispatches from the hustings.

**Tech stack**

| Part | Choice |
| --- | --- |
| Language | C# (.NET 10) |
| Discord library | Discord.NET, slash commands, buttons and modals |
| Storage | SQLite through Microsoft.Data.Sqlite, as in the existing code: the schema lives in `InitializeDatabase()`, new columns are added with `AddColumnIfMissing`, and there is no migration framework |
| Bot settings | Token, guild id and database path in `appsettings.json` / `appsettings.Local.json`, as today |
| Game config and templates | Constants in YAML, read with YamlDotNet and reloadable at runtime. Issue, event and crisis templates in the database, edited from Discord, with YAML import and export |
| Scheduling | A background poller, as in SundamarBot's `AutoAdvanceTurnService`: a `PeriodicTimer` started from `Bot.RunAsync`, which checks every few minutes (and on startup) whether a resolution is due, and resolves the turn daily at **08:00 in the configured time zone** |
| Rendering | SkiaSharp, for the seat chart and the province map |
| Hosting | A Windows machine, run the same way as SundamarBot. SkiaSharp's Windows native libraries come with the base package |

**Scope and roles**

- The bot runs one game, in the guild set by `Discord:GuildId` (on this server, `1563971520102015058`).
- The GM is anyone with the Manage Server permission. In the fiction, the GM is **His Majesty King Arthas II**. A **GM role**, set with `/gm channel set`, is given access to every private channel the bot creates. A **Cabinet role**, also set with `/gm channel set`, is held by the sitting Chancellor and ministers and opens #cabinet (section 9).
- The bot needs the Manage Channels and Manage Roles permissions, and its own role must sit above every party role, or Discord won't let it assign them.

**Conventions**

- All money is in **crowns** and stored as a decimal with two places.
- Popularity, opinion, discontent and the other values are stored as doubles, and clamped to their ranges after every change.
- Every random roll uses one seeded random generator per resolution, and the seed is logged so results can be reproduced.
- Every deadline and resolution time is shown as a Discord timestamp (`<t:…:F>`), so each player sees it in their own time zone, next to the in-game season.

## 2. Time model

- **Turn:** one turn is one in-game season (Spring, Summer, Autumn, Winter) and lasts one real day, from one 08:00 resolution to the next.
- **Resolution:** the full daily resolution in section 7 that ends a turn. Every deadline counts in resolutions. The campaign days of a pre-election (below) are not resolutions.
- **Calendar:** the bot stores the season and the year, and every post is dated, for example "Summer 1851".
- **Start:** the GM opens the game with `/gm start` (section 6). The first turn, **Spring 1850**, is a founding pre-election: it runs like any pre-election, ending in the election that seats the first Assembly from Summer 1850. Before the start, the founding parties form and the opening event is queued for Spring 1850. `/gm start` fires it at once, so the first issues are open from the first day, and parties write their programmes as manifestos throughout the founding pre-election.
- **Term:** a term runs from one election to the next, normally **16 seasons**. Its last season is the **pre-election period**, which lasts **3 real days**; it is the only turn longer than one day. The first two days each end with a **campaign day** at 08:00 (section 7), and the third ends with the full resolution, which runs the election and begins the next term.
  - First term: Summer 1850 to Spring 1854, with the pre-election in Spring 1854.
  - **Snap election:** a passed no-confidence motion cuts the term short. The season that the same resolution opens becomes the pre-election, and the new term runs 16 seasons from the election after it.
  - The season and year of the next pre-election are stored in the game state, set when a term begins and moved by a snap election.
- **Fiscal year:** the year's Budget is computed at the start of each Winter. The Treasury allocates it during Winter, and the allocation takes effect at the Winter resolution, which opens Spring. At game start the bot runs the Winter budget step once with the default percentages, so the purses are funded from Spring 1850.
- **Deadlines:** anything with a deadline (crises, questions, votes) is due at a resolution.

**Scheduling**

- `nextResolutionAt` is stored in the game state. After each resolution it is set to the first 08:00 that is at least `minHoursBetweenResolutions` later, plus any extra days for a longer season. The time zone comes from config, so daylight saving never shifts the hour.
- If the bot starts and `nextResolutionAt` has already passed, it runs **one** resolution, schedules the next one normally and alerts the GM. It never runs more than one resolution to catch up.

**GM controls**

- Delay the next resolution by any number of hours.
- Trigger a resolution early.
- Pause and resume the schedule.
- Set the length of any season in days, overriding the defaults.

## 3. Data model

These are the main tables. Ids are short strings (slugs) for anything defined in YAML, Discord snowflakes (stored as `TEXT`) for anything that is a Discord object, and integers for anything else created during play.

| Entity | Key fields |
| --- | --- |
| GameState | season, year, termNumber, termStartSeason, preElectionSeason, preElectionYear, resolutionNumber, nextResolutionAt, seasonLengthDays (null uses the default), paused, seats, taxRate, economy, favour, favourBaseline, lastNavyShare, threat, reserve, rngSeed |
| Channel | role (News, Dashboard, Assembly, CampaignLog, PartyRegistry, Cabinet, Gm, FactionCategory, PartyCategory, ArchiveCategory, TurnTracker, GmRole, CabinetRole), discordId (a channel, category or role id) |
| Faction | id (the player's Discord user id), characterName, popularity, homeProvinceId, partyId (null if independent), seats, privateChannelId, status (Active, Retired) |
| Party | id, name, colour, emojiId, createdSeason, roleId, channelId, programmeMessageUrl |
| PartyJoinRequest | id, factionId, partyId, requestedAt, status (Pending, Accepted, Declined, Dropped) |
| PartyJoinAnswer | requestId, factionId, accept |
| ProgrammeStance | partyId, issueId, stance. Only non-Silent stances are stored; a missing row means Silent |
| Province | id, name, population, discontent |
| ProvinceCondition | provinceId, conditionId, since |
| Issue | id, templateId, title, forText, againstText, opinion, salience, salienceRaisedResolution, status (Open, Closed) |
| Event | id, templateId, title, text, effects, status (Queued, Fired, Cancelled), queuedAt, fireAtResolution, firedResolution |
| Crisis | id, templateId, eventId, title, ministry, provinceId, issueId, answers, complication, default, postedAt, dueResolution, status, chosenAnswer, responseText |
| Answer (inside Crisis) | number (its position in the list, from 1), label, lean (For, Against, Neutral), price, text, effects |
| Bill | id, templateId, issueId, title, motion, lean (For, Against), passText, passEffects, status (Available, Voting, Cooldown, Passed, Withdrawn), cooldownUntil |
| Reading | id, billId, sponsorFactionId, kind (Government, Private), proposedAt, opensResolution, dueResolution, status (Scheduled, Open, Passed, Rejected, Cancelled). One reading is one vote on a bill; a bill rejected and proposed again gets a new reading |
| NoConfidence | id, tablerFactionId, tabledAt, gatherUntilResolution, dueResolution, status (Gathering, Voting, Passed, Failed, Lapsed, Cancelled) |
| NoConfidenceCosigner | noConfidenceId, factionId |
| Vote | id, subjectType (Reading, NoConfidence, Cabinet), subjectId, factionId, choice (Yes, No, Abstain), castAt |
| Cabinet | id, kind (Sitting, Nominated), chancellorFactionId, status (Approved, Caretaker), ministries mapped to holder faction ids, dueResolution (for a nomination) |
| Purse | ministry (War, Interior, CommerceLabor, Treasury, Navy), balance, allocatedThisYear |
| LedgerEntry | id, account (a ministry or Reserve), amount (negative for spending), kind (Allocation, Grant, CarryOver, Interest, Upkeep, Purchase, Crackdown, Crisis, Project, Graft, Effect, Adjustment), label, sourceType, sourceId, madeAt, resolutionNumber, season, year, publicFromResolution |
| Division | id, name, sides, provinceId |
| GendarmerieCorps | id, name, sides, provinceId |
| PendingOrder | id, unitType (Division, Gendarmerie, null for a crackdown), unitId, kind (Raise, Train, Station, Crackdown), provinceId, cost |
| Project | id, templateId, title, purse, provinceId, price, term, progress, spending (the purse's planned spend per season), startedResolution, bonusDueResolution, effects, bonusText, bonusEffects, status (Active, Stalled, Finished) |
| Action | id, factionId, kind (Campaign, Attack, Poll), issueId, provinceId (polls only, null for a national poll), stance, offProgramme, targetFactionId, submittedAt, resolved |
| OppositionAction | id, factionId, kind (Question, Condemn, Bill), season, year |
| Question | id, askerFactionId, ministry, text, askedAt, dueResolution, answerText, answeredAt |
| Condemnation | id, factionId, crisisId, side (For, Against), madeAt |
| StanceLog | factionId, issueId, stance, season, source (Programme, Campaign, Vote), offProgramme |
| ProvinceResult | termNumber, provinceId, partyId, strength, seats |
| Snapshot | id, resolutionAt, file path to a copy of the database |
| LogEntry | id, at, kind, source, payload, publicFlag |

A faction has no name of its own. It is shown as the player's mention, followed by the character name where that helps.

**Templates:** IssueTemplate, BillTemplate, ProjectTemplate, EventTemplate and CrisisTemplate hold the same fields as their live copies, plus a status (Draft, Ready, Archived), createdAt, updatedAt and timesPosted. An EventTemplate also has an optional schedule: a season and year, or a delay. A ProjectTemplate also has `ministerial`, `requires` and a bonus deadline (a turn, or a number of seasons). A live copy records the templateId it was posted from.

**Stance** is an enum with values: StronglyFor = +2, For = +1, Silent = 0, Against = −1, StronglyAgainst = −2. Silent means *no stance*: it is what a party has on an issue its programme doesn't list or lists as Silent.

**Lean** of a crisis answer: For = +1, Against = −1, Neutral = 0. A bill's lean is For or Against its issue, never Neutral.

**Crisis ministries** are War, Interior, CommerceLabor and Treasury. The Navy purse is a contribution with no minister, so it can never hold a crisis.

**Effects** are a map from a key to a number change. Valid keys:

- economy, taxRate, favour, favourBaseline, threat, reserve
- seats (the Assembly size, applied at the next election)
- purse.{ministry}, for every ministry except Navy
- discontent.{province}
- condition.{province}.{condition}: +1 gives the province the condition, −1 removes it
- opinion.{issue}, salience.{issue}
- popularity.{faction}

`opinion.{issue}` moves public opinion on the issue, and it stays where it is put: opinion has no drift, so only effects and crisis outcomes move it. `favour` is a temporary shift that drifts back towards `favourBaseline`, and `favourBaseline` moves Favour for good.

Five more keys are actions, not number changes, and take no value (in YAML, the value `true`):

- open.{issueTemplate} opens a live issue from a Ready template;
- close.{issue} closes a live issue, and withdraws its bills;
- bill.{billTemplate} makes a bill from a Ready template available to propose. Its issue must be open, or opened earlier in the same effects;
- project.{projectTemplate} starts a project from a Ready template;
- crisis.{crisisTemplate} posts a live crisis from a Ready template (events only).

These work in events, crisis answers, bills' pass effects and finished projects, except `crisis`, which only events can use. A passed bill can open the next bill, or start a project. Issues leave play only through `close`; a bill that should settle its issue for good carries `close.{issue}` in its own pass effects.

In a project's effects, `here` stands for the project's province, as in `discontent.here`.

## 4. Configuration files

All files live in a `config/` folder. The GM reloads them with a command. A reload changes constants only, never values already in play.

**constants.yaml**

```yaml
seats: 180                       # starting Assembly size; reforms change GameState.seats
display: { upEmoji: "<:stat_raise:1565442139855392808>", downEmoji: "<:stat_lower:1565442180057927860>" }
start: { season: Spring, year: 1850 }
resolutionTime: "08:00"
timeZone: "Europe/Prague"        # IANA id
minHoursBetweenResolutions: 12
preElectionDays: 3
seasonsPerTerm: 16
popularity: { min: 0, max: 100, start: 10, drift: 0.05 }
campaign: { factor: 4, actionsPerTurn: 2, crowding: true }   # each of a different kind
attack: { factor: 3, backfire: 2 }
poll: { opinionNoise: 0.15, salienceNoise: 1 }
strongStancePenalty: 0.5
programme: { flipPenalty: 1 }
opinion: { crisisShift: 0.05 }
salience: { min: 0, max: 5, campaignRaise: 1, crisisRaise: 1, decay: 1, rejectDrop: 2 }
rejectedCooldownSeasons: 4
law: { sponsorFactor: 4 }
government: { billsPerSeason: 1 }
opposition: { actionsPerSeason: 1, privateBillsPerYear: 1,
              unansweredQuestion: 2, questionReward: 1, condemnReward: 2, condemnPenalty: 1 }
assembly: { noConfidenceShare: 0.1667, noConfidenceGatherResolutions: 2, noConfidenceCooldownSeasons: 4 }
election: { baseExponent: 0.85, homeMultiplier: 1.5, programmeWeight: 0.5,
            multiplierMin: 0.5, multiplierMax: 1.5, crowding: true }
economy: { start: 100, growth: 0.01 }
taxRate: 0.20
reserve: { start: 10, interest: 0.03 }
defaultAllocation: { War: 0.30, Interior: 0.20, CommerceLabor: 0.25, Treasury: 0.10, Navy: 0.15 }
favour: { start: 60, baseline: 60, drift: 1, navyFactor: 20, alertBelow: 30 }
threat: { start: 25, alerts: [60, 85] }
discontent: { min: 0, max: 10, start: 3, rest: 3, drift: 0.5, alertAt: 7, growthDrag: 0.005 }
division: { raise: 4, upkeep: 0.5, baseSides: 12, sidesStep: 2, maxSides: 18, trainCosts: [2, 3, 4] }
gendarmerie: { raise: 2, upkeep: 0.25, baseSides: 8, sidesStep: 2, maxSides: 12, trainCosts: [1, 2] }
crackdown: { cost: 1, reduction: 2, backlash: 1, failurePopularity: 2 }
project: { popularity: 2, graft: 0.5 }   # graft: share of spending above the pace that is lost
startingForces:
  divisions: { aurenten: 2, iracterie: 1, north_arpal: 1, agrinenten: 1, ocidanten: 1 }   # province: count
  gendarmerie: [aurenten, iracterie, north_arpal]
```

Unit upkeep (`division.upkeep`, `gendarmerie.upkeep`) is per unit per year, paid from the War and Interior purses each Spring (section 5). `opposition.privateBillsPerYear` caps how many of a faction's opposition actions in a year can be bills.

**provinces.yaml** ([config/provinces.yaml](config/provinces.yaml)): id, name, population, a one-line public description and starting conditions for each of the ten provinces, matching the census and geography in [Lore.md](Lore.md) (populations in millions; only the proportions matter, for seat apportionment). Harbours need the `coastal` condition. Each province also has a `mapKey`: its flat colour in the map's index image (section 8). Conditions are copied into the database at game start; after that, events and crisis answers change them.

**conditions.yaml** ([config/conditions.yaml](config/conditions.yaml)): the catalogue of province conditions. A condition is public, so it carries a name and a one-line description players can read. Its numbers are hidden.

```yaml
lawless:
  name: Lawless
  description: Gangs rule the back streets, and the watch looks away.
  biases: { army: 0.1, poor_relief: 0.2 }   # opinion bias per issue id; missing issues are 0
  discontentRest: 1                 # added to the province's resting discontent
  growth: -0.002                    # added to the national yearly growth, per province with it
```

Every field except `name` is optional and defaults to 0. Crime is not a separate value: it is the `lawless` condition. The catalogue has two kinds of condition, which the bot treats alike:

- **Standing** conditions describe a province's character and are set in `provinces.yaml`: urban, industrial, farmland, frontier, anzarian_border, crossroads, rugged, forested, marshland, remote and coastal.
- **Event** conditions come and go with events and crises: lawless, epidemic, striking, famine and booming.

Provinces and conditions reach players through `/info north_arpal`. Discontent is in words, and condition numbers never appear:

```
## North Arpal
Mountain passes and transit corridors to the southern realm.
Discontent: restless

Conditions
Frontier: A border with a foreign power, watched from both sides.
Crossroads: Roads and rivers carry the kingdom's trade through here.
Rugged: Hills and uplands, hard to farm and harder to travel.
Coastal: A harbour province, open to the sea.
Striking: Picket lines stand at the gates, and nothing moves.

Stationed: 2nd Arpal Division · d12, Carden Gendarmerie · d8
Last election: Liberal Union 9, Country Party 6, Crown and Union 4
```

**Content** is not config: it lives in `content/` as templates, and the GM imports it with `/content import` (section 6). Content templates are issues, bills, projects, events and crises.

**Starting content** in `content/`: the eight opening issues in `content/issues/`, their nine bills in `content/bills/`, six projects in `content/projects/`, and the opening event in `content/events/opening_1850.yaml`. The GM imports them before the game opens and marks them Ready. The opening event is scheduled for Spring 1850, so marking it Ready queues it, and `/gm start` fires it when the founding pre-election opens. It opens the issues and makes their bills available.

**issues/\*.yaml:** the import and export format for issue templates, one file per issue. An issue is a broad question that parties take stances on. It has no law of its own: the laws are its bills.

```yaml
id: labour
title: The Condition of Labour
for: Protect the workers and their children.
against: Leave industry free to compete.
opinion: 0.2
salience: 3
```

`/info labour`. Opinion and salience never appear:

```
## The Condition of Labour
For: Protect the workers and their children.
Against: Leave industry free to compete.
Status: Open

Bills
The Ten Hours Bill · For · before the Assembly, vote due Summer 1851

Liberal Union: Strongly For
Country Party: Against
Crown and Union: no stance
```

**bills/\*.yaml:** the import and export format for bill templates, one file per bill. A bill is one law on an issue, leaning *For* or *Against* it, so one issue can carry rival bills: a reform and its repeal, or two ways of settling the same question.

```yaml
id: ten_hours_bill
title: The Ten Hours Bill
issue: labour
lean: For
motion: Limit factory work to ten hours a day, ban the employment of children under twelve, and send Crown inspectors into every mill and pit.
pass:
  text: Crown inspectors walk into the mills and pits, the children walk out, and the owners swear the kingdom is ruined.
  effects: { economy: -5, purse.CommerceLabor: -2,
             discontent.iracterie: -3, discontent.aurenten: -2, discontent.north_arpal: -1 }
```

- A bill enters play through a `bill.{id}` effect, and can then be proposed with `/bill propose`.
- **Passing** applies its pass text and effects. The issue stays open unless the effects include `close.{issue}`.
- **Rejection** rests the bill, not the issue: other bills on the same issue can still be proposed.
- Only one bill per issue can be before the Assembly at a time.

When the bill passes, in #news:

```
## The Ten Hours Bill becomes law
Passed by 104 seats to 61, proposed by @player (Lord Halvard).
Crown inspectors walk into the mills and pits, the children walk out, and the owners swear the
kingdom is ruined.

<:stat_lower:1565442180057927860> Economy −5
<:stat_lower:1565442180057927860> Commerce and Labor purse −2
<:stat_lower:1565442180057927860> Discontent in Iracterie
<:stat_lower:1565442180057927860> Discontent in Aurenten
<:stat_lower:1565442180057927860> Discontent in North Arpal
```

**events/\*.yaml:** the import and export format for event templates, one file per event.

```yaml
id: carden_strike
title: Strike at the Carden Docks
text: The dockers walk out over wages, and the harbour falls silent.
effects: { discontent.north_arpal: 1, salience.labour: 1,
           condition.north_arpal.striking: 1, crisis.dock_strike_carden: true }
schedule: { season: Autumn, year: 1851 }
```

When it fires, in #news:

```
## Strike at the Carden Docks
Autumn 1851
The dockers walk out over wages, and the harbour falls silent.

<:stat_raise:1565442139855392808> Discontent in North Arpal
<:stat_raise:1565442139855392808> Salience: The Condition of Labour
North Arpal is now *Striking*
Crisis: The Carden Dock Strike
```

`schedule` is optional, and lets a storyline be planned in files instead of queued by hand:

- `{ season, year }` fires the event at the start of that turn. Before the game starts, Spring 1850 means it fires at `/gm start`.
- `{ delay: n }` fires it `n` turns after it is queued, counted exactly as `/gm event queue` counts.
- **Queuing:** marking the template Ready queues it as `/gm event queue` would. Marking it Ready fails if the scheduled turn has already started.
- **Changes:** changing the schedule of a Ready template moves its queued event, and clearing it cancels it. Once the event has fired, the schedule does nothing more.
- An event without a schedule is queued by hand, as before.

**crises/\*.yaml:** the import and export format for crisis templates, one file per template. The province and the issue are required, and so are at least two answers. Each answer leans *For* or *Against* the crisis's issue, or is *Neutral*; several answers may share a lean.

```yaml
id: dock_strike_carden
title: The Carden Dock Strike
ministry: CommerceLabor
province: north_arpal
issue: labour
answers:
  - lean: For
    label: Meet the strikers' demands
    price: 3
    text: The docks reopen within the week, and the owners are furious.
    effects: { discontent.north_arpal: -2, economy: -1, condition.north_arpal.striking: -1 }
  - lean: Neutral
    label: Commission a royal inquiry
    price: 4
    text: Learned gentlemen take evidence, and the dockers wait.
    effects: { discontent.north_arpal: -1 }
  - lean: Against
    label: Declare the strike unlawful
    text: The strike collapses, and so does trust in Carden.
    effects: { discontent.north_arpal: 2, condition.north_arpal.striking: -1 }
complication:
  text: The money runs out mid-negotiation, and the strike spreads.
  effects: { discontent.north_arpal: 1, discontent.iracterie: 1 }
default:
  text: The strike drags on unanswered, and ships divert to Mugi.
  effects: { economy: -2, discontent.north_arpal: 2 }
```

- Answers are numbered by their order in the list, from 1. The minister picks one by its label, and commands refer to it by number.
- `price` defaults to 0, and `effects` to none.
- `complication` is optional. Without one, the **standard complication** applies: "The money runs out, and the province pays for it.", with discontent +1 in the crisis's province.
- `default` is required: it is what happens when no one answers.

When the event posts it, in #news (`/info` shows the same):

```
## Crisis: The Carden Dock Strike
Ministry of Commerce and Labor · North Arpal · The Condition of Labour
Due at the next resolution, Winter 1851 <t:…:R>

1. Meet the strikers' demands · For · 3 cr
2. Commission a royal inquiry · Neutral · 4 cr
3. Declare the strike unlawful · Against · free
```

A condemnation, in #news at once:

```
@rival (Baroness Edel) condemns the handling of The Carden Dock Strike, and stands For
The Condition of Labour.
```

After the resolution, a clean outcome:

```
## The Carden Dock Strike
@player (Lord Halvard), Minister of Commerce and Labor, chose: Meet the strikers' demands
> The Crown will not see Carden starve for the sake of a shipowner's ledger.

The docks reopen within the week, and the owners are furious.

<:stat_lower:1565442180057927860> Discontent in North Arpal
<:stat_lower:1565442180057927860> Economy −1
North Arpal is no longer *Striking*

Condemnation: @rival (Baroness Edel) stood with the minister, and gains nothing.
```

A complicated one, when the purse could not pay. The answer's effects come first, then the complication's:

```
## The Carden Dock Strike
@player (Lord Halvard), Minister of Commerce and Labor, chose: Commission a royal inquiry.
The ministry could not pay, and the plan went wrong.

Learned gentlemen take evidence, and the dockers wait.
The money runs out mid-negotiation, and the strike spreads.

<:stat_lower:1565442180057927860> Discontent in North Arpal
<:stat_raise:1565442139855392808> Discontent in North Arpal
<:stat_raise:1565442139855392808> Discontent in Iracterie

Condemnation: @rival (Baroness Edel) was right where the minister was not: popularity +2.
```

With no answer by the deadline:

```
## The Carden Dock Strike
No answer came from the Ministry of Commerce and Labor. @player (Lord Halvard) loses popularity: −4.
The strike drags on unanswered, and ships divert to Mugi.

<:stat_lower:1565442180057927860> Economy −2
<:stat_raise:1565442139855392808> Discontent in North Arpal
```

**projects/\*.yaml:** the import and export format for project templates, one file per project. A project is public works paid season by season from one purse, with effects when it is finished and an optional bonus for finishing on time.

```yaml
id: royal_railway
title: The Royal Railway
purse: CommerceLabor
province: provencie_gares
price: 9
term: 6
effects: { economy: 5, discontent.here: -2, discontent.soralenten: -1 }
bonus:
  by: { season: Spring, year: 1853 }
  text: The first train reaches the coast ahead of the guarantee, and the shareholders cheer the Crown.
  effects: { economy: 2, condition.iracterie.booming: 1 }
```

| Field | Meaning |
| --- | --- |
| `purse` | The ministry that pays and runs it: War, Interior, CommerceLabor or Treasury. Defaults to CommerceLabor. |
| `province` | Where it is built. `here` in its effects stands for it. A ministerial project leaves it out, and the minister chooses. |
| `price` | The total cost. |
| `term` | The seasons it should take. The **pace** is `price / term`, the most a season can usefully absorb. |
| `effects` | Applied once, when it finishes, and permanent. |
| `bonus` | Optional. Its text and effects apply as well if it finishes in time: `by: { season, year }` means by the end of that turn, and `within: n` means within `n` seasons of starting. |
| `ministerial` | Optional, default false. If true, the purse's minister can start it with `/ministry project`, choosing the province. |
| `requires` | Optional. A condition the province must have, for example `coastal`. |

- **Starting:** a `project.{id}` effect starts it, from a bill, an event, a crisis answer or another project; or, if it is ministerial, its minister does.
- **Spending:** the purse's minister sets its spending per season, which starts at the pace. Spending above the pace is partly lost to **graft**, the contractors and speculators who profit from a rushed job (section 5). Spending less slows it down, and 0 pauses it.

The bonus makes graft a real choice: rushing costs more, but can beat the deadline. The Tovrerie fen drainage (price 4, term 4, so a pace of 1 a season) starts at the earliest in Winter 1850, and its bonus is due by Summer 1851, three seasons later. At the pace it would be one season late. Spending 2 a season gets it done in time, for 5 crowns in all instead of 4.

When it starts, in #news:

```
## Project begun: The Royal Railway
Provencie Gares · Ministry of Commerce and Labor · 9 cr over 6 seasons
Bonus if finished by the end of Spring 1853
```

`/ministry fund royal_railway 3`, the minister's private reply:

```
The Royal Railway: 3 cr a season from Commerce and Labor, 1.5 cr above the pace.
Graft takes half of that: 2.25 cr of work a season, finishing in Summer 1852.
That beats the bonus deadline, Spring 1853. The purse holds 7.2 cr.
```

When it finishes in time, in #news:

```
## The Royal Railway is finished
Provencie Gares · @player (Lord Halvard), Minister of Commerce and Labor: popularity +2.
The first train reaches the coast ahead of the guarantee, and the shareholders cheer the Crown.

<:stat_raise:1565442139855392808> Economy +5
<:stat_lower:1565442180057927860> Discontent in Provencie Gares
<:stat_lower:1565442180057927860> Discontent in Soralenten
<:stat_raise:1565442139855392808> Economy +2
Iracterie is now *Booming*
```

## 5. Formulas

All constants named here come from `constants.yaml`. Every value is clamped to its range after each change.

Opinion has three layers. The stored `opinion_i` is the **base opinion**, the gross mood before any province is considered. Only effects and crisis outcomes move it; it never drifts. Each province turns it into a **provincial opinion** through its conditions, and the **national opinion**, the net mood, combines the provinces. Players never meet the base opinion directly: polls, campaigns, attacks, crises and elections all use a provincial or the national opinion.

**Provincial opinion**

`o(i, p) = clamp(opinion_i + sum of condition biases of province p for issue i, −1, 1)`

**National opinion**

`O(i) = Σ_p population_p × o(i, p) / Σ_p population_p`, the population-weighted mean of provincial opinion. Campaigns and attacks are national and use it, so conditions still count in proportion to the people they touch.

**Stance score**

For stance s and opinion o:

- s = ±1: `g = s × o`
- s = ±2: `g = 2 × sign(s) × o − strongStancePenalty`
- s = 0: `g = 0`

**Salience factor:** `k(i) = salience_i / 3`

**Simultaneous actions:** every campaign, attack and poll in a turn is computed against the state at the start of the resolution (popularity, opinion, salience and logged stances). All popularity changes then apply together, and salience rises afterwards. Submission order never matters.

**Campaign**

1. `Δpopularity = campaign.factor × g(s, O(i)) × k(i) / √m`, where m is the number of factions campaigning on i this resolution with a stance of the same sign, this one included. A crowded line pays less, so the most popular stance on the most salient issue is not the only sensible move. If `campaign.crowding` is off, m is 1.
2. Then salience of i rises by 1, at most once per issue per season.

The stance s defaults to the party's programme stance on the issue. A faction may give a different stance; it is then logged publicly as **off-programme**. Where the programme takes no stance on the issue (and always for an independent), the faction must give a stance, and doing so is not off-programme. Every campaign writes the stance it used to the StanceLog.

**Attack** on target faction t, issue i

1. Take t's stance: its latest StanceLog entry on i, or else its party's programme stance. An attack on a faction with no stance, or a Silent one, on i is rejected.
2. If `g(s_t, O(i)) < 0`, then t loses `attack.factor × |g| × k(i)`.
3. Otherwise, the attacker loses `attack.backfire`.

A faction cannot attack a member of its own party.

**Poll:** the faction's private channel receives the mood on the issue, in words: nationally `O(i)`, or in one province `o(i, p)` when a province is given, with uniform noise of ±`poll.opinionNoise`, and salience with noise of ±`poll.salienceNoise`, each turned into words (section 9). The numbers are never shown.

**Programme changes:** recording a new programme outside a pre-election costs every member of the party `programme.flipPenalty` popularity for each issue whose stance changes sign (For to Against, or the reverse, at any strength). Adding a stance, dropping one, or changing its strength is free. During a pre-election, programmes are manifestos and change freely.

**Government and opposition**

- **Government:** every cabinet member, plus every member of a party that holds at least one cabinet seat (the Chancellorship counts).
- **Opposition:** every active member of a party that holds no cabinet seat. An independent outside the cabinet is neither, and cannot use opposition commands.

**Opposition actions:** each opposition faction has `opposition.actionsPerSeason` opposition action a season, spent on a question, a condemnation or a private bill. At most `privateBillsPerYear` of a faction's opposition actions in a year can be bills.

**Cabinet lifecycle**

1. The GM appoints a Chancellor.
2. The Chancellor nominates a cabinet, which opens an approval vote. A faction may hold only one ministry, and the Chancellor may not hold a ministry.
3. If the vote passes, the nominated cabinet becomes the sitting cabinet. If it fails, the sitting cabinet stays, the GM is alerted, and the Chancellor nominates again.
4. A sitting cabinet stays in office as **caretaker** until a new one is approved. This covers the time after an election and after a passed no-confidence motion. Caretaker ministers act with full powers.
5. Before the first cabinet is approved, every ministry is vacant. The GM can use `/gm cabinet impose` to fill the gap.
6. **Cabinet role:** the Chancellor and every minister of the sitting cabinet, caretaker or not, hold the Cabinet role; nobody else does. Nominees, and a newly appointed Chancellor still forming a cabinet, get it only once that cabinet is approved or imposed; the exception is an appointment to a vacant Chancellorship, which fills the sitting cabinet's seat and grants the role at once. The bot adds and removes it with every change of office: an approval, an imposed cabinet, an appointment, a resignation, a retirement and a player rejoining the server.
7. **Resignation:** a minister or the Chancellor may resign at any time with `/ministry resign`. A minister's ministry is vacant at once; the Chancellor fills it with a new nomination, or the GM with `/gm cabinet impose`. A Chancellor's resignation leaves the ministers in office and the Chancellorship vacant until the GM appoints a new one. A pending nomination that includes the resigning faction is cancelled. The GM is alerted.

**Vacant ministries**

- A vacant ministry's crises and questions go to the **Chancellor**, who answers them as its minister would, paying from that ministry's purse and gaining or losing popularity in its place.
- If the Chancellorship is vacant too, a due crisis applies its default and no faction loses popularity, and a question cannot be asked; an open one is dropped, and nobody gains or loses over it (the season its opposition action was spent has passed). The GM is alerted a resolution in advance.

**Crisis resolution**, where the minister chose one of the crisis's answers, and L is that answer's lean: +1 for *For*, −1 for *Against* and 0 for *Neutral*

1. If the ministry's purse covers the answer's price, deduct it and apply the answer's effects. The outcome is **clean**.
2. Otherwise, the purse is emptied, and the answer's effects and the complication (the crisis's own, or the standard one) both apply. The outcome is **complicated**.
3. The minister's faction gets `Δpopularity = campaign.factor × L × o(i, p) × k(i)`. A Neutral answer neither gains nor loses.
4. Opinion on i moves `+crisisShift × L` if clean, or `−crisisShift × L` if complicated. Salience of i rises by 1, whatever the lean.
5. **No response by the deadline:** the default applies, and the minister's faction loses `campaign.factor × k(i)`.
6. **Condemnations:** a condemnation backs *For* or *Against* on the crisis's issue. The right side is the sign of `o(i, p)`. A condemning faction on the right side gains `condemnReward` if the minister was not on it (a Neutral answer or no response counts as not on it). One on the wrong side loses `condemnPenalty` if the minister was on the right side. When `o(i, p)` is exactly 0, nobody gains or loses.

The responsible minister is whoever holds the ministry at the deadline, or the Chancellor if it is vacant.

**Questions:** the responsible minister at the deadline, as for crises, must answer it. If they don't, the minister's faction loses `unansweredQuestion` and the asker gains `questionReward`. If the ministry changed hands since the question was asked, the deadline moves one resolution later.

**The agenda**

- The **Chancellor** alone proposes government bills, at most `government.billsPerSeason` a turn, caretaker or not. Ministers cannot. While the Chancellorship is vacant, no government bill can be proposed.
- Opposition factions propose **private bills** as their opposition action.
- A bill proposed during a turn is **scheduled**: it is announced in #assembly at once as the next turn's business. Its vote opens at the resolution that ends the turn, with its post and buttons in #assembly, and is counted at the resolution after that.
- Until its vote opens, the proposer can withdraw a scheduled bill from `/me`, which gives back the Chancellor's bill for the turn or the opposition action.
- A scheduled bill counts as before the Assembly, for the rule of one bill per issue.

**Votes**

- Votes are weighted by each faction's seats at the time of the count. Only active factions with seats vote.
- Bills and cabinet approval pass with a majority of Yes and No votes. Abstentions don't count, and a tie fails.
- No confidence passes when Yes votes hold more than half of all filled seats.
- Votes are public as they are cast.
- A Yes vote on a bill logs the bill's lean as the voter's stance on its issue, and a No vote logs the opposite: Yes on a *For* bill is For, Yes on an *Against* bill is Against.

**Laws**, where L is the bill's lean, +1 for *For* and −1 for *Against*

- **Passed:** the sponsor faction gets `Δpopularity = law.sponsorFactor × L × O(i) × k(i)`. The bill's pass text and effects apply, and the bill is Passed. The issue stays open unless the effects close it.
- **Rejected:** salience of the issue falls by `rejectDrop`, and the bill goes into Cooldown for `rejectedCooldownSeasons`, after which it is Available again. Other bills on the issue are unaffected.
- An issue can have only one bill before the Assembly at a time.
- **Closing an issue** withdraws all its bills, and cancels a reading in progress.
- Reforms are ordinary bills whose pass effects change the rules, for example `seats: 30`, or `taxRate: 0.05`.

**No confidence:** any active faction outside the cabinet can table a motion or co-sign one, members of governing parties included, so a party can bring down its own Chancellor. Neither costs an action. A motion goes to a vote once its tabler and co-signers together hold the tabling threshold, `noConfidenceShare` of the Assembly's seats rounded to the nearest seat (30 of 180). If they don't reach it within `noConfidenceGatherResolutions` resolutions, the motion lapses; a lapsed motion starts no cooldown. After a failed motion, no new one can be tabled for `noConfidenceCooldownSeasons`. A passed motion turns the sitting cabinet into a caretaker and calls a **snap election** (section 2): the season the resolution opens becomes the pre-election.

**Election boundary:** no bill can be voted on and no no-confidence motion tabled during the pre-election, so bills can't be proposed during it or in the turn before a scheduled one. A bill scheduled for a turn that becomes a snap pre-election is cancelled when that turn opens. Any motion or cabinet nomination still open when the election runs is cancelled. A cancelled bill returns to Available with no cooldown, and a cancelled private bill no longer counts towards its proposer's `privateBillsPerYear`.

**Every season**

- Any issue whose salience was not raised this season loses 1 salience.
- Discontent in each province moves `discontent.drift` toward its resting value: `discontent.rest` plus the `discontentRest` of each of the province's conditions.
- Favour moves `favour.drift` toward `favourBaseline`.
- Popularity of every active faction fades toward the start: `popularity += popularity.drift × (popularity.start − popularity)`. At 0.05, a faction at 50 loses 2 a season and one at 0 gains 0.5, so a lead must be kept up by campaigning and a fallen faction can always climb back. Campaign days don't drift.

**Fiscal year**

1. **Start of Winter:** `economy ×= 1 + growth`, where

   `growth = economy.growth + Σ condition growth − growthDrag × Σ_p share_p × max(0, discontent_p − discontent.rest)`

   The middle term is the `growth` of every condition on every province. The last is the **unrest drag**: `share_p` is province p's share of the population, so unrest costs in proportion to the people it touches. At 0.005, the whole nation at discontent 7 costs 2% a year, against the base 1% growth. Then `Budget = economy × taxRate`; this is the **available** Budget.
2. **During Winter:** the Treasury allocates the available Budget across five purses: War, Interior, CommerceLabor, Treasury and Navy. Anything unallocated goes to the reserve.
3. **Start of Spring:**
   - leftover balances from last year's purses move to the reserve;
   - the reserve grows by `reserve.interest`;
   - the new allocation applies. If none was submitted, last year's percentages repeat; the first year uses `defaultAllocation`;
   - yearly upkeep is paid: divisions from the War purse, gendarmerie from the Interior purse. A purse that cannot cover all of its upkeep is emptied, every one of its units loses one training step (`sidesStep` sides, never below `baseSides`), and the GM is alerted.
4. **The Navy purse** is a contribution, not spending money. It is paid out at once. navyShare is the Navy allocation divided by the available Budget, and Anzaria judges the change: `favour += (navyShare − lastNavyShare) × navyFactor`, then `lastNavyShare = navyShare`. A raise pleases the nobles, a cut insults them, and an unchanged contribution leaves Favour to drift.
5. **Game start:** the reserve starts at `reserve.start`, `lastNavyShare` starts at the Navy share of `defaultAllocation`, and steps 1, 3 and 4 run once with `defaultAllocation`.

**Reserve:** the Treasury can grant any amount from the reserve to any purse except Navy. There are no loans.

**Ledger:** every change to a purse or the reserve is written as a LedgerEntry, so the money can always be traced: allocations, grants, carry-overs and interest, upkeep, purchases and crackdowns, crisis answers, project spending (with the share lost to graft), and changes from laws, events, crisis effects and `/gm apply`. An entry becomes public at the resolution after it is made, when the orders and responses behind it are revealed anyway; the ministry's own minister and the Chancellor see it at once. An entry from a private `/gm apply` is shown as a *royal adjustment*, without the GM's reason.

**Forces**

Divisions and gendarmerie are the same kind of unit with different constants. A unit is one die.

- **Raise** creates a new unit at `baseSides`. **Disband** is immediate and refunds nothing.
- **Train:** sides rise by `sidesStep`, up to `maxSides`. The cost is the `trainCosts` entry for the unit's current step, so retraining a unit that lost a step costs that step again.
- **Station** moves a unit to another province, for free.
- **Battle** (a GM command): every die on each side is rolled, ad-hoc dice add their bonus, and the side totals are compared. The higher total wins, and a tie goes to the defender. The loser loses its lowest-rolling unit, or its two lowest if the winner's total is at least double. The bot proposes the losses, and the GM confirms them. Losses only apply to real units, never to ad-hoc dice.
- **Riot** (a GM command): the gendarmerie in a province roll their total against one die with `max(2, round(2 × discontent))` sides. A province with no gendarmerie rolls 0. The bot reports the result, and the GM decides the consequences.

**Crackdown** (Interior), a gamble on order in one province:

- It needs at least one gendarmerie corps in the province, costs `crackdown.cost` from the Interior purse when ordered, and is rolled at the next resolution. At most one per province per season.
- The gendarmerie roll as in a riot, against the same discontent die.
- **Success** (the gendarmerie total is at least the die): discontent falls by `crackdown.reduction`, and the `lawless` condition is removed if present.
- **Failure:** discontent rises by `crackdown.backlash`, the Interior minister's faction loses `crackdown.failurePopularity`, and the GM is alerted to write the follow-up.
- The result is public, in the resolution's news.

**Projects**

- **Pace:** `pace = price / term`.
- **Each resolution,** an Active or Stalled project pays its `spending` from its purse, or as much of it as the purse holds, and never more than it needs to finish. A payment `x` adds `min(x, pace) + (1 − project.graft) × max(0, x − pace)` to its progress: money above the pace is partly lost to graft.
- **Stalled:** a season in which it pays nothing, because the purse is empty or spending is 0. Its minister is told privately.
- **Finishing:** once progress reaches the price, its effects apply and the faction holding its ministry (the Chancellor while it is vacant) gains `project.popularity`. If it finishes by its bonus deadline, the bonus text and effects apply too. A `by` deadline counts the resolution that ends that turn, and `within: n` counts the n resolutions after it started.
- A project started by an effect begins at the next resolution, so a bill passing at the end of Autumn first pays at the end of Winter.
- **Vacant ministry:** the Chancellor sets the spending. With no Chancellor either, it carries on at its last spending.

## 6. Commands

All commands are slash commands. Long text, such as a roleplay response, is entered in a modal. The bot checks permissions and timing, and replies privately with an error if a command is not allowed.

**Command conventions**

- **Confirmations restate the outcome.** Every player command replies privately with exactly what will happen, when (the next resolution as `<t:…:R>`), and any public consequence: an off-programme stance, a stance logged by a vote, a pending action replaced, a law that would cost its sponsor popularity because the public is against it.
- **Confirm buttons** guard anything announced publicly or hard to reverse: an off-programme campaign, `/party leave`, `/ministry resign`, `/war disband`, `/interior disband`, and the GM's `/gm faction retire`, `/gm party delete`, `/gm set`, `/gm undo` and `/content remove`.
- **Errors say when.** A command refused for timing names when it becomes possible, for example "Bills can be proposed again after the election, from Summer 1854 `<t:…:R>`", and a command refused for role names the role it needs.
- **Autocomplete offers only valid choices.** `/attack` lists rivals outside the player's party, then only the issues where that rival has a stance, with the stance shown. `/bill propose` lists only Available bills whose issue has no bill before the Assembly (scheduled or open), with each bill's issue and lean. `/opposition question` hides vacant ministries only when the Chancellorship is vacant too. `/crisis respond` lists only the crises the player is responsible for. `/war` and `/interior` show each unit's die and the cost of its next training, for example "1st Guards · d14 · train 3 cr".
- **Buttons on the bot's own posts** replace commands for the common case: votes, no-confidence co-signing and join answers. Votes also have `/vote`; every other button is repeated in `/me`, so a lost post never blocks anyone.

**Status and help**

| Command | Who | Effect |
| --- | --- | --- |
| `/me` | Any faction | Privately shows the faction's turn at a glance: the next resolution, pending actions (each with a Cancel button), whether the season's opposition action is used and whether a bill is still allowed this year, a bill it has scheduled (with a Withdraw button), open votes not yet cast, open crises and questions for a minister, and whether the faction is government, opposition or independent. Anything waiting on the faction comes with its buttons: votes, co-signing, join answers and questions to answer. With nothing pending, it says what the faction could do next, given its role and the turn. |
| `/help topic` | Anyone | Explains a topic: campaigning, opposition, voting, each ministry, elections. |

**Information**

| Command | Who | Effect |
| --- | --- | --- |
| `/info thing` | Anyone | Shows any game object. One autocomplete lists factions, parties, issues, bills, projects, crises, provinces, the four ministries and the budget, each marked with its kind. What each shows is listed below. |
| `/map` | Anyone | Shows the province map from the latest election. |

`/info` shows, by kind:

- **Faction:** popularity, party, seats, home province and logged stances.
- **Party:** colour, members with popularity and seats, programme, and whether it is in government.
- **Issue:** *For* and *Against* texts, status (Open, Closed), its bills with their leans and status (Available, before the Assembly with the vote's due date, Cooldown with its end, Passed), and every party's programme stance on it.
- **Bill:** issue, lean, motion and status, and its readings with their results.
- **Project:** province, purse, progress against its price, spending per season, expected finish and bonus deadline.
- **Crisis:** ministry, province, issue, answer labels with their leans and prices, and deadline.
- **Province:** description, discontent in words, conditions (name and description), the units stationed there and the last election result.
- **Ministry:** its holder, its purse (this year's allocation, money in and out, and balance), its spending this year line by line from the ledger, its units or projects, and its open crises and questions.
- **Budget:** the year's available Budget, every purse with its allocation, spending and balance, the Navy contribution, the reserve with grants and interest, and the year's spending by kind across the government (upkeep, purchases, crises, projects, graft, laws and events). A `year` option shows an earlier year.

With nothing chosen, `/info` lists what is live: open issues, available bills, active projects and live crises.

`/info commerce_labor` in Winter 1851:

```
## Ministry of Commerce and Labor
Minister: @player (Lord Halvard)
Purse 1851: allocated 5.0 cr · granted 5.0 · spent 9.5 · balance 0.5 cr

Spring 1851   +5.0   Allocation
Spring 1851   −1.5   Project: The Royal Railway
Summer 1851   −1.5   Project: The Royal Railway
Summer 1851   −2.0   The Ten Hours Bill (law)
Autumn 1851   +5.0   Grant from the reserve
Autumn 1851   −3.0   Crisis: The Carden Dock Strike, meet the strikers' demands
Autumn 1851   −1.5   Project: The Royal Railway

Projects: The Royal Railway, 6.0 of 9 cr, 1.5 cr a season at the pace, finishing Spring 1852
```

This season's spending appears after the resolution; until then only the minister and the Chancellor see it, in `/ministry status`.

**Factions**

Players don't create factions themselves. The GM registers each one with `/gm faction create` (below), after agreeing the character and home province with the player. Any game command from someone without a faction is refused privately with "You are not registered yet; ask the GM", and the GM is alerted once per player.

**Parties**

A party has **two or more** factions. Only the GM founds parties (`/gm party create`, below), at the players' request. Each party has its own constitution, written by its members as roleplay. The bot doesn't model it: every member has the same commands, and the only party vote it holds is on a join application, decided by a **majority** of the party's active members.

| Command | Who | When | Effect |
| --- | --- | --- | --- |
| `/party apply party` | An independent faction | Pre-election only | Applies to join. The application is posted in the party chat with Accept and Decline buttons, and each member answers once (an answer can be changed). The faction joins as soon as a majority of the members accept, and the application is declined as soon as a majority decline. Applications still pending when the election runs are dropped. |
| `/party leave` | A member | Any time | The faction becomes independent and keeps its seats. |

Joining or leaving a party never changes seats. Seats only change at an election.

**Dissolution:** a party left with fewer than two active members, through leaving, retiring or a GM move, dissolves at once, as `/gm party delete` does. Its last member becomes independent and keeps its seats, and the dissolution is announced in #news.

**Party role and chat**

Every party has a Discord role and a private text channel, both created and kept in sync by the bot.

- **Role:** named after the party and coloured with the party colour. It is shown separately in the member list, so players are grouped by party. Members hold it; nobody else does. Discord shows a member's highest coloured role, so other coloured roles should sit below the party roles.
- **Chat:** a text channel named after the party, under the party category. Only the party role, the GM role and the bot can see it. Every member can pin messages there.
- **Membership:** the bot adds or removes the role whenever a faction joins, leaves, is moved by the GM, is retired, or rejoins the server. The role is the only thing that grants access to the chat, so access follows membership automatically.
- **Edits:** renaming or recolouring a party renames the role and the channel, and recolours the role.
- **Deletion:** the role is deleted, and the chat is moved to the archive category, read-only and visible only to the GM role, so its history is kept.
- **Sync:** on startup and from the *Repair* button on the `/gm setup` checklist, the bot checks every party's role, role members and channel, and the Cabinet role's members, against the database, and repairs any differences. If Discord refuses a change, for example because the bot's role is too low, the GM is alerted.

**Party programmes**

A party writes its programme as an ordinary Discord message, usually in its chat, and sends the GM its link (**Copy Message Link** in Discord). The GM records it with `/gm party programme party message`. Who in the party may agree a programme is for its constitution, not the bot. The bot can read any message in a channel it can see, as long as it has the Message Content intent.

The message looks like this:

```
## Liberal Union
The Condition of Labour: *Strongly For*
Free Trade: *Against*
The Franchise: *For*
```

- **Header:** lines starting with `#` are ignored, since the party comes from the command. So are blank lines and lines with no letters or digits (rules, emoji only).
- **Lines:** a stance line names an issue and an opinion, in either order: `Issue: Opinion` or `Opinion: Issue`. The parser is forgiving:
  - **Formatting** is ignored: list bullets (`-`, `*`, `•`, `1.`, `1)`), blockquotes (`>`), subtext (`-#`), Markdown markers (`**`, `__`, `*`, `_`, `~~`, `` ` ``, `||`), backslash escapes, quotes, trailing punctuation, Discord custom emoji, mentions and timestamps, unicode emoji and extra spaces.
  - **Separators** between issue and opinion: `:`, ` - `, ` -- `, `–`, `—`, `=`, `->`, `-->`, `=>`, `→`, `|` or a tab. Further text after another separator is commentary (`Free Trade: For - it brings prosperity`).
  - **No separator** is needed when the opinion starts or ends the line and the rest names an issue (`Strongly support free trade`, `No stance on Poor Relief`, `Free Trade strongly for`); a leading *on*, *of*, *to*, *towards*, *regarding* or *about* before the issue is dropped.
  - **The issue** is matched against the title or id of an open issue, ignoring capitalisation, punctuation, a leading *The*, and underscores against spaces. Failing that, whole words contained in exactly one open issue's title or id match it (`Labour` is *The Condition of Labour*), from 4 letters up.
  - **The opinion** is one of *Strongly For*, *For*, *Against*, *Strongly Against* or *Silent*, or an alias from `config/programme.yaml` (YAML Reference), ignoring capitalisation and punctuation. The defaults include *support*, *in favour of*, *pro*, *yes*, *aye*, *+*, *+1* for For; *oppose*, *opposed to*, *anti*, *no*, *nay*, *reject*, *-*, *-1* for Against; *++*, *+2* and *--*, *-2* for the strong stances; and a strength word (*strongly*, *very*, *firmly*, *fiercely*, *staunchly*, *wholly*, *fully*, ...) before or after For or Against makes it strong. Filler such as *we are*, *the party stands* and *it* around the opinion is allowed (`Free Trade: we are firmly against it`).
  - **Silent** (also *non-aligned*, *neutral*, *no stance*, *abstain*, *undecided*, *none*, *n/a*, *0*, ...) records no stance, exactly as leaving the issue out, but still counts as giving the issue for the duplicate check.
- **Missing issues have no stance.** Any open issue the message doesn't mention is recorded as Silent (no stance), so the message is always the party's complete programme and replaces the previous one.
- **Prose is ignored:** a line with no opinion that doesn't name an issue on its own (`Our principles are as follows:`) is skipped, and listed to the GM in the preview as an ignored line, so nothing disappears unseen.
- **Errors:** an opinion with an unknown, closed or ambiguous issue (an error names the candidates) or no issue at all, a line that names an issue with no or an unrecognised opinion, or an issue given twice fails the whole message. The bot lists every bad line to the GM privately, and nothing is recorded, so the party can fix the message and the GM can record it again.
- **Preview:** the GM privately sees every open issue with its parsed stance, marking the ones with no stance and the ones that flip sign, with the popularity cost of the flips (section 5) for every member, the ignored lines, and Record and Cancel buttons.
- **On record:** the party's ProgrammeStance rows are replaced, the change is logged, and the bot replies to the original message with the recorded programme, so the party sees exactly what was understood.
  - **During a pre-election,** the programme is the party's **manifesto**: it is posted to #news as such, and changes freely.
  - **Outside a pre-election,** it is posted to #campaign-log. Every flipped stance is announced in #news ("The Liberal Union changes its line on Free Trade"), and the flip penalty applies.
- **Timing:** a programme takes effect as soon as it is recorded, including for the election's programme score. Issues opened later have no stance until a new programme is recorded.

**Campaign actions** (two per faction per turn, and two per day during the pre-election, each of a different kind)

| Command | Effect |
| --- | --- |
| `/campaign issue [stance]` | Campaigns on an issue nationally. The stance defaults to the programme stance; giving a different one is off-programme, and needs a Confirm. |
| `/attack faction issue` | Attacks a rival's stance nationally. |
| `/poll issue [province]` | Polls an issue, nationally or in one province. The answer comes back in words. |

A faction may have at most two pending actions, never two of the same kind: a campaign and a poll is allowed, two campaigns are not. Submitting an action of a kind that is already pending replaces it, and the reply names what was replaced. Pending actions are cancelled from `/me`. Both actions resolve simultaneously with everyone else's (section 5), so a poll never sees the effect of the same turn's campaign.

**Opposition** (opposition factions only; the private bill, the third opposition action, is proposed with `/bill propose` below)

| Command | Limit | Effect |
| --- | --- | --- |
| `/opposition question ministry` | The season's opposition action | Opens a modal for the question. It is posted publicly with an Answer button, which opens a modal for the responsible minister; the answer is posted under it. The minister must answer by the second resolution after it is asked. |
| `/opposition condemn crisis side` | The season's opposition action | Publicly backs *For* or *Against* on a live crisis's issue, posted to #news at once, so the minister sees it before answering. Scored at the resolution (section 5). One per faction per crisis. |

**The Assembly**

| Command | Who | Effect |
| --- | --- | --- |
| `/bill propose bill` | The Chancellor (government bill, at most `government.billsPerSeason` a turn), or an opposition faction (private bill, spending the season's opposition action, at most `privateBillsPerYear` a year) | Schedules an Available bill for a vote next turn, as a new reading (section 5). The kind follows the proposer's role. It is announced in #assembly at once, and when the next turn opens, the reading is posted there with the bill's motion, its issue and lean, Yes, No and Abstain buttons and a running tally. |
| `/vote subject yes/no/abstain` | Any active faction with seats | The buttons' fallback. Casts or changes a vote until the deadline. Votes are weighted by seats. The reply notes the stance the vote logs, and warns when it contradicts the voter's programme. |
| `/noconfidence` | Any active faction outside the cabinet; not during the pre-election, nor during the cooldown | Tables a motion. It is posted in #assembly with a Cosign button, open to the same factions, and a running count, for example "18 of 30 seats". It goes to a vote once its supporters hold the tabling threshold, and lapses if they don't within `noConfidenceGatherResolutions` (section 5). |

**Bills** are voted on during the turn after they are proposed: the vote opens when that turn starts and is counted when it ends. **Cabinet and no-confidence votes** resolve at the second resolution after they open. Either way, every vote has at least one full day.

**Ministers**

| Command | Who | Effect |
| --- | --- | --- |
| `/crisis respond crisis answer` | The responsible minister: the one whose ministry holds the crisis, or the Chancellor for a vacant ministry | Opens a modal for the response. The reply shows the answer's price against the purse balance, and warns when the purse can't cover it and the answer will be complicated, pointing to `/treasury grant`. The answer can be changed until the deadline, and the response stays private until the resolution. |
| `/treasury allocate` | Treasury | During Winter: opens a modal with the five purses (War, Interior, Commerce and Labor, Treasury, Navy), pre-filled with last year's crowns, with the available Budget in the labels. The bot then previews the remainder that goes to the reserve and Anzaria's reaction to the Navy change in words ("the nobles will be pleased"), with a Confirm button. It can be resubmitted until the Winter resolution. |
| `/treasury grant ministry amount` | Treasury | Moves money from the reserve to a purse. |
| `/war raise name province`, `/war train division`, `/war station division province`, `/war disband division` | War | Manages divisions. |
| `/interior raise name province`, `/interior train corps`, `/interior station corps province`, `/interior disband corps` | Interior | Manages gendarmerie, the same way. |
| `/interior crackdown province` | Interior | Orders a crackdown (section 5). |
| `/ministry project project province` | The minister of the project's purse | Starts a ministerial project in a province that meets its `requires`. It first pays at the next resolution. |
| `/ministry fund project amount` | The minister of the project's purse | Sets a project's spending per season, from 0 up. The reply shows the pace, how much graft would take, the expected finish and whether it beats the bonus deadline. |
| `/chancellor nominate treasury war interior commerce` | Chancellor | Nominates the cabinet and opens the approval vote. Each nominee is told in their private channel. |
| `/ministry resign` | A minister or the Chancellor | Resigns at once (section 5). Announced in #news. |
| `/ministry status` | A minister | Privately shows the ministry's purse, pending orders, units, its projects with their spending, and open crises and questions, with the ledger entries not yet public. The Chancellor can view any ministry's. |

Purchases (forces and crackdowns) deduct their cost from the purse immediately, and take effect at the next resolution as a PendingOrder. Projects are the exception, paid per season from the purse that runs them.

**GM**

The GM's commands are split in two groups: `/gm` runs the game, and `/content` (below) writes it. Both need Manage Server.

| Command | Effect |
| --- | --- |
| `/gm setup` | Posts the **setup checklist** in the channel where it is run (one message, moved there if it was elsewhere). It creates nothing: it lists every game channel, category and role with ✅, ⚠️ or ❌ (registered and correct, registered with a problem such as wrong permissions, or not set), the bot's permissions and role position, the intents, and whether every party has its role and chat and every faction its private channel. An unset item with a channel or role of the matching name is suggested, and a *Register the suggestions* button registers them as `/gm channel set` would. A *Repair* button re-syncs party roles and chats, faction channels and the Cabinet role. The message updates itself whenever a channel or role changes, after `/gm channel set`, and on startup. |
| `/gm cleanup` | Cleans the server after testing. Privately lists, with counts and names, every party chat (by stored id, or any channel in the Parties or Archive category), every faction chat (by stored id, or any channel in the Factions category) and every party role (by stored id, by a party's name, or by the name of a party chat being deleted), and deletes them one by one after a Confirm. It never deletes the categories, a registered game channel, the GM or Cabinet role, @everyone, a managed role or one at or above the bot's top role; matching roles it won't touch are listed. The stored ids of what it deleted are cleared, but parties, factions and the whitelist stay, so Repair, a restart or the next private message brings their roles and chats back while the game data still has them. The result is logged as a GM override and posted to #gm. |
| `/gm start` | Opens the game, once, after a Confirm that warns if no event is queued. In one transaction it loads the provinces and their conditions, sets the starting values from `constants.yaml`, runs the fiscal game start (section 5), and sets the turn to Spring 1850 as the founding pre-election (term 0, with the pre-election stored as Spring 1850). Then, as step 10 of a resolution would, it fires every event queued with no delay, in queue order. It posts the fired events and a proclamation opening the hustings to #news, updates the dashboard, renames the turn tracker, and schedules the pre-election as usual: two campaign days, then the full resolution that runs the election. Refused once the game has started. |
| `/gm agenda` | Privately shows what needs the GM before the next resolution: its time, the event queue in order (or a warning that it is empty), crises and questions with no response, vacant ministries, join applications, scheduled bills, and open votes with their tallies. |
| `/gm party create name colour founder1 founder2 [founder3] [founder4] [founder5] [emoji]` | The only way a party is founded. Creates it with the founders as members, along with its role and chat, and announces it in #news. Every founder must be independent. The colour is `#RRGGBB`, and is used for the role, the seat chart and the province map; the GM is warned when it is close to an existing party's. |
| `/gm party edit party [name] [colour] [emoji]` | Edits a party, and updates its role and chat. |
| `/gm party move faction [party]` | Moves a faction into a party, or with no party makes it independent, at any time. A GM override of the pre-election joining rule and of the members' vote. |
| `/gm party programme party message` | Records the linked message as the party's programme (see Party programmes). |
| `/gm party delete party` | Deletes a party. Its members become independent, its role is deleted and its chat is archived. |
| `/gm faction create player character home` | Registers a player as a faction: keyed on their Discord account, at starting popularity, independent, with a private channel. The home province is the character's safe borough (section 8); autocomplete shows each province's description. It holds no seats until the next election. Announced in #news as the character entering politics, and the player is greeted in their private channel. Refused if the player already has a faction. |
| `/gm faction edit faction [character] [home]` | Edits a faction. Players can't change their character or home province; only the GM can. |
| `/gm reward faction amount reason` | Rewards excellent roleplay (a propaganda poster, a manifesto, a speech) with popularity, applied at once. Posted to #news as a royal commendation naming the faction and the reason, so players see what earns favour. A negative amount is refused; use `/gm apply` for penalties. |
| `/gm faction retire faction` | Retires a faction: it leaves its party and any ministry, its pending actions are dropped, and its seats stay vacant until the next election. |
| `/gm channel set role target` | Sets a game channel, a category (faction, party or archive), the GM role or the Cabinet role. Game channels are added to the command whitelist automatically. |
| `/gm event queue template [delay]` | Queues an event from a Ready template, to fire at the start of the next turn (section 7), or `delay` turns later. Before the game starts, no delay means it fires at `/gm start`, and a delay counts turns from there. Issues and crises enter play this way. |
| `/gm event cancel event` | Removes a queued event before it fires. |
| `/gm crisis cancel crisis [message]` | Withdraws a live crisis, with an optional in-world message. Condemnations of it give back their opposition actions. |
| `/gm crisis edit crisis` | Edits a live crisis as YAML until its minister responds (the same as `/content edit id live`). Its ministry and issue stay fixed. |
| `/gm cabinet chancellor faction` | Appoints the Chancellor, announced as a royal proclamation. |
| `/gm cabinet impose` | Imposes a caretaker cabinet without a vote, announced as a royal proclamation. |
| `/gm proclaim` | Opens a modal for a free-text royal proclamation, posted to #news. |
| `/gm set key value` | Sets a value to an absolute number, using the effect keys plus the game-state fields. The reply shows the old and the new value before the Confirm. Nothing is posted. |
| `/gm apply effects [public]` | Applies effects written as text (below), as a delta, through the same code as events. With `public`, the effect summary is posted to #news; otherwise only the GM sees it. For follow-ups that shouldn't wait a turn, such as a failed crackdown or a riot. |
| `/gm roll battle attackers defenders` | Rolls a battle. Each side is a list of unit ids or dice, such as `6d12+2`. |
| `/gm roll riot province` | Rolls a riot. Both rolls show every die to the GM privately; the GM announces them with `/gm proclaim` or `/gm apply`. |
| `/gm resolve action [hours]` | Controls the resolution schedule: `now` runs one at once, `delay` pushes the next one back by `hours`, and `pause` and `resume` stop and restart the schedule. |
| `/gm reload` | Reloads `constants.yaml`. |
| `/gm undo` | Restores the snapshot taken before the last resolution (section 10). |
| `/gm peek [election]` | Shows the full hidden state privately, with numbers. With `election`, it runs the election on the current state instead and shows the result privately. |

**Content management (`/content`)**

Issue, bill, project, event and crisis **templates** are written as YAML files (section 4, and [YAML Reference.md](YAML%20Reference.md) for every field) and managed entirely from Discord: the GM attaches files to import them, and edits them as YAML in a modal. Each template has a status:

- **Draft:** invisible to players, and freely editable.
- **Ready:** can be posted.
- **Archived:** out of use, but kept for the log.

Posting a template creates a **live copy**, so later edits to the template never change an issue, event or crisis already in play. A template can only be Ready when every template its effects open, make available, start or post is Ready, and a bill only when its issue template is Ready.

**Template ids** are slugs, written in the file's `id`. A template's kind is recognised from its fields, checked in this order: `answers` makes a crisis, `lean` a bill, `price` a project, `text` an event, and `for` an issue.

| Command | Effect |
| --- | --- |
| `/content import files` | Imports one or more attached YAML files, or a zip from `/content export`. Each new template arrives as a Draft. A file whose id already exists replaces that template, and a replaced Ready template returns to Draft. Every file is validated, and each error is listed with its file and line; a file with errors imports nothing, the others still import. |
| `/content edit id [live]` | Opens a modal holding the template's YAML. Saving replaces it, exactly as an import would. Changing the `id` saves a copy under the new id instead, leaving the original as it was. With `live`, it edits the live copy, within the limits in Editing live items. A template longer than a modal holds (4,000 characters) is edited by exporting it and importing it again. |
| `/content preview id` | Shows privately exactly what players will see, with parsed effects. |
| `/content ready id` | Validates a template and marks it Ready. It fails if any required field is missing. An event with a schedule is queued at once (see events/\*.yaml). |
| `/content list [kind] [status]` | Lists templates, optionally only issues, bills, projects, events or crises, and only one status. |
| `/content remove id` | Deletes a Draft that was never posted. Anything else is archived instead: out of use, but kept for the log. |
| `/content export [id]` | Attaches every template, or just one, as YAML files in a zip, ready to edit and import again. |

- Ids, factions, parties, provinces, conditions, issues, bills, projects, events, crises, answers, votes and ministries autocomplete in every command.

**Effects as text**, so they fit in a modal field: `discontent.north_arpal -2, economy -1, opinion.labour +0.05, crisis.dock_strike_carden`. The bot validates every key against the effect keys in section 3 and rejects anything unknown.

**Effect summaries**

Whenever effects reach players, the post shows a **summary of effects**, one line per effect in the order the GM wrote them. This covers fired events, crisis outcomes (the chosen answer, a complication or a default), passed laws and finished projects.

Every post that carries effects has the same shape (the example posts in section 4 predate it):

```
## Heading
Any plain lines, such as the vote count or the minister who chose
*The story, in italics*

**-- EFFECTS --**
- [<:stat_raise:1565442139855392808>] Discontent in North Arpal
- [<:stat_lower:1565442180057927860>] Economy −2
- North Arpal is now *Striking*
```

- A number going up or down starts with the **up** or **down** emoji (`display.upEmoji`, `display.downEmoji` in `constants.yaml`), followed by the value's readable name, for example "Discontent in North Arpal".
- Public numbers (section 9) also show the amount: economy, purses, the reserve, faction popularity, the Assembly size, and the tax rate as a percentage.
- The people's mood and Threat show only the direction, never the amount. These are opinion, salience, discontent, Favour and its baseline, and Threat. A change to the Favour baseline reads the same as a change to Favour, so the summary never reveals whether a shift will last.
- Actions use plain lines with no emoji: "New issue: The Condition of Labour", "New bill: The Ten Hours Bill", "Project begun: The Royal Railway", "Issue closed: Free Trade", "Crisis: The Carden Dock Strike", "North Arpal is now *Lawless*", "North Arpal is no longer *Epidemic*".
- An effect of 0 is left out. Something with no effects has no summary.

Section 4 shows example posts next to each content format: a fired event, a passed law and the crisis outcomes.

`/content preview` shows the post exactly as players will see it.

**Editing live items**

- **A live issue:** text and hidden values can be edited at any time. Every edit is logged, and players are shown changes to the *For* or *Against* text.
- **A live bill:** it can be edited until it is put to a vote, and again after a rejection. Players are shown changes to the motion.
- **A live project:** its effects and bonus can be edited until it finishes.
- **A queued event:** it can be edited until it fires.
- **A live crisis:** it can be edited until a minister responds. After that it is locked, so a response is never judged against something the minister did not see.

## 7. Daily resolution

At 08:00 (or when the GM triggers it), the bot first **snapshots** the database with the SQLite backup API, then runs these steps in order inside one database transaction. If any step throws an error, the transaction rolls back, the GM is alerted, and nothing is posted.

1. **Votes due:** count bills, cabinet approval and no confidence, as in section 5. A passed no-confidence motion turns the cabinet into a caretaker and calls a snap election, moving the stored pre-election to the season this resolution opens. Apply the law outcomes. Lapse no-confidence motions still gathering at their `gatherUntilResolution`.
2. **Crises due:** resolve each one in posting order, as in section 5, then score the condemnations.
3. **Questions due:** apply the penalty and the reward for each unanswered question.
4. **Campaign actions:** resolve campaigns, attacks and polls simultaneously (section 5), and deliver poll results privately.
5. **Standing work:**
   - apply pending raises, training and stationing, then roll crackdowns;
   - pay each project's spending, add its progress after graft, and apply any that finish, with their bonus if they are on time.
6. **Seasonal changes:** salience decay, discontent drift, Favour drift, then popularity drift.
7. **Fiscal steps,** only at the start of Winter or Spring (section 5).
8. **Election,** only at the end of a pre-election: cancel open votes and drop pending join applications (section 5), then run section 8.
9. **Advance** the season, and the year after Winter. Lift cooldowns that have expired, returning those bills to Available. Increment `resolutionNumber` and schedule the next resolution.
10. **Events:** the new turn starts, so every queued event whose `fireAtResolution` has come fires in the order it was queued, and its effects apply. Issues it opens are open from this turn, and crises it posts are due at the next resolution, so a minister always has the whole turn to answer. Then open the bills scheduled for this turn, due at the next resolution, or cancel them if this turn is a pre-election (section 5).
11. **Alerts** to the GM (section 10).
12. **Post:** a news summary to #news, the fired events, the new crises, the readings opened in #assembly, crackdown results, the crisis outcomes, minister responses and condemnation scores, the turn's campaigns and attacks to #campaign-log, an updated dashboard, and private reports to each faction and the cabinet. Rename the turn tracker (section 9).

Actions submitted after the resolution has started count towards the next one.

**Campaign days.** On the first two days of a pre-election, 08:00 runs a shorter step instead: snapshot, campaign actions (step 4), and posting, as **dispatches from the hustings** in #news. Nothing else changes, no events fire, and deadlines don't count it.

## 8. Election

Every party with at least one active member stands. Independents and retired factions win no seats.

1. **Safe boroughs:** S is the Assembly size (`GameState.seats`, 180 unless reformed), and F is the number of factions in standing parties. Each of them holds a safe borough in its home province and is guaranteed 1 seat, which leaves `S − F` seats.
2. **Apportionment:** split the remaining seats among the ten provinces by population, using the largest remainder.
3. **Base strength** of party P in province p:

   `base(P, p) = (sum of members' popularity, multiplied by homeMultiplier for members whose home province is p) ^ baseExponent`
4. **Programme score** of P in p, over the open issues:
   - `w_i = salience_i / total salience of open issues`; if the total is 0, the score is 0;
   - `n_i` is the number of parties, P included, whose stance on i has the same sign as P's; Silent stances and missing stances are left out;
   - `score(P, p) = Σ w_i × g(stance_Pi, o(i, p)) / √n_i`. If crowding is turned off in config, drop the √n\_i.
5. **Strength:** `strength(P, p) = base × clamp(1 + programmeWeight × score, multiplierMin, multiplierMax)`.
6. **Provincial seats:** D'Hondt on strengths, in each province. A tie goes to the party with the higher total popularity, then to the older party.
7. **Faction seats:** add up each party's seats from every province, split them among its members by popularity using the largest remainder (equally if the members' total popularity is 0), then add each member's safe borough seat.
8. **Store** each party's strength and seats in each province as ProvinceResult rows for the new term.
9. **Post** the results with the rest of the resolution (section 7, step 12), never before the transaction commits: seats by party and province, each faction's seats, the national vote share (each party's summed strength divided by the total), a seat chart and the province map. The post closes with a royal proclamation summoning the new Assembly.

**Seat chart:** the hemicycle (`ArchGenerator`), with parties ordered by seats, largest first, and each in its party colour.

**Province map**

Each province is coloured after the party with the highest strength there, which is that party's vote in the province. A tie uses the D'Hondt tie-break. A province where no party stands keeps the map's original fill.

- **Images:** three 849×504 PNGs in `assets/map/`, aligned pixel for pixel and copied to the build output.
  - `base.png` is the land, sea and borders, with no text. Every Aurelian province is filled with one grey, `#808080`, and Anzaria with a darker `#404040`. Anzaria is not in the index, so it never changes colour. The borders' blur and shadows lie outside the provinces.
  - `provinces.png` is the index image: every province a single flat colour (its `mapKey`), with no anti-aliasing. Everything else is transparent.
  - `overlay.png` is drawn on top after colouring: province names, city names and city dots, with their glow, on a transparent background. Keeping them separate means party colours never wash out the text.
- **Provinces and map keys** (`id` and `mapKey` in `provinces.yaml`):

  | Province | Id | Map key |
  | --- | --- | --- |
  | Ocidanten | `ocidanten` | `#FF0000` |
  | Provencie Sacs | `provencie_sacs` | `#3300FF` |
  | Agrinenten | `agrinenten` | `#00FF65` |
  | Soralenten | `soralenten` | `#CB00FF` |
  | Silvicarie | `silvicarie` | `#0065FF` |
  | Provencie Gares | `provencie_gares` | `#32FF00` |
  | North Arpal | `north_arpal` | `#CCFF00` |
  | Aurenten | `aurenten` | `#FF0098` |
  | Iracterie | `iracterie` | `#FF9900` |
  | Tovrerie | `tovrerie` | `#00FFFF` |

- **Rendering:** [Rendering/ProvinceMapGenerator.cs](Rendering/ProvinceMapGenerator.cs), alongside the seat charts. `Generate(partyColourByMapKey, savePath)` takes a map from a province's `mapKey` to its winning party's hex colour, and writes a PNG. Provinces left out keep the grey fill. The caller translates province ids to map keys through `provinces.yaml`.
  1. It loads the three images and checks that they are the same size.
  2. Each province pixel is multiplied per channel by `partyColour / 128`. The flat fill becomes exactly the party colour, and any shading painted into a province darkens with it instead of turning grey.
  3. The overlay is drawn on top.
- **Legend:** the embed carries the legend (party, colour emoji, provinces won), so the image needs no spare space for one.
- **Where it is posted:** with the election results in #news, in `/gm peek election` (from the preview's strengths), and on demand through `/map`, which redraws the latest term's ProvinceResult rows in each party's current colour. A province whose winning party was deleted since the election keeps the original fill.
- **Maintaining the art:** the project copies everything under `assets/` to the build output. If the map is redrawn, keep the province fill at `#808080` (other shades inside a province tint proportionally), put text, cities and any effect that should not take the party colour in the overlay, keep all three images at the same size (or regenerate the index to match), and keep them aligned pixel for pixel. The index was made from a 718×335 cut-out of the provinces, placed at offset (100, 137) on the base map. Do not let anti-aliasing or colour-profile conversion touch the index colours, or province lookups will miss.

After the election the next term begins. The outgoing cabinet stays as caretaker until the GM appoints a Chancellor and a new cabinet is approved (section 5).

`/gm peek election` runs this algorithm on the current state and shows the result only to the GM. It is meant for balancing and testing.

## 9. Visibility and channels

**Who sees what**

| Information | Players | Ministers | GM |
| --- | --- | --- | --- |
| Faction popularity, seats, parties, programmes | Yes | Yes | Yes |
| Issues and their status, bills with their motions and status | Yes | Yes | Yes |
| Projects: progress, spending, deadlines | Yes | Yes | Yes |
| Opinion and salience | In words, through polls; as a direction in effect summaries | Same | The numbers |
| Condition biases and other condition numbers | No | No | Yes |
| Province conditions (name and description) | Yes | Yes | Yes |
| Fired events and their effect summaries | Yes | Yes | Yes |
| Queued events | No | No | Yes |
| Crisis title, ministry, province, answer labels, leans and prices, and deadline | Yes | Yes | Yes |
| Crisis outcome texts and effect summaries | After the resolution | After the resolution | Yes |
| Minister's crisis response | After the resolution | After the resolution | Yes |
| Condemnations | Yes, at once | Yes, at once | Yes |
| Campaigns and attacks | After the resolution | After the resolution | Yes |
| Economy, total Budget, each purse's allocation and balance, the reserve, the Navy contribution | Yes | Yes | Yes |
| Ledger entries (what each purse and the reserve spent and received) | After the next resolution | At once for their own ministry, and for the Chancellor | Yes |
| Discontent in each province | In words | In words | The number |
| Favour | In words | In words | The number |
| Threat | No (only as a direction in effect summaries) | No | Yes |
| Divisions and gendarmerie, with their dice and stations | Yes | Yes | Yes |
| Pending orders | No | Yes | Yes |
| Crackdown results | Yes | Yes | Yes |
| Individual votes, as cast | Yes | Yes | Yes |
| A party's chat | Its members only | Its members only | Yes |
| Stance logs, vote results, off-programme stances, programme flips | Yes | Yes | Yes |

**Words for hidden values.** The bot turns a number into a word at these thresholds. Noise is added before the word is chosen.

- **Opinion** (`o(i, p)`, or `O(i)` for a national poll): 0.6 or more is *strongly for*, 0.2 to 0.6 *for*, −0.2 to 0.2 *divided*, −0.6 to −0.2 *against*, and below −0.6 *strongly against*.
- **Salience** (rounded): 0 is *forgotten*, 1 *a minor matter*, 2 *discussed*, 3 *much discussed*, 4 *a burning question*, and 5 *the talk of the nation*.
- **Favour:** 70 or more is *content*, 50–69 *calm*, 30–49 *restless*, 10–29 *resentful*, and below 10 *defiant*.
- **Discontent:** below 2 is *quiet*, 2–4 *grumbling*, 4–6 *restless*, 6–8 *unruly*, and 8 or more *rioting*.

A poll reads, for example: "In North Arpal, the Condition of Labour is *a burning question*, and the people are *strongly for* it."

**Voice.** Everything the bot posts publicly is written in the world's voice. The resolution summary reads as the news of the season. GM acts (appointing a Chancellor, imposing a cabinet, summoning the Assembly after an election, `/gm proclaim`) are posted as royal proclamations, under the heading "By His Majesty's Command". Campaign days are dispatches from the hustings. GM alerts and private errors stay plain.

**Channels.** The GM sets each channel with `/gm channel set`. The ids are stored in the database, and every game channel is allowed by the command whitelist.

- **#news:** the daily resolution summary, events, crises, condemnations, issues, royal proclamations, manifestos, programme flips, crackdown results and election results.
- **#dashboard:** one pinned embed, edited after every resolution: the calendar, parties and seats, economy, budget and purses, Favour in words, discontent in words and conditions by province, open issues and open crises.
- **#assembly:** bills scheduled for next turn, readings of bills, votes with a running tally, and no-confidence motions.
- **#campaign-log:** campaign actions and attacks, posted with the resolution that resolves them (never on submission, since they resolve simultaneously), stance logs, and programmes recorded outside a pre-election.
- **#cabinet:** private to the Cabinet role, the GM role and the bot. Pending orders, projects, crisis deadlines, and the cabinet's own discussion. The Cabinet role has no colour, so party colours stay as they are. Showing it separately in the member list is the GM's choice: hoisted above the party roles, ministers are listed under the Cabinet instead of under their party. When a minister leaves office they lose the role, and with it the chat; its history stays for the next cabinet.
- **One private channel per faction,** created by the bot under the faction category: poll results and reminders.
- **One chat per party,** created by the bot under the party category: members' discussion, programme drafts and join applications (section 6).
- **#gm:** private to the GM. Everything operational stays here, never in #news: after every resolution, campaign day and `/gm start` one **digest** (the seed, events fired, actions resolved, the election with each province's strengths, readings cancelled, the next resolution, and the alerts gathered under "Needs your attention"), plus alerts raised between resolutions, previews and errors. #news carries only what the world would report.
- **Turn tracker:** a voice channel whose name shows the current turn, for example `Summer 1851`, `Spring 1854 · Pre-election`, or `Summer 1851 · Paused` while paused. On this server it is `1565059818731995166`. The bot renames it after every resolution, undo, pause or resume, and after `/gm set` changes the season or year. Discord allows only two renames per channel every 10 minutes, so renames are queued, and only the latest name is sent. The bot needs Manage Channels on it. Denying Connect to @everyone keeps it display-only.

**Reminders** go to a faction's private channel 12 hours before a resolution when it has an unused campaign action (either of its two), an unused opposition action (during a pre-election, only on its last day), an open vote it hasn't cast, or (as a minister) an unanswered crisis or question.

## 10. GM alerts, snapshots and undo

**Alerts** are posted to #gm after each resolution, and only when a threshold is newly crossed or an event happens:

- Discontent in a province reaches 7.
- Favour falls below 30, or reaches 0.
- Threat reaches 60, or 85.
- The War or Interior purse cannot cover its yearly upkeep, and its units lose training.
- A crackdown fails.
- A bill comes back from its cooldown.
- A project's bonus deadline is one resolution away and it will not make it at its current spending.
- A party dissolves (posted when it happens, not after the resolution).
- A cabinet seat is empty, a minister or the Chancellor resigns, a cabinet vote fails, or no-confidence co-signers reach the tabling threshold.
- A no-confidence motion passes and calls a snap election.
- A crisis or question is due at the next resolution and has no response, or both its ministry and the Chancellorship are vacant. For a crisis, which is always due at the resolution after it is posted, this alert comes 12 hours before that resolution, with the reminders.
- No event is queued for the next resolution.
- A player with an active faction leaves the server.
- An unregistered player tries a game command (once per player, posted when it happens).
- Discord refuses a role or channel change, for example because the bot's role is too low.
- A resolution was missed while the bot was offline.

The bot never acts on an alert itself. The GM decides what happens.

**Snapshots**

- Before every resolution and campaign day, the bot copies the database to a `snapshots/` folder, named by date and season, and keeps the last 30.
- `/gm undo` restores the latest snapshot, deletes the posts from that resolution where it can, re-pins the previous dashboard, and **pauses** the schedule. A second undo goes one resolution further back.
- Commands that players submitted after the snapshot are lost. The bot lists them to the GM from the log, and sends each affected faction its lost commands in its private channel, each with a Resubmit button that re-runs it with the same options (checked again against the restored state).
- When the GM resumes, the resolution re-runs with the same seed, so it reproduces the same rolls unless the state was changed.

**Log:** every state change is written to LogEntry with its source (command, resolution step or GM override). This makes disputes easy to settle and the history easy to export.

## 11. Build order

Build and test in this order. Each stage should be playable before the next begins.

1. **Foundation:** the schema in `InitializeDatabase()`, config loading, channel setup, the turn tracker and the resolution poller with scheduling, snapshots and pause. Include a test command that advances the season.
2. **Factions and parties:** GM faction registration and private channels, `/gm setup`, GM party management (create, move, edit, delete), party roles and chats with sync, joining through `/party apply` (pre-election only, by majority) and leaving, dissolution, programmes recorded by the GM, `/gm reward`, manifestos and flip penalties, `/info` (for factions and parties first), `/me` and `/help` (first versions, grown each stage), and the dashboard.
3. **Issues, events and campaigning:** `/content import`, `edit`, `preview`, `ready`, `list` and `remove` for every kind of template, the event queue with delays, bills made available by effects, province conditions, `/info` for provinces, issues and bills, events firing at the start of a turn with effect summaries, `/gm start` opening the founding pre-election and firing the opening event, `/gm apply`, campaign (with crowding), attack and poll (in words), salience, opinion and popularity drift.
4. **Elections:** the full algorithm in section 8, with unit tests built around the worked example below, the seat chart, the province map and the GM preview.
5. **Government:** the cabinet lifecycle with resignation, royal proclamations, votes with buttons and running tallies, `/bill propose` (the Chancellor's agenda and private bills) with readings scheduled for the next turn, laws with leans and per-bill cooldowns, `/opposition` actions (question, condemnation or private bill), questions, vacant ministries passing to the Chancellor, `/noconfidence` from any non-cabinet faction with co-signing buttons and lapsing, and snap elections.
6. **Crises:** `/info` for crises, posting through events, responses with two or more answers (including Neutral), prices from purses, complications (with the standard fallback), defaults and condemnations.
7. **Budget and standing work:** the fiscal year with the unrest drag, purses and upkeep, the Navy contribution and Favour, the reserve, divisions and gendarmerie (one unit model) with training lost to unpaid upkeep, crackdowns, and projects: started by effects or by ministers, spending with graft, bonus deadlines, `/ministry project`, `/ministry fund` and `/info` for projects.
8. **GM tools:** `/content export`, `/gm agenda`, alerts, reminders, battle and riot rolls, set, undo with resubmit buttons, and peek.

**Cut:** honours, loans, informants (on factions or provinces) and unit equipment were in earlier drafts. None of them is in the Human Rules: informants duplicated polls, equipment duplicated training, and each added a command to learn.

**Test case for the election code.** With crowding off and a single national "province" (both test-only settings), 30 seats in total (6 of them reserved, one per faction), three parties whose faction popularities are {20, 15, 10}, {30, 10} and {12}, and two issues (opinion +0.3 at salience 4, and +0.4 at salience 3):

- Stances: party A is Strongly For / For, party B is Against / Silent, party C is For / Strongly For.
- Expected seats: A 12, B 8, C 4 from D'Hondt over the remaining 24 seats. With the reserved seats added: A 15 (split 6, 5, 4), B 10 (split 7, 3), C 5.
- The margin is narrow (B's 9th quotient is about 2.337, A's 12th about 2.361), so the test also catches rounding errors in the exponent.
