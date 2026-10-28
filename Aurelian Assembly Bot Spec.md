# Aurelian Assembly: Bot Spec

Oct 28, 2026 · @eM

Implementation spec for the Discord bot that runs Aurelian Assembly. Read with the Brief Rules; where they differ, this spec wins. Every number lives in config and can be rebalanced without code changes.

## 1. Overview

**What the bot does**

- Tracks game state: factions, parties, seats, provinces, issues, crises, the budget, forces and projects.
- Collects player actions through slash commands.
- Resolves the game once a day, and posts the results.
- Runs elections.
- Alerts the GM when thresholds are crossed.

**What the bot does not do**

- It never creates story events. The GM decides when issues and crises appear, writes them in YAML or posts them inline, and decides all follow-ups.
- It keeps no story flags.

**Tech stack**

| Part | Choice |
| --- | --- |
| Language | C# (.NET 10) |
| Discord library | Discord.NET, slash commands, buttons and modals |
| Storage | SQLite through Microsoft.Data.Sqlite, as in the existing code: the schema lives in `InitializeDatabase()`, new columns are added with `AddColumnIfMissing`, and there is no migration framework |
| Bot settings | Token, guild id and database path in `appsettings.json` / `appsettings.Local.json`, as today |
| Game config and templates | Constants in YAML, read with YamlDotNet and reloadable at runtime. Issue and crisis templates in the database, edited from Discord, with YAML import and export |
| Scheduling | A hosted background service that resolves the turn daily at **08:00 in the configured time zone** |
| Hosting | Any small VPS |

**Scope and roles**

- The bot runs one game, in the guild set by `Discord:GuildId`.
- The GM is anyone with the Manage Server permission.

**Conventions**

- All money is in **crowns** and stored as a decimal with two places.
- Popularity, opinion, discontent and the other values are stored as doubles, and clamped to their ranges after every change.
- Every random roll uses one seeded random generator per resolution, and the seed is logged so results can be reproduced.
- Every deadline and resolution time is shown as a Discord timestamp (`<t:…:F>`), so each player sees it in their own time zone, next to the in-game season.

## 2. Time model

- **Turn:** one turn is one in-game season (Spring, Summer, Autumn, Winter) and lasts one real day, from one 08:00 resolution to the next.
- **Resolution:** the full daily resolution in section 7 that ends a turn. Every deadline counts in resolutions. The campaign days of a pre-election (below) are not resolutions.
- **Calendar:** the bot stores the season and the year, and every post is dated, for example "Summer 1851".
- **Start:** the game opens in **Spring 1850** with a founding pre-election, so the first Assembly sits from Summer 1850. Before it opens, the GM creates the parties and posts the opening issues, so party programmes have something to cover.
- **Term:** a term is **16 seasons**. Its last season is the **pre-election period**, which lasts **3 real days**; it is the only turn longer than one day. The first two days each end with a **campaign day** at 08:00 (section 7), and the third ends with the full resolution, which runs the election and begins the next term.
  - First term: Summer 1850 to Spring 1854, with the pre-election in Spring 1854.
- **Fiscal year:** the year's Budget is computed at the start of each Winter. The Exchequer allocates it during Winter, and the allocation takes effect at the Winter resolution, which opens Spring. At game start the bot runs the Winter budget step once with the default percentages, so the purses are funded from Spring 1850.
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
| GameState | season, year, termNumber, termStartSeason, resolutionNumber, nextResolutionAt, paused, taxRate, economy, favour, threat, reserve, rngSeed |
| Channel | role (News, Dashboard, Assembly, CampaignLog, Cabinet, Gm, FactionCategory, TurnTracker), discordChannelId |
| Faction | id (the player's Discord user id), characterName, popularity, homeProvinceId, partyId (null if independent), seats, privateChannelId, status (Active, Retired) |
| Party | id, name, colour, emojiId, leaderFactionId, createdSeason |
| PartyJoinRequest | factionId, partyId, requestedAt |
| ProgrammeStance | partyId, issueId, stance |
| Province | id, name, population, discontent, tags |
| Issue | id, templateId, title, motion, forText, againstText, opinion, baseline, salience, salienceRaisedResolution, status (Open, Cooldown, Closed), cooldownUntil, passText, passEffects |
| Crisis | id, templateId, title, ministry, provinceId, issueId, forLean, againstLean, complication, default, postedAt, dueResolution, status, chosenLean, responseText |
| Lean (inside Crisis) | label, price, text, effects |
| Bill | id, issueId, sponsorFactionId, kind (Government, Private), proposedAt, dueResolution, status (Open, Passed, Rejected, Cancelled) |
| NoConfidence | id, tablerFactionId, tabledAt, dueResolution, status (Gathering, Voting, Passed, Failed, Cancelled) |
| NoConfidenceCosigner | noConfidenceId, factionId |
| Vote | id, subjectType (Bill, NoConfidence, Cabinet), subjectId, factionId, choice (Yes, No, Abstain), castAt |
| Cabinet | id, kind (Sitting, Nominated), chancellorFactionId, status (Approved, Caretaker), ministries mapped to holder faction ids, dueResolution (for a nomination) |
| Purse | ministry (War, Interior, CommerceLabour, Exchequer, Navy), balance, allocatedThisYear |
| Loan | id, principal, remaining, instalment, takenYear |
| Division | id, name, sides, bonus, provinceId |
| GendarmerieCorps | id, name, sides, bonus, provinceId |
| PendingOrder | id, unitType (Division, Gendarmerie), unitId, kind (Raise, Train, Equip, Station), provinceId, cost |
| Informant | id, targetType (Faction, Province), targetId, placedSeason |
| Project | id, typeId, provinceId, seasonsLeft, status |
| Action | id, factionId, kind (Campaign, Attack, Poll), issueId, provinceId, stance, offProgramme, targetFactionId, submittedAt, resolved |
| Question | id, askerFactionId, ministry, text, askedAt, dueResolution, answerText, answeredAt |
| ShadowResponse | id, factionId, crisisId, lean, text |
| StanceLog | factionId, issueId, stance, season, source (Programme, Campaign, Vote), offProgramme |
| Honour | id, chancellorFactionId, targetFactionId, season |
| ProvinceResult | termNumber, provinceId, partyId, strength, seats |
| Snapshot | id, resolutionAt, file path to a copy of the database |
| LogEntry | id, at, kind, source, payload, publicFlag |

A faction has no name of its own. It is shown as the player's mention, followed by the character name where that helps.

**Templates:** IssueTemplate and CrisisTemplate hold the same fields as Issue and Crisis, plus a status (Draft, Ready, Archived), createdAt, updatedAt and timesPosted. A live Issue or Crisis records the templateId it was posted from.

**Stance** is an enum with values: StronglyFor = +2, For = +1, Silent = 0, Against = −1, StronglyAgainst = −2.

**Crisis ministries** are War, Interior, CommerceLabour and Exchequer. The Navy purse is a contribution with no minister, so it can never hold a crisis.

**Effects** are a map from a key to a number change. Valid keys:

- economy, taxRate, favour, threat, reserve
- purse.{ministry}, for every ministry except Navy
- discontent.{province}
- opinion.{issue}, baseline.{issue}, salience.{issue}
- popularity.{faction}

`opinion.{issue}` is a temporary shift that drifts back towards the baseline. `baseline.{issue}` moves public opinion for good.

## 4. Configuration files

All files live in a `config/` folder. The GM reloads them with a command. A reload changes constants only, never values already in play.

**constants.yaml**

```yaml
seats: 180
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
opinion: { drift: 0.10, crisisShift: 0.05 }
salience: { min: 0, max: 5, campaignRaise: 1, crisisRaise: 1, decay: 1, rejectDrop: 2 }
rejectedCooldownSeasons: 4
law: { sponsorFactor: 4 }
government: { billsPerSeason: 2 }
crisis: { minHours: 12 }
opposition: { questionsPerSeason: 1, shadowPerSeason: 1, privateBillsPerYear: 1,
              unansweredQuestion: 2, shadowReward: 2, shadowPenalty: 1,
              noConfidenceTabling: 30, noConfidenceCooldownSeasons: 4 }
election: { baseExponent: 0.85, homeMultiplier: 1.5, programmeWeight: 0.5,
            multiplierMin: 0.5, multiplierMax: 1.5, crowding: true }
economy: { start: 100, growth: 0.01 }
taxRate: 0.20
reserve: { start: 10, interest: 0.03 }
loan: { interest: 0.10, years: 4, maxDebtBudgets: 1 }
defaultAllocation: { War: 0.30, Interior: 0.20, CommerceLabour: 0.25, Exchequer: 0.10, Navy: 0.15 }
favour: { start: 60, navyTarget: 0.15, navyFactor: 20, alertBelow: 30 }
threat: { start: 25, alerts: [60, 85] }
discontent: { min: 0, max: 10, start: 3, rest: 3, drift: 0.5, alertAt: 7 }
division: { raise: 4, upkeep: 0.5, baseSides: 12, sidesStep: 2, maxSides: 18,
            trainCosts: [2, 3, 4], maxBonus: 6, equipCosts: [1, 1, 2, 2, 3, 3] }
gendarmerie: { raise: 2, upkeep: 0.25, baseSides: 8, sidesStep: 2, maxSides: 12,
               trainCosts: [1, 2], maxBonus: 4, equipCosts: [1, 1, 2, 2] }
informant: { costPerSeason: 0.5, discoveryChance: 0.10, discoveryPenalty: 3 }
honours: { perYear: 3, popularity: 1 }
projectPopularity: 2
startingForces:
  divisions: { aurenten: 4, iracterie: 2, north_arpal: 2 }   # province: count
  gendarmerie: [aurenten, iracterie, north_arpal]
```

Unit upkeep (`division.upkeep`, `gendarmerie.upkeep`) is per year, deducted from the Budget each Winter. Informant cost is per season.

**provinces.yaml:** id, name, population and tags for each of the ten provinces, matching the 1844 census in the Brief Rules. Northern harbours need the `coastal` tag. Each province also has a `mapKey`: its flat colour in the map's index image (section 8).

**tags.yaml:** for each tag, an opinion bias per issue id, for example `industrial: { factory_act: 0.3, grain_duties: 0.3 }`. An issue with no entry has a bias of 0.

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

**crises/\*.yaml:** the import and export format for crisis templates, one file per template. The province and the issue are required.

```yaml
id: dock_strike_carden
title: The Carden Dock Strike
ministry: CommerceLabour
province: north_arpal
issue: factory_act
leans:
  for:
    label: Meet the strikers' demands
    price: 3
    text: The docks reopen within the week, and the owners are furious.
    effects: { discontent.north_arpal: -2, economy: -1 }
  against:
    label: Declare the strike unlawful
    price: 0
    text: The strike collapses, and so does trust in Carden.
    effects: { discontent.north_arpal: 2 }
complication:
  text: The money runs out mid-negotiation, and the strike spreads.
  effects: { discontent.north_arpal: 1, discontent.iracterie: 1 }
default:
  text: The strike drags on unanswered, and ships divert to Mugi.
  effects: { economy: -2, discontent.north_arpal: 2 }
```

**projects.yaml:** the project catalogue. Effects apply once, when the project finishes, and are permanent.

| Project | Price | Seasons | Economy | Other effect |
| --- | --- | --- | --- | --- |
| Railway | 6 | 4 | +2 | Discontent −1 in its province |
| Factory district | 8 | 4 | +3 | Discontent +1 in its province |
| Coal mine | 4 | 2 | +1 | none |
| Northern harbour (coastal only) | 5 | 3 | +1 | Favour +2 |

## 5. Formulas

All constants named here come from `constants.yaml`. Every value is clamped to its range after each change.

**Provincial opinion**

`o(i, p) = clamp(opinion_i + sum of tag biases of province p for issue i, −1, 1)`

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

The stance s defaults to the party's programme stance on the issue. A faction may give a different stance; it is then logged publicly as **off-programme**. A faction with no programme stance on the issue (an independent, or an issue the party has no stance on) must give one. Every campaign writes the stance it used to the StanceLog.

**Attack** on target faction t, issue i, province p

1. Take t's stance: its latest StanceLog entry on i, or else its party's programme stance. An attack on a faction with no stance, or a Silent one, on i is rejected.
2. If `g(s_t, o(i, p)) < 0`, then t loses `attack.factor × |g| × k(i)`.
3. Otherwise, the attacker loses `attack.backfire`.

A faction cannot attack a member of its own party.

**Poll:** the faction's private channel receives `o(i, p)` with uniform noise of ±`poll.opinionNoise`, rounded to 0.05, and salience with noise of ±`poll.salienceNoise`.

**Government and opposition**

- **Government:** every cabinet member, plus every member of a party that holds at least one cabinet seat (the Chancellorship counts).
- **Opposition:** every other active faction.

**Cabinet lifecycle**

1. The GM appoints a Chancellor.
2. The Chancellor nominates a cabinet, which opens an approval vote. A faction may hold only one ministry, and the Chancellor may not hold a ministry.
3. If the vote passes, the nominated cabinet becomes the sitting cabinet. If it fails, the sitting cabinet stays, the GM is alerted, and the Chancellor may nominate again.
4. A sitting cabinet stays in office as **caretaker** until a new one is approved. This covers the time after an election and after a passed no-confidence motion. Caretaker ministers act with full powers.
5. Before the first cabinet is approved, every ministry is vacant. The GM can use `/gm cabinet impose` to fill the gap.

**Vacant ministries**

- A crisis due for a vacant ministry applies its default. No faction loses popularity, and the GM is alerted a resolution in advance.
- A question cannot be asked to a vacant ministry. An open question whose ministry becomes vacant is dropped.

**Crisis resolution**, with lean sign L = +1 for *For* and −1 for *Against*

1. If the ministry's purse covers the lean's price, deduct it and apply the lean's effects. The outcome is **clean**.
2. Otherwise, the purse is emptied, and the lean's effects and the complication both apply. The outcome is **complicated**.
3. The minister's faction gets `Δpopularity = campaign.factor × L × o(i, p) × k(i)`.
4. Opinion on i moves `+crisisShift × L` if clean, or `−crisisShift × L` if complicated. Salience of i rises by 1.
5. **No response by the deadline:** the default applies, and the minister's faction loses `campaign.factor × k(i)`.
6. **Shadow responses:** let the right sign be the sign of `o(i, p)`. A shadow faction on the right sign gains `shadowReward` if the minister was not on it (or did not respond). A shadow faction on the wrong sign loses `shadowPenalty` if the minister was on the right one. When `o(i, p)` is exactly 0, shadows neither gain nor lose.

The responsible minister is whoever holds the ministry at the deadline.

**Questions:** the minister holding the ministry at the deadline must answer it, or lose `unansweredQuestion`. If the ministry changed hands since the question was asked, the deadline moves one resolution later.

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

**No confidence:** a motion goes to a vote once its tabler and co-signers together hold `noConfidenceTabling` seats. After a failed motion, no new one can be tabled for `noConfidenceCooldownSeasons`.

**Election boundary:** no bill or no-confidence motion can be opened during the pre-election. Any bill, motion or cabinet nomination still open when the election runs is cancelled, and its issue returns to Open with no cooldown.

**Every season**

- Opinion drifts: `opinion += opinion.drift × (baseline − opinion)`.
- Any issue whose salience was not raised this season loses 1 salience.
- Discontent in each province moves `discontent.drift` toward its resting value.

**Fiscal year**

1. **Start of Winter:** `economy ×= 1 + growth`. Then `Budget = economy × taxRate`. Yearly unit upkeep and loan instalments are deducted, and what remains is the **available** Budget.
2. **During Winter:** the Exchequer allocates the available Budget across five purses: War, Interior, CommerceLabour, Exchequer and Navy. Anything unallocated goes to the reserve.
3. **Start of Spring:**
   - leftover balances from last year's purses move to the reserve;
   - the reserve grows by `reserve.interest`;
   - the new allocation applies. If none was submitted, last year's percentages repeat; the first year uses `defaultAllocation`.
4. **The Navy purse** is a contribution, not spending money. It is paid out at once, and `favour += (navyShare − navyTarget) × navyFactor`, where navyShare is the Navy allocation divided by the available Budget.
5. **Game start:** the reserve starts at `reserve.start`, and steps 1, 3 and 4 run once with `defaultAllocation`.

**Loans and reserve**

- A loan of L adds L to the Exchequer purse. Each Winter for `loan.years` years, starting with the next one, `L × (1 + interest) / years` is deducted as an instalment.
- Total outstanding debt cannot exceed `loan.maxDebtBudgets` times the last computed Budget. A loan above that is rejected.
- The Exchequer can grant any amount from the reserve to any purse except Navy.

**Forces**

- **Train:** sides rise by `sidesStep`. The cost is the next entry in `trainCosts`.
- **Equip:** the bonus rises by 1. The cost is the next entry in `equipCosts`.
- **Raise** creates a new unit at base sides with no bonus. **Disband** is immediate and refunds nothing.
- **Station** moves a unit to another province, for free.
- **Battle** (a GM command): every die on each side is rolled and its bonus added, and the side totals are compared. The higher total wins, and a tie goes to the defender. The loser loses its lowest-rolling unit, or its two lowest if the winner's total is at least double. The bot proposes the losses, and the GM confirms them. Losses only apply to real units, never to ad-hoc dice.
- **Riot** (a GM command): the gendarmerie in a province roll their total against one die with `max(2, round(2 × discontent))` sides. A province with no gendarmerie rolls 0. The bot reports the result, and the GM decides the consequences.

**Projects:** when a project finishes, its effects apply and the Commerce & Labour minister's faction gains `projectPopularity`.

**Informants**

- Each informant costs `costPerSeason` from the Interior purse at every resolution. If the purse cannot pay, the informant is recalled.
- **On a province:** the current Interior minister's private channel receives `o(i, p)` for one random open issue, with noise of ±0.1.
- **On a faction:** the GM is pinged to supply intelligence by hand.
- **Discovery:** each season, an informant has a `discoveryChance` of being exposed. It is then removed, a public log entry is posted, and the current Interior minister's faction loses `discoveryPenalty`.

**Honours:** the Chancellor may grant up to `honours.perYear` honours a year, never to themselves. Each gives the target `honours.popularity` and is logged publicly.

## 6. Commands

All commands are slash commands. Long text, such as a roleplay response, is entered in a modal. The bot checks permissions and timing, and replies privately with an error if a command is not allowed.

**Factions**

| Command | Who | When | Effect |
| --- | --- | --- | --- |
| `/faction create character home` | Any player without a faction | Any time | Creates a faction keyed on the player's Discord account, at starting popularity, and a private channel for it. It holds no seats until the next election. |
| `/faction info [faction]` | Anyone | Any time | Shows popularity, party, seats, home province and logged stances. |
| `/map` | Anyone | Any time | Shows the province map from the latest election. |

**Parties**

Parties are created by the GM (see the GM commands). Players manage membership and programmes.

| Command | Who | When | Effect |
| --- | --- | --- | --- |
| `/party join party` | An independent faction | Any time | Requests to join. The party leader accepts or declines with buttons. |
| `/party leave` | A member | Any time | The faction becomes independent and keeps its seats. If the leader leaves, leadership passes to the most popular remaining member. |
| `/party programme issue stance` | The party leader | Pre-election, or any time for an issue the party has no stance on yet | Sets the party's stance on an open issue. During the pre-election it can be changed until the election; outside it, it is fixed once set. |
| `/party leader faction` | The party leader | Any time | Hands leadership to another member. |

Joining or leaving a party never changes seats. Seats only change at an election.

**Campaign actions** (one per faction per turn, and one per day during the pre-election)

| Command | Effect |
| --- | --- |
| `/campaign issue province [stance]` | Campaigns on an issue in a province. The stance defaults to the programme stance; giving a different one is off-programme. |
| `/attack faction issue province` | Attacks a rival's stance. |
| `/poll issue province` | Polls an issue in a province. |

Submitting another action before the resolution replaces the pending one.

**Opposition** (opposition factions only)

| Command | Limit | Effect |
| --- | --- | --- |
| `/question ask ministry` | 1 per season | Opens a modal for the question. The minister must answer by the second resolution after it is asked. |
| `/shadow crisis lean` | 1 per season | Opens a modal for a shadow response to an open crisis. |
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
| `/crisis respond crisis lean` | The minister whose ministry holds the crisis | Opens a modal for the response. The lean can be changed until the deadline, and the response stays private until the resolution. |
| `/question answer question` | The minister asked | Opens a modal for the answer, which is posted publicly. |
| `/bill government issue` | Any minister, or the Chancellor | Puts an issue's motion to a vote. At most `government.billsPerSeason` a season across the whole government. |
| `/exchequer allocate war interior commerce exchequer navy` | Exchequer | During Winter: allocates the available Budget, in crowns. |
| `/exchequer loan amount` | Exchequer | Takes a loan, up to the debt cap. |
| `/exchequer grant ministry amount` | Exchequer | Moves money from the reserve to a purse. |
| `/war raise name province`, `/war train division`, `/war equip division`, `/war station division province`, `/war disband division` | War | Manages divisions. |
| `/interior raise name province`, `/interior train corps`, `/interior equip corps`, `/interior station corps province` | Interior | Manages gendarmerie. |
| `/interior informant place target`, `/interior informant recall informant` | Interior | Manages informants. |
| `/commerce project type province` | Commerce & Labour | Starts a project. |
| `/chancellor nominate exchequer war interior commerce` | Chancellor | Nominates the cabinet and opens the approval vote. |
| `/chancellor honour faction` | Chancellor | Grants an honour. |

Purchases deduct their cost from the purse immediately, and take effect at the next resolution as a PendingOrder.

**GM**

| Command | Effect |
| --- | --- |
| `/gm party create name colour leader [emoji]` | Creates a party with the leader as its first member. The leader must be independent. |
| `/gm party edit party [name] [colour] [emoji]` | Edits a party. |
| `/gm party add faction party`, `/gm party remove faction` | Moves a faction into or out of a party directly. |
| `/gm party delete party` | Deletes a party. Its members become independent. |
| `/gm faction edit faction [character] [home]` | Edits a faction. |
| `/gm faction retire faction` | Retires a faction: it leaves its party and any ministry, its pending actions are dropped, and its seats stay vacant until the next election. |
| `/gm channel set role channel` | Sets a game channel, or the category for private faction channels. Game channels are added to the command whitelist automatically. |
| `/gm issue post template` | Opens a live issue from a Ready template. |
| `/gm issue close issue [message]` | Withdraws a live issue without a vote, with an optional in-world message. |
| `/gm crisis post template [deadline]` | Posts a live crisis from a Ready template. The deadline defaults to the next resolution, or the one after if the next is less than `crisis.minHours` away. |
| `/gm crisis cancel crisis [message]` | Withdraws a live crisis, with an optional in-world message. |
| `/gm chancellor appoint faction` | Appoints the Chancellor. |
| `/gm cabinet impose` | Imposes a caretaker cabinet without a vote. |
| `/gm set key value` | Sets any value, using the effect keys plus the game-state fields. |
| `/gm roll battle attackers defenders` | Rolls a battle. Each side is a list of unit ids or dice, such as `6d12+2`. |
| `/gm roll riot province` | Rolls a riot. |
| `/gm resolve now`, `/gm resolve delay hours`, `/gm resolve pause`, `/gm resolve resume` | Controls the resolution schedule. |
| `/gm reload` | Reloads `constants.yaml`. |
| `/gm undo` | Restores the snapshot taken before the last resolution (section 10). |
| `/gm peek` | Shows the full hidden state privately. |
| `/gm election preview` | Runs the election on the current state and shows the result privately. |

**Content management (GM)**

Issue and crisis **templates** live in the database and are written and edited from Discord. Each template has a status:

- **Draft:** invisible to players, and freely editable.
- **Ready:** can be posted.
- **Archived:** out of use, but kept for the log.

Posting a template creates a **live copy**, so later edits to the template never change an issue or crisis already in play.

| Command | Effect |
| --- | --- |
| `/gm issue create` | Opens a modal for a new issue template: title, motion, *For*, *Against* and pass text. It starts as a Draft. |
| `/gm issue set id field value` | Edits one field: opinion, baseline, salience, title, motion, *For*, *Against* or pass text. |
| `/gm issue effects id effects` | Sets the pass effects. |
| `/gm crisis create` | Opens a modal for a new crisis template: title, ministry, province and issue. |
| `/gm crisis lean id for/against` | Opens a modal for one lean: label, price, text and effects. |
| `/gm crisis outcome id complication/default` | Opens a modal: text and effects. |
| `/gm crisis set id field value` | Edits one field. |
| `/gm preview id` | Shows privately exactly what players will see, with parsed effects. |
| `/gm ready id` | Validates a template and marks it Ready. It fails if any required field is missing. |
| `/gm list issues/crises [status]` | Lists templates. |
| `/gm archive id` | Takes a template out of use. |
| `/gm delete id` | Deletes a template permanently. Allowed only for Drafts that were never posted. |
| `/gm export` | Writes all templates to YAML files and attaches them as a zip. |
| `/gm import file` | Imports YAML templates from an attachment, as Drafts. |

- Ids, factions, parties, provinces, issues, crises, votes and ministries autocomplete in every command.
- Crisis creation is split across several modals because a Discord modal holds at most five fields.

**Effects as text**, so they fit in a modal field: `discontent.north_arpal -2, economy -1, opinion.factory_act +0.05`. The bot validates every key against the effect keys in section 3 and rejects anything unknown.

**Editing live items**

- **A live issue:** text and hidden values can be edited at any time. Every edit is logged, and players are shown changes to the motion, *For* or *Against* text.
- **A live crisis:** it can be edited until a minister responds. After that it is locked, so a response is never judged against something the minister did not see.

## 7. Daily resolution

At 08:00 (or when the GM triggers it), the bot first **snapshots** the database with the SQLite backup API, then runs these steps in order inside one database transaction. If any step throws an error, the transaction rolls back, the GM is alerted, and nothing is posted.

1. **Votes due:** count bills, cabinet approval and no confidence, as in section 5. A passed no-confidence motion turns the cabinet into a caretaker and asks the GM to appoint a Chancellor. Apply the law outcomes.
2. **Crises due:** resolve each one in posting order, as in section 5, then score the shadow responses.
3. **Questions due:** apply the penalty for each unanswered question.
4. **Campaign actions:** resolve campaigns, attacks and polls simultaneously (section 5), and deliver poll results privately.
5. **Standing work:**
   - apply pending raises, training, equipment and stationing;
   - count down projects, and apply any that finish;
   - charge informants, send their reports, and roll for discovery.
6. **Seasonal changes:** opinion drift, salience decay, then discontent drift.
7. **Fiscal steps,** only at the start of Winter or Spring (section 5).
8. **Election,** only at the end of a pre-election: cancel open votes (section 5), then run section 8.
9. **Advance** the season, and the year after Winter. Lift cooldowns that have expired. Increment `resolutionNumber` and schedule the next resolution.
10. **Alerts** to the GM (section 10).
11. **Post:** a news summary to #news, the crisis outcomes and minister and shadow responses, an updated dashboard, and private reports to each faction and the cabinet. Rename the turn tracker (section 9).

Actions submitted after the resolution has started count towards the next one.

**Campaign days.** On the first two days of a pre-election, 08:00 runs a shorter step instead: snapshot, campaign actions (step 4), and posting. Nothing else changes, and deadlines don't count it.

## 8. Election

Every party with at least one active member stands. Independents and retired factions win no seats.

1. **Reserved seats:** F is the number of factions in standing parties. Each of them is guaranteed 1 seat, which leaves `180 − F` seats.
2. **Apportionment:** split the remaining seats among the ten provinces by population, using the largest remainder.
3. **Base strength** of party P in province p:

   `base(P, p) = (sum of members' popularity, multiplied by homeMultiplier for members whose home province is p) ^ baseExponent`
4. **Programme score** of P in p, over the open issues:
   - `w_i = salience_i / total salience of open issues`; if the total is 0, the score is 0;
   - `n_i` is the number of parties, P included, whose stance on i has the same sign as P's; Silent stances and missing stances are left out;
   - `score(P, p) = Σ w_i × g(stance_Pi, o(i, p)) / √n_i`. If crowding is turned off in config, drop the √n\_i.
5. **Strength:** `strength(P, p) = base × clamp(1 + programmeWeight × score, multiplierMin, multiplierMax)`.
6. **Provincial seats:** D'Hondt on strengths, in each province. A tie goes to the party with the higher total popularity, then to the older party.
7. **Faction seats:** add up each party's seats from every province, split them among its members by popularity using the largest remainder (equally if the members' total popularity is 0), then add each member's reserved seat.
8. **Store** each party's strength and seats in each province as ProvinceResult rows for the new term.
9. **Post** the results: seats by party and province, each faction's seats, the national vote share (each party's summed strength divided by the total), a seat chart in party colours, and the province map.

**Province map**

Each province is coloured after the party with the highest strength there, which is that party's vote in the province. A tie uses the D'Hondt tie-break. A province where no party stands keeps the map's original fill.

- **Images:** three 849×504 PNGs in `assets/map/`, aligned pixel for pixel and copied to the build output.
  - `base.png` is the land, sea and borders, with no text. Every Aurelian province is filled with one grey, `#808080`, and Anzaria with a darker `#404040`. Anzaria is not in the index, so it never changes colour. The borders' blur and shadows lie outside the provinces.
  - `provinces.png` is the index image: every province a single flat colour (its `mapKey`), with no anti-aliasing. Everything else is transparent.
  - `overlay.png` is drawn on top after colouring: province names, city names and city dots, with their glow, on a transparent background. Keeping them separate means party colours never wash out the text.
- **Map keys** (`mapKey` in `provinces.yaml`):

  | Province | Key |
  | --- | --- |
  | Ocidanten | `#FF0000` |
  | Provencie Sacs | `#3300FF` |
  | Agrinenten | `#00FF65` |
  | Soralenten | `#CB00FF` |
  | Silvicarie | `#0065FF` |
  | Provencie Gares | `#32FF00` |
  | North Arpal | `#CCFF00` |
  | Aurenten | `#FF0098` |
  | Iracterie | `#FF9900` |
  | Tovrerie | `#00FFFF` |

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
| Opinion, baselines, salience, tag biases | No (only through polls) | No | Yes |
| Crisis title, ministry, province, lean labels and deadline | Yes | Yes | Yes |
| Crisis prices | No | Yes | Yes |
| Crisis outcome texts and effects | After the resolution | After the resolution | Yes |
| Minister's crisis response | After the resolution | After the resolution | Yes |
| Economy, total Budget, each purse's allocation, the Navy contribution | Yes | Yes | Yes |
| Purse balances, the reserve, loans | No | Yes | Yes |
| Discontent in each province | Yes | Yes | Yes |
| Favour | As a word | As a word | The number |
| Threat | No | No | Yes |
| Divisions and gendarmerie | Count only | In full | In full |
| Informants | No | Interior minister only | Yes |
| Individual votes, as cast | Yes | Yes | Yes |
| Stance logs, honours, vote results, off-programme stances | Yes | Yes | Yes |

**Favour in words:** 70 or more is *content*, 50–69 *calm*, 30–49 *restless*, 10–29 *resentful*, and below 10 *defiant*.

**Channels.** The GM sets each channel with `/gm channel set`. The ids are stored in the database, and every game channel is allowed by the command whitelist.

- **#news:** the daily resolution summary, crises, issues and election results.
- **#dashboard:** one pinned embed, edited after every resolution.
- **#assembly:** bills, votes with a running tally, and no-confidence motions.
- **#campaign-log:** campaign actions and attacks, stance logs, honours.
- **#cabinet:** private to ministers. Purses, forces, crisis prices and deadlines.
- **One private channel per faction,** created by the bot under the faction category: poll results, informant reports and reminders. The bot needs the Manage Channels permission.
- **#gm:** private to the GM. Alerts, previews and errors.
- **Turn tracker:** a voice channel whose name shows the current turn, for example `📅 Summer 1851`, `📅 Spring 1854 · Pre-election`, or `⏸ Summer 1851` while paused. On this server it is `1565059818731995166`. The bot renames it after every resolution, undo, pause or resume, and after `/gm set` changes the season or year. Discord allows only two renames per channel every 10 minutes, so renames are queued, and only the latest name is sent. The bot needs Manage Channels on it. Denying Connect to @everyone keeps it display-only.

**Reminders** go to a faction's private channel 12 hours before a resolution when it has an unused campaign action, an open vote it hasn't cast, or (as a minister) an unanswered crisis or question.

## 10. GM alerts, snapshots and undo

**Alerts** are posted to #gm after each resolution, and only when a threshold is newly crossed or an event happens:

- Discontent in a province reaches 7.
- Favour falls below 30, or reaches 0.
- Threat reaches 60, or 85.
- Total loan debt reaches the cap.
- An issue reopens after its cooldown.
- A party loses its last member.
- A cabinet seat is empty, a cabinet vote fails, or no-confidence co-signers reach the tabling threshold.
- A crisis or question is due at the next resolution and has no response, or its ministry is vacant.
- A player with an active faction leaves the server.
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

1. **Foundation:** the schema in `InitializeDatabase()`, config loading, channel setup, the turn tracker and the hosted resolution service with scheduling, snapshots and pause. Include a test command that advances the season.
2. **Factions and parties:** faction creation and private channels, GM party management, joining and leaving, programmes and the dashboard.
3. **Issues and campaigning:** GM issue commands, campaign, attack and poll, salience and opinion drift.
4. **Elections:** the full algorithm in section 8, with unit tests built around the worked example below, the seat chart, the province map and the GM preview.
5. **Government:** the cabinet lifecycle, votes, bills, laws, questions and no confidence.
6. **Crises:** posting, responses, prices from purses, complications, defaults and shadow responses.
7. **Budget and standing work:** the fiscal year, purses, the Navy contribution and Favour, loans, the reserve, divisions, gendarmerie, projects, informants and honours.
8. **GM tools:** content management, alerts, reminders, battle and riot rolls, set, undo and peek.

**Test case for the election code.** With crowding off and a single national "province" (both test-only settings), 30 seats in total (6 of them reserved, one per faction), three parties whose faction popularities are {20, 15, 10}, {30, 10} and {12}, and two issues (opinion +0.3 at salience 4, and +0.4 at salience 3):

- Stances: party A is Strongly For / For, party B is Against / Silent, party C is For / Strongly For.
- Expected seats: A 12, B 8, C 4 from D'Hondt over the remaining 24 seats. With the reserved seats added: A 15 (split 6, 5, 4), B 10 (split 7, 3), C 5.
- The margin is narrow (B's 9th quotient is about 2.337, A's 12th about 2.361), so the test also catches rounding errors in the exponent.
