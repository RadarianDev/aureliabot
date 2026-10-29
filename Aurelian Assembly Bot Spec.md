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
- **Players see words, the GM sees numbers.** Hidden values (opinion, salience, Favour, Threat) reach players only as words or as a direction, never as a number. Conditions and events are public, so players infer public opinion from what they can see.
- **Few decisions for players, heavy lifting for the bot.** A faction makes about one decision a day. Upkeep, drift, budgets and elections are the bot's job.
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
- The GM is anyone with the Manage Server permission. In the fiction, the GM is **His Majesty King Arthas II**. A **GM role**, set with `/gm channel set`, is given access to every private channel the bot creates.
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
- **Start:** the game opens in **Spring 1850** with a founding pre-election, so the first Assembly sits from Summer 1850. Before it opens, the GM approves the founding parties and queues an opening event that opens the first issues, so party programmes have something to cover.
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
| Channel | role (News, Dashboard, Assembly, CampaignLog, PartyRegistry, Cabinet, Gm, FactionCategory, PartyCategory, ArchiveCategory, TurnTracker, GmRole), discordId (a channel, category or role id) |
| Faction | id (the player's Discord user id), characterName, popularity, homeProvinceId, partyId (null if independent), seats, privateChannelId, status (Active, Retired) |
| Party | id, name, colour, emojiId, leaderFactionId, createdSeason, roleId, channelId, programmeMessageUrl |
| PartyJoinRequest | factionId, partyId, requestedAt |
| ProgrammeStance | partyId, issueId, stance. Only non-Silent stances are stored; a missing row means Silent |
| Province | id, name, population, discontent |
| ProvinceCondition | provinceId, conditionId, since |
| Issue | id, templateId, title, motion, forText, againstText, opinion, baseline, salience, salienceRaisedResolution, status (Open, Cooldown, Closed), cooldownUntil, passText, passEffects |
| Event | id, templateId, title, text, effects, status (Queued, Fired, Cancelled), queuedAt, firedResolution |
| Crisis | id, templateId, eventId, title, ministry, provinceId, issueId, answers, complication, default, postedAt, dueResolution, status, chosenAnswer, responseText |
| Answer (inside Crisis) | key, label, lean (For, Against, Neutral), price, text, effects |
| Bill | id, issueId, sponsorFactionId, kind (Government, Private), proposedAt, dueResolution, status (Open, Passed, Rejected, Cancelled) |
| NoConfidence | id, tablerFactionId, tabledAt, dueResolution, status (Gathering, Voting, Passed, Failed, Cancelled) |
| NoConfidenceCosigner | noConfidenceId, factionId |
| Vote | id, subjectType (Bill, NoConfidence, Cabinet), subjectId, factionId, choice (Yes, No, Abstain), castAt |
| Cabinet | id, kind (Sitting, Nominated), chancellorFactionId, status (Approved, Caretaker), ministries mapped to holder faction ids, dueResolution (for a nomination) |
| Purse | ministry (War, Interior, CommerceLabor, Treasury, Navy), balance, allocatedThisYear |
| Division | id, name, sides, bonus, provinceId |
| GendarmerieCorps | id, name, sides, bonus, provinceId |
| PendingOrder | id, unitType (Division, Gendarmerie, null for a crackdown), unitId, kind (Raise, Train, Equip, Station, Crackdown), provinceId, cost |
| Informant | id, provinceId, placedSeason |
| Project | id, typeId, provinceId, seasonsLeft, instalment, status (Active, Stalled, Finished) |
| Action | id, factionId, kind (Campaign, Attack, Poll), issueId, provinceId, stance, offProgramme, targetFactionId, submittedAt, resolved |
| OppositionAction | id, factionId, kind (Question, Shadow), season, year |
| Question | id, askerFactionId, ministry, text, askedAt, dueResolution, answerText, answeredAt |
| ShadowResponse | id, factionId, crisisId, answerKey, text |
| StanceLog | factionId, issueId, stance, season, source (Programme, Campaign, Vote), offProgramme |
| ProvinceResult | termNumber, provinceId, partyId, strength, seats |
| Snapshot | id, resolutionAt, file path to a copy of the database |
| LogEntry | id, at, kind, source, payload, publicFlag |

A faction has no name of its own. It is shown as the player's mention, followed by the character name where that helps.

**Templates:** IssueTemplate, EventTemplate and CrisisTemplate hold the same fields as Issue, Event and Crisis, plus a status (Draft, Ready, Archived), createdAt, updatedAt and timesPosted. A live Issue, Event or Crisis records the templateId it was posted from.

**Stance** is an enum with values: StronglyFor = +2, For = +1, Silent = 0, Against = −1, StronglyAgainst = −2. Silent means *no stance*: players never write it, and it is what a party has on an issue its programme doesn't list.

**Lean** of a crisis answer: For = +1, Against = −1, Neutral = 0.

**Crisis ministries** are War, Interior, CommerceLabor and Treasury. The Navy purse is a contribution with no minister, so it can never hold a crisis.

**Effects** are a map from a key to a number change. Valid keys:

- economy, taxRate, favour, favourBaseline, threat, reserve
- seats (the Assembly size, applied at the next election)
- purse.{ministry}, for every ministry except Navy
- discontent.{province}
- condition.{province}.{condition}: +1 gives the province the condition, −1 removes it
- opinion.{issue}, baseline.{issue}, salience.{issue}
- popularity.{faction}

`opinion.{issue}` is a temporary shift that drifts back towards the baseline. `baseline.{issue}` moves public opinion for good. `favour` and `favourBaseline` work the same way for Favour.

Three more keys are actions, not number changes, and take no value (in YAML, the value `true`):

- open.{issueTemplate} opens a live issue from a Ready template;
- close.{issue} closes a live issue without a vote;
- crisis.{crisisTemplate} posts a live crisis from a Ready template (events only).

Issues enter play only through `open`, in events and crisis answers, and leave play through a passed law or `close`. Crises enter play only through events.

## 4. Configuration files

All files live in a `config/` folder. The GM reloads them with a command. A reload changes constants only, never values already in play.

**constants.yaml**

```yaml
seats: 180                       # starting Assembly size; reforms change GameState.seats
display: { upEmoji: "⬆️", downEmoji: "⬇️" }   # replace with "<:name:id>" custom emoji
start: { season: Spring, year: 1850 }
resolutionTime: "08:00"
timeZone: "Europe/Prague"        # IANA id
minHoursBetweenResolutions: 12
preElectionDays: 3
seasonsPerTerm: 16
popularity: { min: 0, max: 100, start: 10 }
campaign: { factor: 4, homeMultiplier: 2 }
attack: { factor: 3, backfire: 2 }
poll: { opinionNoise: 0.15, salienceNoise: 1 }
strongStancePenalty: 0.5
programme: { flipPenalty: 1 }
opinion: { drift: 0.10, crisisShift: 0.05 }
salience: { min: 0, max: 5, campaignRaise: 1, crisisRaise: 1, decay: 1, rejectDrop: 2 }
rejectedCooldownSeasons: 4
law: { sponsorFactor: 4 }
government: { billsPerSeason: 2 }
opposition: { actionsPerSeason: 1, privateBillsPerYear: 1,
              unansweredQuestion: 2, questionReward: 1, shadowReward: 2, shadowPenalty: 1,
              noConfidenceTabling: 30, noConfidenceCooldownSeasons: 4 }
election: { baseExponent: 0.85, homeMultiplier: 1.5, programmeWeight: 0.5,
            multiplierMin: 0.5, multiplierMax: 1.5, crowding: true }
economy: { start: 100, growth: 0.01 }
taxRate: 0.20
reserve: { start: 10, interest: 0.03 }
defaultAllocation: { War: 0.30, Interior: 0.20, CommerceLabor: 0.25, Treasury: 0.10, Navy: 0.15 }
favour: { start: 60, baseline: 60, drift: 1, navyFactor: 20, alertBelow: 30 }
threat: { start: 25, alerts: [60, 85] }
discontent: { min: 0, max: 10, start: 3, rest: 3, drift: 0.5, alertAt: 7 }
division: { raise: 4, upkeep: 0.5, baseSides: 12, sidesStep: 2, maxSides: 18,
            trainCosts: [2, 3, 4], maxBonus: 6, equipCosts: [1, 1, 2, 2, 3, 3] }
gendarmerie: { raise: 2, upkeep: 0.25, baseSides: 8, sidesStep: 2, maxSides: 12,
               trainCosts: [1, 2], maxBonus: 4, equipCosts: [1, 1, 2, 2] }
crackdown: { cost: 1, reduction: 2, backlash: 1, failurePopularity: 2 }
informant: { costPerSeason: 0.5, discoveryChance: 0.10, discoveryPenalty: 3 }
projectPopularity: 2
startingForces:
  divisions: { aurenten: 2, iracterie: 1, north_arpal: 1, agrinenten: 1, ocidanten: 1 }   # province: count
  gendarmerie: [aurenten, iracterie, north_arpal]
```

Unit upkeep (`division.upkeep`, `gendarmerie.upkeep`) is per year, paid from the War and Interior purses each Spring (section 5). Informant cost is per season.

**provinces.yaml** ([config/provinces.yaml](config/provinces.yaml)): id, name, population, a one-line public description and starting conditions for each of the ten provinces, matching the census and geography in [Lore.md](Lore.md) (populations in millions; only the proportions matter, for seat apportionment). Harbours need the `coastal` condition. Each province also has a `mapKey`: its flat colour in the map's index image (section 8). Conditions are copied into the database at game start; after that, events and crisis answers change them.

**conditions.yaml** ([config/conditions.yaml](config/conditions.yaml)): the catalogue of province conditions. A condition is public, so it carries a name and a one-line description players can read. Its numbers are hidden.

```yaml
lawless:
  name: Lawless
  description: Gangs rule the back streets, and the watch looks away.
  biases: { conscription: 0.1, press_act: -0.1, poor_law: 0.1 }   # opinion bias per issue id; missing issues are 0
  discontentRest: 1                 # added to the province's resting discontent
  growth: -0.002                    # added to the national yearly growth, per province with it
```

Every field except `name` is optional and defaults to 0. Crime is not a separate value: it is the `lawless` condition. The catalogue has two kinds of condition, which the bot treats alike:

- **Standing** conditions describe a province's character and are set in `provinces.yaml`: urban, industrial, farmland, frontier, anzarian_border, crossroads, rugged, forested, marshland, remote and coastal.
- **Event** conditions come and go with events and crises: lawless, epidemic, strike, famine and boom.

**Starting content** lives in `content/`, in the import format below: the twelve opening issues in `content/issues/` and the opening event in `content/events/opening_1850.yaml`. The GM imports them with `/gm import` before the game opens, marks them Ready and queues the opening event.

**issues/\*.yaml:** the import and export format for issue templates, one file per issue.

```yaml
id: factory_act
title: The Factory Act
motion: Limit factory working hours and ban child labour.
for: Protect workers.
against: Leave industry free.
opinion: 0.5
baseline: 0.4
salience: 2
pass:
  text: Inspectors enter the mills for the first time.
  effects: { economy: -1, discontent.iracterie: -1 }
```

**events/\*.yaml:** the import and export format for event templates, one file per event.

```yaml
id: carden_strike
title: Strike at the Carden Docks
text: The dockers walk out over wages, and the harbour falls silent.
effects: { discontent.north_arpal: 1, salience.factory_act: 1,
           condition.north_arpal.strike: 1, crisis.dock_strike_carden: true }
```

**crises/\*.yaml:** the import and export format for crisis templates, one file per template. The province and the issue are required, and so are at least two answers. Each answer leans *For* or *Against* the crisis's issue, or is *Neutral*; several answers may share a lean.

```yaml
id: dock_strike_carden
title: The Carden Dock Strike
ministry: CommerceLabor
province: north_arpal
issue: factory_act
answers:
  - key: meet
    lean: For
    label: Meet the strikers' demands
    price: 3
    text: The docks reopen within the week, and the owners are furious.
    effects: { discontent.north_arpal: -2, economy: -1, condition.north_arpal.strike: -1 }
  - key: inquiry
    lean: Neutral
    label: Commission a royal inquiry
    price: 4
    text: Learned gentlemen take evidence, and the dockers wait.
    effects: { discontent.north_arpal: -1 }
  - key: outlaw
    lean: Against
    label: Declare the strike unlawful
    price: 0
    text: The strike collapses, and so does trust in Carden.
    effects: { discontent.north_arpal: 2, condition.north_arpal.strike: -1 }
complication:
  text: The money runs out mid-negotiation, and the strike spreads.
  effects: { discontent.north_arpal: 1, discontent.iracterie: 1 }
default:
  text: The strike drags on unanswered, and ships divert to Mugi.
  effects: { economy: -2, discontent.north_arpal: 2 }
```

**projects.yaml:** the project catalogue. The price is paid in equal instalments, one per season (section 5). Effects apply once, when the project finishes, and are permanent. They may use any effect key, including issue keys such as `opinion.{issue}` and condition keys.

| Project | Price | Seasons | Economy | Other effect |
| --- | --- | --- | --- | --- |
| Railway | 6 | 4 | +2 | Discontent −1 in its province |
| Factory district | 8 | 4 | +3 | Discontent +1 in its province |
| Coal mine | 4 | 2 | +1 | none |
| Naval harbour (coastal only) | 5 | 3 | +1 | Favour +2 |

## 5. Formulas

All constants named here come from `constants.yaml`. Every value is clamped to its range after each change.

**Provincial opinion**

`o(i, p) = clamp(opinion_i + sum of condition biases of province p for issue i, −1, 1)`

**Stance score**

For stance s and opinion o:

- s = ±1: `g = s × o`
- s = ±2: `g = 2 × sign(s) × o − strongStancePenalty`
- s = 0: `g = 0`

**Salience factor:** `k(i) = salience_i / 3`

**Simultaneous actions:** every campaign, attack and poll in a turn is computed against the state at the start of the resolution (popularity, opinion, salience and logged stances). All popularity changes then apply together, and salience rises afterwards. Submission order never matters.

**Campaign**

1. `Δpopularity = campaign.factor × g(s, o(i, p)) × k(i)`, doubled when p is the faction's home province.
2. Then salience of i rises by 1, at most once per issue per season.

The stance s defaults to the party's programme stance on the issue. A faction may give a different stance; it is then logged publicly as **off-programme**. Where the programme takes no stance on the issue (and always for an independent), the faction must give a stance, and doing so is not off-programme. Every campaign writes the stance it used to the StanceLog.

**Attack** on target faction t, issue i, province p

1. Take t's stance: its latest StanceLog entry on i, or else its party's programme stance. An attack on a faction with no stance, or a Silent one, on i is rejected.
2. If `g(s_t, o(i, p)) < 0`, then t loses `attack.factor × |g| × k(i)`.
3. Otherwise, the attacker loses `attack.backfire`.

A faction cannot attack a member of its own party.

**Poll:** the faction's private channel receives the mood of the province on the issue, in words: `o(i, p)` with uniform noise of ±`poll.opinionNoise`, and salience with noise of ±`poll.salienceNoise`, each turned into words (section 9). The numbers are never shown.

**Programme changes:** recording a new programme outside a pre-election costs every member of the party `programme.flipPenalty` popularity for each issue whose stance changes sign (For to Against, or the reverse, at any strength). Adding a stance, dropping one, or changing its strength is free. During a pre-election, programmes are manifestos and change freely.

**Government and opposition**

- **Government:** every cabinet member, plus every member of a party that holds at least one cabinet seat (the Chancellorship counts).
- **Opposition:** every active member of a party that holds no cabinet seat. An independent outside the cabinet is neither, and cannot use opposition commands.

**Opposition actions:** each opposition faction has `opposition.actionsPerSeason` opposition action a season, spent on either a question or a shadow response. Private bills (`privateBillsPerYear`) and no confidence are counted separately.

**Cabinet lifecycle**

1. The GM appoints a Chancellor.
2. The Chancellor nominates a cabinet, which opens an approval vote. A faction may hold only one ministry, and the Chancellor may not hold a ministry.
3. If the vote passes, the nominated cabinet becomes the sitting cabinet. If it fails, the sitting cabinet stays, the GM is alerted, and the Chancellor may nominate again.
4. A sitting cabinet stays in office as **caretaker** until a new one is approved. This covers the time after an election and after a passed no-confidence motion. Caretaker ministers act with full powers.
5. Before the first cabinet is approved, every ministry is vacant. The GM can use `/gm cabinet impose` to fill the gap.

**Vacant ministries**

- A crisis due for a vacant ministry applies its default. No faction loses popularity, and the GM is alerted a resolution in advance.
- A question cannot be asked to a vacant ministry. An open question whose ministry becomes vacant is dropped, and the asker's opposition action is refunded.

**Crisis resolution**, where the minister chose one of the crisis's answers, and L is that answer's lean: +1 for *For*, −1 for *Against* and 0 for *Neutral*

1. If the ministry's purse covers the answer's price, deduct it and apply the answer's effects. The outcome is **clean**.
2. Otherwise, the purse is emptied, and the answer's effects and the complication both apply. The outcome is **complicated**.
3. The minister's faction gets `Δpopularity = campaign.factor × L × o(i, p) × k(i)`. A Neutral answer neither gains nor loses.
4. Opinion on i moves `+crisisShift × L` if clean, or `−crisisShift × L` if complicated. Salience of i rises by 1, whatever the lean.
5. **No response by the deadline:** the default applies, and the minister's faction loses `campaign.factor × k(i)`.
6. **Shadow responses:** a shadow response picks one of the answers, and its sign is that answer's lean. Let the right sign be the sign of `o(i, p)`. A shadow faction on the right sign gains `shadowReward` if the minister was not on it (a Neutral answer or no response counts as not on it). A shadow faction on the wrong sign loses `shadowPenalty` if the minister was on the right one. A Neutral shadow response neither gains nor loses. When `o(i, p)` is exactly 0, shadows neither gain nor lose.

The responsible minister is whoever holds the ministry at the deadline.

**Questions:** the minister holding the ministry at the deadline must answer it. If they don't, the minister's faction loses `unansweredQuestion` and the asker gains `questionReward`. If the ministry changed hands since the question was asked, the deadline moves one resolution later.

**Votes**

- Votes are weighted by each faction's seats at the time of the count. Only active factions with seats vote.
- Bills and cabinet approval pass with a majority of Yes and No votes. Abstentions don't count, and a tie fails.
- No confidence passes when Yes votes hold more than half of all filled seats.
- Votes are public as they are cast.
- A Yes vote on a bill logs the voter's stance on the issue as For, and a No vote logs it as Against.

**Laws**

- **Passed:** the sponsor faction gets `Δpopularity = law.sponsorFactor × opinion_i × k(i)`. The issue closes, and its pass text and effects apply.
- **Rejected:** salience falls by `rejectDrop`, and the issue goes into Cooldown for `rejectedCooldownSeasons`, after which it reopens.
- An issue can have only one open bill at a time.
- Reforms are ordinary issues whose pass effects change the rules, for example an Electoral Reform issue with `seats: +20`, or a tax issue with `taxRate: +0.05`.

**No confidence:** a motion goes to a vote once its tabler and co-signers together hold `noConfidenceTabling` seats. After a failed motion, no new one can be tabled for `noConfidenceCooldownSeasons`. A passed motion turns the sitting cabinet into a caretaker and calls a **snap election** (section 2): the season the resolution opens becomes the pre-election.

**Election boundary:** no bill or no-confidence motion can be opened during the pre-election. Any bill, motion or cabinet nomination still open when the election runs is cancelled, and its issue returns to Open with no cooldown.

**Every season**

- Opinion drifts: `opinion += opinion.drift × (baseline − opinion)`.
- Any issue whose salience was not raised this season loses 1 salience.
- Discontent in each province moves `discontent.drift` toward its resting value: `discontent.rest` plus the `discontentRest` of each of the province's conditions.
- Favour moves `favour.drift` toward `favourBaseline`.

**Fiscal year**

1. **Start of Winter:** `economy ×= 1 + growth`, where growth is `economy.growth` plus the `growth` of every condition on every province. Then `Budget = economy × taxRate`; this is the **available** Budget.
2. **During Winter:** the Treasury allocates the available Budget across five purses: War, Interior, CommerceLabor, Treasury and Navy. Anything unallocated goes to the reserve.
3. **Start of Spring:**
   - leftover balances from last year's purses move to the reserve;
   - the reserve grows by `reserve.interest`;
   - the new allocation applies. If none was submitted, last year's percentages repeat; the first year uses `defaultAllocation`;
   - yearly upkeep is paid: divisions from the War purse, gendarmerie from the Interior purse. A purse that cannot cover its upkeep is emptied, and the GM is alerted.
4. **The Navy purse** is a contribution, not spending money. It is paid out at once. navyShare is the Navy allocation divided by the available Budget, and Anzaria judges the change: `favour += (navyShare − lastNavyShare) × navyFactor`, then `lastNavyShare = navyShare`. A raise pleases the nobles, a cut insults them, and an unchanged contribution leaves Favour to drift.
5. **Game start:** the reserve starts at `reserve.start`, `lastNavyShare` starts at the Navy share of `defaultAllocation`, and steps 1, 3 and 4 run once with `defaultAllocation`.

**Reserve:** the Treasury can grant any amount from the reserve to any purse except Navy. There are no loans.

**Forces**

- **Train:** sides rise by `sidesStep`. The cost is the next entry in `trainCosts`.
- **Equip:** the bonus rises by 1. The cost is the next entry in `equipCosts`.
- **Raise** creates a new unit at base sides with no bonus. **Disband** is immediate and refunds nothing.
- **Station** moves a unit to another province, for free.
- **Battle** (a GM command): every die on each side is rolled and its bonus added, and the side totals are compared. The higher total wins, and a tie goes to the defender. The loser loses its lowest-rolling unit, or its two lowest if the winner's total is at least double. The bot proposes the losses, and the GM confirms them. Losses only apply to real units, never to ad-hoc dice.
- **Riot** (a GM command): the gendarmerie in a province roll their total against one die with `max(2, round(2 × discontent))` sides. A province with no gendarmerie rolls 0. The bot reports the result, and the GM decides the consequences.

**Crackdown** (Interior), a gamble on order in one province:

- It needs at least one gendarmerie corps in the province, costs `crackdown.cost` from the Interior purse when ordered, and is rolled at the next resolution. At most one per province per season.
- The gendarmerie roll as in a riot, against the same discontent die.
- **Success** (the gendarmerie total is at least the die): discontent falls by `crackdown.reduction`, and the `lawless` condition is removed if present.
- **Failure:** discontent rises by `crackdown.backlash`, the Interior minister's faction loses `crackdown.failurePopularity`, and the GM is alerted to write the follow-up.
- The result is public, in the resolution's news.

**Projects**

- A project's instalment is its price divided by its seasons. At every resolution, an Active or Stalled project pays its instalment from the Commerce and Labor purse and counts down one season. If the purse cannot pay, nothing is taken, the project is Stalled for that season, and the minister is told privately.
- When a project finishes, its effects apply and the Commerce and Labor minister's faction gains `projectPopularity`.

**Informants** (provinces only)

- Each informant costs `costPerSeason` from the Interior purse at every resolution. If the purse cannot pay, the informant is recalled.
- Each season, the current Interior minister's private channel receives the mood of the province on one random open issue, in words, with noise of ±0.1.
- **Discovery:** each season, an informant has a `discoveryChance` of being exposed. It is then removed, a public log entry is posted, and the current Interior minister's faction loses `discoveryPenalty`.

## 6. Commands

All commands are slash commands. Long text, such as a roleplay response, is entered in a modal. The bot checks permissions and timing, and replies privately with an error if a command is not allowed.

**Factions**

| Command | Who | When | Effect |
| --- | --- | --- | --- |
| `/faction create character home` | Any player without a faction | Any time | Creates a faction keyed on the player's Discord account, at starting popularity, and a private channel for it. The home province is the character's safe borough (section 8). It holds no seats until the next election. |
| `/faction info [faction]` | Anyone | Any time | Shows popularity, party, seats, home province and logged stances. |
| `/province info province` | Anyone | Any time | Shows a province's description, discontent, conditions (name and description), forces count and last election result. |
| `/map` | Anyone | Any time | Shows the province map from the latest election. |

**Parties**

A party is founded by **two or more** factions. They apply at any time with an ordinary message in #party-registry, and the GM approves the application by creating the party with `/gm party create`. Players manage membership after that; the GM records programmes (below).

| Command | Who | When | Effect |
| --- | --- | --- | --- |
| `/party join party` | An independent faction | Pre-election only | Requests to join. The request is posted in the party chat, where the leader accepts or declines with buttons. Requests still pending when the election runs are dropped. |
| `/party leave` | A member | Any time | The faction becomes independent and keeps its seats. If the leader leaves, leadership passes to the most popular remaining member. |
| `/party leader faction` | The party leader | Any time | Hands leadership to another member. |

Joining or leaving a party never changes seats. Seats only change at an election. A party that falls below two members is not dissolved; the GM is alerted and decides.

**Party role and chat**

Every party has a Discord role and a private text channel, both created and kept in sync by the bot.

- **Role:** named after the party and coloured with the party colour. It is shown separately in the member list, so players are grouped by party. Members hold it; nobody else does. Discord shows a member's highest coloured role, so other coloured roles should sit below the party roles.
- **Chat:** a text channel named after the party, under the party category. Only the party role, the GM role and the bot can see it. The leader can also pin and manage messages there.
- **Membership:** the bot adds or removes the role whenever a faction joins, leaves, is moved by the GM, is retired, or rejoins the server. The role is the only thing that grants access to the chat, so access follows membership automatically.
- **Leadership:** when the leader changes, the bot moves the leader's channel permissions to the new leader.
- **Edits:** renaming or recolouring a party renames the role and the channel, and recolours the role.
- **Deletion:** the role is deleted, and the chat is moved to the archive category, read-only and visible only to the GM role, so its history is kept.
- **Sync:** on startup and on `/gm party sync`, the bot checks every party's role, role members and channel against the database, and repairs any differences. If Discord refuses a change, for example because the bot's role is too low, the GM is alerted.

**Party programmes**

A party writes its programme as an ordinary Discord message, and the GM records it with `/gm party programme party message`. `party` is chosen with autocomplete, and `message` is the message's link (**Copy Message Link** in Discord). The bot can read any message in a channel it can see, as long as it has the Message Content intent.

The message looks like this:

```
## Liberal Union
The Factory Act: *Strongly For*
Grain Duties: *Against*
Electoral Reform: *For*
```

- **Header:** lines starting with `#` are ignored, since the party comes from the command.
- **Lines:** every other non-empty line is `Issue: Opinion`.
  - The issue is matched against the title or the id of an open issue, ignoring capitalisation.
  - The opinion is one of *Strongly For*, *For*, *Against* or *Strongly Against*, ignoring capitalisation, surrounding spaces and Markdown emphasis (`*`, `_`).
- **Missing issues have no stance.** Any open issue the message doesn't mention is recorded as Silent (no stance), so the message is always the party's complete programme and replaces the previous one.
- **Errors:** a line that names an unknown or closed issue, repeats an issue, or has an unrecognised opinion (including *Silent*) fails the whole message. The bot lists every bad line to the GM privately, and nothing is recorded, so the message can be fixed and run again.
- **Preview:** before recording, the bot shows the GM privately every open issue with its parsed stance, marking the ones with no stance and the ones that flip sign, with the popularity cost of the flips (section 5), and Apply and Cancel buttons.
- **On apply:** the party's ProgrammeStance rows are replaced, the change is logged, and the bot replies to the original message with the recorded programme, so the party sees exactly what was understood.
  - **During a pre-election,** the programme is the party's **manifesto**: it is posted to #news as such, and changes freely.
  - **Outside a pre-election,** it is posted to #campaign-log. Every flipped stance is announced in #news ("The Liberal Union changes its line on Grain Duties"), and the flip penalty applies.
- **Timing:** the GM may record a programme at any time. It takes effect immediately, including for the election's programme score. Issues opened later have no stance until a new programme is recorded.

**Campaign actions** (one per faction per turn, and one per day during the pre-election)

| Command | Effect |
| --- | --- |
| `/campaign issue province [stance]` | Campaigns on an issue in a province. The stance defaults to the programme stance; giving a different one is off-programme. |
| `/attack faction issue province` | Attacks a rival's stance. |
| `/poll issue province` | Polls an issue in a province. The answer comes back in words. |

Submitting another action before the resolution replaces the pending one.

**Opposition** (opposition factions only)

| Command | Limit | Effect |
| --- | --- | --- |
| `/question ask ministry` | The season's opposition action | Opens a modal for the question. The minister must answer by the second resolution after it is asked. |
| `/shadow crisis answer` | The season's opposition action | Opens a modal for a shadow response to an open crisis, choosing one of its answers. |
| `/bill private issue` | 1 per year | Puts an issue's motion to a vote. |
| `/noconfidence table` and `/noconfidence cosign` | Not during the pre-election, nor during the cooldown | The motion goes to a vote once its tabler and co-signers hold 30 seats. |

**The Assembly**

| Command | Who | Effect |
| --- | --- | --- |
| `/vote subject yes/no/abstain` | Any active faction with seats | Casts or changes a vote until the deadline. Votes are weighted by seats. |

**Bills, cabinet and no-confidence votes** resolve at the second resolution after they open, so every vote has at least one full day.

**Ministers**

| Command | Who | Effect |
| --- | --- | --- |
| `/crisis respond crisis answer` | The minister whose ministry holds the crisis | Opens a modal for the response. The answer can be changed until the deadline, and the response stays private until the resolution. |
| `/question answer question` | The minister asked | Opens a modal for the answer, which is posted publicly. |
| `/bill government issue` | Any minister, or the Chancellor | Puts an issue's motion to a vote. At most `government.billsPerSeason` a season across the whole government. |
| `/treasury allocate war interior commerce treasury navy` | Treasury | During Winter: allocates the available Budget, in crowns. |
| `/treasury grant ministry amount` | Treasury | Moves money from the reserve to a purse. |
| `/war raise name province`, `/war train division`, `/war equip division`, `/war station division province`, `/war disband division` | War | Manages divisions. |
| `/interior raise name province`, `/interior train corps`, `/interior equip corps`, `/interior station corps province` | Interior | Manages gendarmerie. |
| `/interior crackdown province` | Interior | Orders a crackdown (section 5). |
| `/interior informant place province`, `/interior informant recall informant` | Interior | Manages informants. |
| `/commerce project type province` | Commerce and Labor | Starts a project. Its first instalment is paid at the next resolution. |
| `/chancellor nominate treasury war interior commerce` | Chancellor | Nominates the cabinet and opens the approval vote. |

Purchases (forces and crackdowns) deduct their cost from the purse immediately, and take effect at the next resolution as a PendingOrder. Projects are the exception, paid per season.

**GM**

| Command | Effect |
| --- | --- |
| `/gm party create name colour leader founder [founder3] [founder4] [founder5] [emoji]` | Approves an application from #party-registry: creates a party with the leader and the founders (at least two factions in all) as members, along with its role and chat. Every founder must be independent. The colour is `#RRGGBB`, and is used for the role, the seat chart and the province map. |
| `/gm party edit party [name] [colour] [emoji]` | Edits a party, and updates its role and chat. |
| `/gm party add faction party`, `/gm party remove faction` | Moves a faction into or out of a party directly, at any time. A GM override of the pre-election joining rule. |
| `/gm party delete party` | Deletes a party. Its members become independent, its role is deleted and its chat is archived. |
| `/gm party sync` | Repairs every party's role and chat to match the database. |
| `/gm party programme party message` | Parses the linked message as the party's programme and records it after a preview (see Party programmes). |
| `/gm faction edit faction [character] [home]` | Edits a faction. |
| `/gm faction retire faction` | Retires a faction: it leaves its party and any ministry, its pending actions are dropped, and its seats stay vacant until the next election. |
| `/gm channel set role target` | Sets a game channel, a category (faction, party or archive) or the GM role. Game channels are added to the command whitelist automatically. |
| `/gm event queue template` | Queues an event from a Ready template, to fire at the start of the next turn (section 7). Issues and crises enter play this way. |
| `/gm event cancel event` | Removes a queued event before it fires. |
| `/gm crisis cancel crisis [message]` | Withdraws a live crisis, with an optional in-world message. |
| `/gm chancellor appoint faction` | Appoints the Chancellor, announced as a royal proclamation. |
| `/gm cabinet impose` | Imposes a caretaker cabinet without a vote, announced as a royal proclamation. |
| `/gm proclaim` | Opens a modal for a free-text royal proclamation, posted to #news. |
| `/gm set key value` | Sets any value, using the effect keys plus the game-state fields. |
| `/gm roll battle attackers defenders` | Rolls a battle. Each side is a list of unit ids or dice, such as `6d12+2`. |
| `/gm roll riot province` | Rolls a riot. |
| `/gm resolve now`, `/gm resolve delay hours`, `/gm resolve pause`, `/gm resolve resume` | Controls the resolution schedule. |
| `/gm reload` | Reloads `constants.yaml`. |
| `/gm undo` | Restores the snapshot taken before the last resolution (section 10). |
| `/gm peek` | Shows the full hidden state privately, with numbers. |
| `/gm election preview` | Runs the election on the current state and shows the result privately. |

**Content management (GM)**

Issue, event and crisis **templates** live in the database and are written and edited from Discord. Each template has a status:

- **Draft:** invisible to players, and freely editable.
- **Ready:** can be posted.
- **Archived:** out of use, but kept for the log.

Posting a template creates a **live copy**, so later edits to the template never change an issue, event or crisis already in play. An event can only be Ready when every issue and crisis template it opens or posts is Ready.

| Command | Effect |
| --- | --- |
| `/gm issue create` | Opens a modal for a new issue template: title, motion, *For*, *Against* and pass text. It starts as a Draft. |
| `/gm issue set id field value` | Edits one field: opinion, baseline, salience, title, motion, *For*, *Against* or pass text. |
| `/gm issue effects id effects` | Sets the pass effects. |
| `/gm event create` | Opens a modal for a new event template: title, text and effects. It starts as a Draft. |
| `/gm event set id field value` | Edits one field: title, text or effects. |
| `/gm crisis create` | Opens a modal for a new crisis template: title, ministry, province and issue. |
| `/gm crisis answer id key [remove]` | Opens a modal to add or edit one answer: lean, label, price, text and effects. With `remove`, deletes the answer instead. A crisis needs at least two answers to be Ready. |
| `/gm crisis outcome id complication/default` | Opens a modal: text and effects. |
| `/gm crisis set id field value` | Edits one field. |
| `/gm preview id` | Shows privately exactly what players will see, with parsed effects. |
| `/gm ready id` | Validates a template and marks it Ready. It fails if any required field is missing. |
| `/gm list issues/events/crises [status]` | Lists templates. |
| `/gm archive id` | Takes a template out of use. |
| `/gm delete id` | Deletes a template permanently. Allowed only for Drafts that were never posted. |
| `/gm export` | Writes all templates to YAML files and attaches them as a zip. |
| `/gm import file` | Imports YAML templates from an attachment, as Drafts. |

- Ids, factions, parties, provinces, conditions, issues, events, crises, answers, votes and ministries autocomplete in every command.
- Crisis creation is split across several modals because a Discord modal holds at most five fields.

**Effects as text**, so they fit in a modal field: `discontent.north_arpal -2, economy -1, opinion.factory_act +0.05, crisis.dock_strike_carden`. The bot validates every key against the effect keys in section 3 and rejects anything unknown.

**Effect summaries**

Whenever effects reach players, the post shows a **summary of effects**, one line per effect in the order the GM wrote them. This covers fired events, crisis outcomes (the chosen answer, a complication or a default), passed laws and finished projects.

- A number going up or down starts with the **up** or **down** emoji (`display.upEmoji`, `display.downEmoji` in `constants.yaml`), followed by the value's readable name, for example "Discontent in North Arpal".
- Values players can see (section 9) also show the amount: economy, discontent, faction popularity, the Assembly size, and the tax rate as a percentage.
- Hidden values show only the direction, never the amount. These are opinion, baseline, salience, Favour and its baseline, Threat, purses and the reserve. A baseline change reads the same as an opinion change, and the same holds for Favour, so the summary never reveals whether a shift will last.
- Actions use plain lines with no emoji: "New issue: The Factory Act", "Issue closed: Grain Duties", "Crisis: The Carden Dock Strike", "North Arpal is now *Lawless*", "North Arpal is no longer *Epidemic*".
- An effect of 0 is left out. Something with no effects has no summary.

```
## Strike at the Carden Docks
The dockers walk out over wages, and the harbour falls silent.

<:up:…> Discontent in North Arpal +1
<:up:…> Salience: The Factory Act
North Arpal is now *Strike*
Crisis: The Carden Dock Strike
```

`/gm preview` shows the post exactly as players will see it.

**Editing live items**

- **A live issue:** text and hidden values can be edited at any time. Every edit is logged, and players are shown changes to the motion, *For* or *Against* text.
- **A queued event:** it can be edited until it fires.
- **A live crisis:** it can be edited until a minister responds. After that it is locked, so a response is never judged against something the minister did not see.

## 7. Daily resolution

At 08:00 (or when the GM triggers it), the bot first **snapshots** the database with the SQLite backup API, then runs these steps in order inside one database transaction. If any step throws an error, the transaction rolls back, the GM is alerted, and nothing is posted.

1. **Votes due:** count bills, cabinet approval and no confidence, as in section 5. A passed no-confidence motion turns the cabinet into a caretaker and calls a snap election, moving the stored pre-election to the season this resolution opens. Apply the law outcomes.
2. **Crises due:** resolve each one in posting order, as in section 5, then score the shadow responses.
3. **Questions due:** apply the penalty and the reward for each unanswered question.
4. **Campaign actions:** resolve campaigns, attacks and polls simultaneously (section 5), and deliver poll results privately.
5. **Standing work:**
   - apply pending raises, training, equipment and stationing, then roll crackdowns;
   - pay project instalments, count down the paid projects, and apply any that finish;
   - charge informants, send their reports, and roll for discovery.
6. **Seasonal changes:** opinion drift, salience decay, discontent drift, then Favour drift.
7. **Fiscal steps,** only at the start of Winter or Spring (section 5).
8. **Election,** only at the end of a pre-election: cancel open votes and drop pending join requests (section 5), then run section 8.
9. **Advance** the season, and the year after Winter. Lift cooldowns that have expired. Increment `resolutionNumber` and schedule the next resolution.
10. **Events:** the new turn starts, so every queued event fires in the order it was queued, and its effects apply. Issues it opens are open from this turn, and crises it posts are due at the next resolution, so a minister always has the whole turn to answer.
11. **Alerts** to the GM (section 10).
12. **Post:** a news summary to #news, the fired events, the new crises, crackdown results, the crisis outcomes and minister and shadow responses, an updated dashboard, and private reports to each faction and the cabinet. Rename the turn tracker (section 9).

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
- **Where it is posted:** with the election results in #news, in `/gm election preview` (from the preview's strengths), and on demand through `/map`, which redraws the latest term's ProvinceResult rows in each party's current colour. A province whose winning party was deleted since the election keeps the original fill.
- **Maintaining the art:** the project copies everything under `assets/` to the build output. If the map is redrawn, keep the province fill at `#808080` (other shades inside a province tint proportionally), put text, cities and any effect that should not take the party colour in the overlay, keep all three images at the same size (or regenerate the index to match), and keep them aligned pixel for pixel. The index was made from a 718×335 cut-out of the provinces, placed at offset (100, 137) on the base map. Do not let anti-aliasing or colour-profile conversion touch the index colours, or province lookups will miss.

After the election the next term begins. The outgoing cabinet stays as caretaker until the GM appoints a Chancellor and a new cabinet is approved (section 5).

`/gm election preview` runs this algorithm on the current state and shows the result only to the GM. It is meant for balancing and testing.

## 9. Visibility and channels

**Who sees what**

| Information | Players | Ministers | GM |
| --- | --- | --- | --- |
| Faction popularity, seats, parties, programmes | Yes | Yes | Yes |
| Issue motions and status | Yes | Yes | Yes |
| Opinion and salience | In words, through polls; as a direction in effect summaries | Same, plus informant reports for the Interior minister | The numbers |
| Condition biases and other condition numbers | No | No | Yes |
| Province conditions (name and description) | Yes | Yes | Yes |
| Fired events and their effect summaries | Yes | Yes | Yes |
| Queued events | No | No | Yes |
| Crisis title, ministry, province, answer labels and their leans, and deadline | Yes | Yes | Yes |
| Crisis prices | No | Yes | Yes |
| Crisis outcome texts and effect summaries | After the resolution | After the resolution | Yes |
| Minister's crisis response | After the resolution | After the resolution | Yes |
| Economy, total Budget, each purse's allocation, the Navy contribution | Yes | Yes | Yes |
| Purse balances, the reserve | No | Yes | Yes |
| Discontent in each province | Yes | Yes | Yes |
| Favour | In words | In words | The number |
| Threat | No (only as a direction in effect summaries) | No | Yes |
| Divisions and gendarmerie | Count only | In full | In full |
| Crackdown results | Yes | Yes | Yes |
| Informants | No | Interior minister only | Yes |
| Individual votes, as cast | Yes | Yes | Yes |
| A party's chat | Its members only | Its members only | Yes |
| Stance logs, vote results, off-programme stances, programme flips | Yes | Yes | Yes |

**Words for hidden values.** The bot turns a number into a word at these thresholds. Noise is added before the word is chosen.

- **Opinion** (`o(i, p)`): 0.6 or more is *strongly for*, 0.2 to 0.6 *for*, −0.2 to 0.2 *divided*, −0.6 to −0.2 *against*, and below −0.6 *strongly against*.
- **Salience** (rounded): 0 is *forgotten*, 1 *a minor matter*, 2 *discussed*, 3 *much discussed*, 4 *a burning question*, and 5 *the talk of the nation*.
- **Favour:** 70 or more is *content*, 50–69 *calm*, 30–49 *restless*, 10–29 *resentful*, and below 10 *defiant*.

A poll reads, for example: "In North Arpal, the Factory Act is *a burning question*, and the people are *strongly for* it."

**Voice.** Everything the bot posts publicly is written in the world's voice. The resolution summary reads as the news of the season. GM acts (appointing a Chancellor, imposing a cabinet, summoning the Assembly after an election, `/gm proclaim`) are posted as royal proclamations, under the heading "By His Majesty's Command". Campaign days are dispatches from the hustings. GM alerts and private errors stay plain.

**Channels.** The GM sets each channel with `/gm channel set`. The ids are stored in the database, and every game channel is allowed by the command whitelist.

- **#news:** the daily resolution summary, events, crises, issues, royal proclamations, manifestos, programme flips, crackdown results and election results.
- **#party-registry:** where factions apply to found a party. The bot reads nothing here; the GM approves with `/gm party create`.
- **#dashboard:** one pinned embed, edited after every resolution: the calendar, parties and seats, economy and budget, Favour in words, discontent and conditions by province, open issues and open crises.
- **#assembly:** bills, votes with a running tally, and no-confidence motions.
- **#campaign-log:** campaign actions and attacks, stance logs, programmes recorded outside a pre-election.
- **#cabinet:** private to ministers. Purses, forces, crisis prices and deadlines.
- **One private channel per faction,** created by the bot under the faction category: poll results, informant reports and reminders.
- **One chat per party,** created by the bot under the party category: members' discussion and join requests (section 6).
- **#gm:** private to the GM. Alerts, previews and errors.
- **Turn tracker:** a voice channel whose name shows the current turn, for example `📅 Summer 1851`, `📅 Spring 1854 · Pre-election`, or `⏸ Summer 1851` while paused. On this server it is `1565059818731995166`. The bot renames it after every resolution, undo, pause or resume, and after `/gm set` changes the season or year. Discord allows only two renames per channel every 10 minutes, so renames are queued, and only the latest name is sent. The bot needs Manage Channels on it. Denying Connect to @everyone keeps it display-only.

**Reminders** go to a faction's private channel 12 hours before a resolution when it has an unused campaign action, an unused opposition action in the season's last turn, an open vote it hasn't cast, or (as a minister) an unanswered crisis or question.

## 10. GM alerts, snapshots and undo

**Alerts** are posted to #gm after each resolution, and only when a threshold is newly crossed or an event happens:

- Discontent in a province reaches 7.
- Favour falls below 30, or reaches 0.
- Threat reaches 60, or 85.
- The War or Interior purse cannot cover its yearly upkeep.
- A crackdown fails.
- An issue reopens after its cooldown.
- A party falls below two members, or loses its last one.
- A cabinet seat is empty, a cabinet vote fails, or no-confidence co-signers reach the tabling threshold.
- A no-confidence motion passes and calls a snap election.
- A crisis or question is due at the next resolution and has no response, or its ministry is vacant.
- No event is queued for the next resolution.
- A player with an active faction leaves the server.
- Discord refuses a role or channel change, for example because the bot's role is too low.
- A resolution was missed while the bot was offline.

The bot never acts on an alert itself. The GM decides what happens.

**Snapshots**

- Before every resolution and campaign day, the bot copies the database to a `snapshots/` folder, named by date and season, and keeps the last 30.
- `/gm undo` restores the latest snapshot, deletes the posts from that resolution where it can, re-pins the previous dashboard, and **pauses** the schedule. A second undo goes one resolution further back.
- Commands that players submitted after the snapshot are lost. The bot lists them to the GM from the log and tells each affected faction to resubmit.
- When the GM resumes, the resolution re-runs with the same seed, so it reproduces the same rolls unless the state was changed.

**Log:** every state change is written to LogEntry with its source (command, resolution step or GM override). This makes disputes easy to settle and the history easy to export.

## 11. Build order

Build and test in this order. Each stage should be playable before the next begins.

1. **Foundation:** the schema in `InitializeDatabase()`, config loading, channel setup, the turn tracker and the resolution poller with scheduling, snapshots and pause. Include a test command that advances the season.
2. **Factions and parties:** faction creation and private channels, GM party management with two-founder creation, party roles and chats with sync, joining (pre-election only) and leaving, programmes with manifestos and flip penalties, and the dashboard.
3. **Issues, events and campaigning:** GM issue and event commands, province conditions and `/province info`, events firing at the start of a turn with effect summaries, campaign, attack and poll (in words), salience and opinion drift.
4. **Elections:** the full algorithm in section 8, with unit tests built around the worked example below, the seat chart, the province map and the GM preview.
5. **Government:** the cabinet lifecycle, royal proclamations, votes, bills, laws, opposition actions, questions, no confidence and snap elections.
6. **Crises:** posting through events, responses with two or more answers (including Neutral), prices from purses, complications, defaults and shadow responses.
7. **Budget and standing work:** the fiscal year, purses and upkeep, the Navy contribution and Favour, the reserve, divisions, gendarmerie, crackdowns, projects with instalments, and province informants.
8. **GM tools:** content management, alerts, reminders, battle and riot rolls, set, undo and peek.

**Deferred:** honours, loans and informants on factions were in earlier drafts. They are cut until the core game runs, because none of them is in the Human Rules and each adds a command to learn.

**Test case for the election code.** With crowding off and a single national "province" (both test-only settings), 30 seats in total (6 of them reserved, one per faction), three parties whose faction popularities are {20, 15, 10}, {30, 10} and {12}, and two issues (opinion +0.3 at salience 4, and +0.4 at salience 3):

- Stances: party A is Strongly For / For, party B is Against / Silent, party C is For / Strongly For.
- Expected seats: A 12, B 8, C 4 from D'Hondt over the remaining 24 seats. With the reserved seats added: A 15 (split 6, 5, 4), B 10 (split 7, 3), C 5.
- The margin is narrow (B's 9th quotient is about 2.337, A's 12th about 2.361), so the test also catches rounding errors in the exponent.
