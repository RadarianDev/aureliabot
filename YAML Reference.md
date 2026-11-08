# YAML Reference

Every YAML file the bot reads, with every field. The [spec](Aurelian%20Assembly%20Bot%20Spec.md) is the
source of truth; this file collects its formats in one place.

There are two kinds of file:

- **Config** (`config/`): the rules and the map. Loaded on startup, and reloaded automatically a moment
  after a file is saved (or with `/gm reload`). A reload changes constants only, never values already in
  play; a file that doesn't load leaves the old config in force, and #gm is told why.
- **Content** (`content/`): issues, bills, projects, events and crises, written by the GM. On startup, and a
  moment after a file is saved, every new or changed file is imported and marked Ready if it passes the
  readiness rules (anything that doesn't stays a Draft, and #gm gets the list). A file is only
  re-imported when the file itself changes, so an edit made in Discord with `/content edit` survives until
  the file is edited. Deleting a file never deletes its template; use `/content remove`. Templates can
  also be imported by attaching files to `/content import`, and `/content export` writes them back out in
  the same format.

Field names are camelCase. Ids are lowercase slugs (`north_arpal`, `ten_hours_bill`).

## Effects

Bills, projects, events and crisis answers all carry an `effects` map: a key and a change.

```yaml
effects: { economy: -20, discontent.north_arpal: 1, open.social_reform: true, bill.ten_hours_bill: true }
```

| Key | Value | Meaning |
| --- | --- | --- |
| `economy` | number | National economy (starts at 1000; the budget is economy × tax rate). |
| `taxRate` | number | Tax rate, as a fraction (`0.05` is five points). |
| `favour` | number | Anzarian Favour, 0–100. Drifts back to its baseline. |
| `favourBaseline` | number | Where Favour drifts to; a lasting change. |
| `threat` | number | Foreign Threat, 0–100. |
| `reserve` | number | The Treasury reserve. |
| `seats` | number | Assembly size, applied at the next election. |
| `senate` | `1` or `-1` | Establishes or abolishes the Senate. |
| `purse.{ministry}` | number | Changes that ministry's current balance at once, not its yearly allocation, and never below 0. `War`, `Interior`, `CommerceLabor` or `Treasury`; never `Navy`. Shows in the ministry's ledger under the bill or event's name. |
| `discontent.{province}` | number | Province discontent, 0–10. |
| `condition.{province}.{condition}` | `1` or `-1` | Gives or removes a condition. |
| `opinion.{issue}` | number | Opinion shift, −1 to 1. Lasting: opinion never drifts. |
| `salience.{issue}` | number | How much the issue matters, 0–5. |
| `popularity.{faction}` | number | A faction's popularity, 0–100 (the faction is its player's Discord user id). |
| `popularity.chancellor` | number | The sitting Chancellor's popularity. |
| `popularity.minister.{ministry}` | number | The popularity of whoever holds that ministry (`War`, `Interior`, `CommerceLabor`, `Treasury`). |
| `popularity.government` / `popularity.opposition` | number | Every government, or every opposition, faction's popularity. |
| `popularity.party.{party}` | number | Every member of a party (its number, as `/info` shows it). |
| `popularity.home.{province}` | number | Every faction whose home is that province. |
| `popularity.sponsor` | number | Whoever the effect belongs to: a law's sponsor, the minister who answered a crisis or runs a finished project. |
| `discontent.all` | number | Discontent in every province at once. |
| `condition.all.{condition}` | `1` or `-1` | Gives or removes a condition in every province. |
| `event.{eventTemplate}` | `true` or a number | Queues a follow-up event from a Ready template: at the start of the next turn, or that many turns later. Players don't see it in the summary. |
| `cancel.{eventTemplate}` | `true` | Cancels every queued copy of that event, so an answer or a law can avert it. Players don't see it in the summary. |
| `division.{province}` / `gendarmerie.{province}` | whole number | Raises that many units there at their base die, or with a negative number destroys that many, the weakest first. |
| `progress.{projectTemplate}` | number | Adds (or with a negative number removes) crowns of work on that live project; it finishes if the work reaches its price. |
| `halt.{projectTemplate}` | `true` | Halts that live project for good; its effects never apply. |
| `open.{issueTemplate}` | `true` | Opens a live issue from a Ready template. |
| `close.{issue}` | `true` | Closes a live issue, and withdraws its bills. |
| `bill.{billTemplate}` | `true` | Makes a bill available to propose. Its issue must be open. |
| `project.{projectTemplate}` | `true` | Starts a project. |
| `crisis.{crisisTemplate}` | `true` | Posts a live crisis. **Events only.** |
| `var.{variable}` | number | Changes one of your own variables (see `config/variables.yaml`). Players never see it in the summary. |

In a project's effects, `here` stands for the project's province: `discontent.here: -1`.

Every key is validated on import, and an unknown one is rejected. Values are clamped to their range
after each change. In modals and `/gm apply`, effects are written as text instead:
`discontent.north_arpal -2, economy -1, open.public_works`.

### Advanced effects

Everything below works in every `effects` field, in `/gm apply` and in modals. Posts only ever list what
actually happened; `/content preview` shows every branch, so check your logic there.

**Formulas.** Any number can be a formula, written as a string. **Setting** a value instead of changing it:
start with `=`.

```yaml
effects:
  economy: "-0.5 * var.plague"          # change by a computed amount
  favour: "=max(favour, 40)"            # set outright
  var.coup_stage: "=3"
```

Formulas use `+ - * / %`, brackets, comparisons (`< <= > >= == !=`, giving 1 or 0), `and`, `or`, `not`,
`true`, `false`, and the functions `min`, `max`, `abs`, `round(x)` or `round(x, digits)`, `floor`, `ceil`,
`clamp(x, low, high)`, `roll(sides)` or `roll(dice, sides)`, and `chance(p)` (1 with probability `p`,
else 0). They can read:

| Reference | Value |
| --- | --- |
| any numeric effect key | its current value: `economy`, `favour`, `threat`, `reserve`, `purse.War`, `discontent.north_arpal`, `opinion.social_reform`, `salience.social_reform`, `division.north_arpal`, … |
| `popularity.{faction}`, `popularity.chancellor`, `popularity.government`, … | popularity; for a group, its average |
| `var.{variable}` | one of your variables |
| `condition.{province}.{condition}` | 1 if the province has it, else 0 |
| `vacant.chancellor`, `vacant.{ministry}` | 1 if the office is empty |
| `faction.{faction}.{field}` | `popularity`, `seats`, `independent`, `government`, `opposition`, `office` (1 if in the cabinet) |
| `party.{party}.{field}` | `seats`, `members`, `popularity` (average), `government` |
| `issue.{issue}.open` | 1 if the issue is open |
| `year`, `season`, `resolution`, `term`, `preElection` | the calendar; `season` is 1–4, and `spring`, `summer`, `autumn`, `winter` stand for 1–4 |

**Conditions.** `if` takes a formula; `then` and `else` take effects. To mix plain effects and blocks,
make `effects` a list:

```yaml
effects:
  - { var.plague: 1 }
  - if: "var.plague >= 3 and favour < 30"
    then: { crisis.quarantine: true, discontent.all: 1 }
    else: { discontent.marsh_hollow: 0.5 }
```

**Random outcomes.** Each outcome has a `weight` (default 1) and `effects`:

```yaml
effects:
  - random:
      - weight: 3
        effects: { threat: 5 }
      - weight: 1
        effects: { event.palace_coup: true }
```

**Many targets at once.** `each` goes through every `province`, `faction` (active), `party` (with active
members) or `issue` (open). Inside it, `it` stands for the current one, in keys and formulas. `where`
filters; `by` with `top: n` or `bottom: n` picks the highest or lowest scores.

```yaml
effects:
  - each: province
    where: "condition.it.striking"
    do: { discontent.it: 1 }
  - each: faction
    by: "popularity.it"
    top: 1
    do: { popularity.it: -3 }
```

Blocks nest freely. An `it` always means the innermost `each`.

**As text** (modals and `/gm apply`), blocks use braces for formulas and brackets for lists:

```
var.plague 1, economy {-0.5 * var.plague}, favour ={max(favour, 40)}
if {var.plague >= 3} [crisis.quarantine, discontent.all 1] else [discontent.marsh_hollow 0.5]
random [3: threat 5 | 1: event.palace_coup]
each province where {condition.it.striking} [discontent.it 1]
each faction by {popularity.it} top 1 [popularity.it -3]
```

**Randomness during a resolution** comes from the resolution's seed, so an undone resolution replays the
same rolls. A formula that fails at play time (dividing by zero, say) skips that effect and warns the GM.

## Content

### `content/issues/*.yaml`: issues

One file per issue. An issue is a broad question that parties take stances on. It has no law of its
own; its laws are bills. It enters play through an `open.{id}` effect, and leaves only through a
`close.{id}` effect, which a bill can carry.

```yaml
id: social_reform
title: Social Reform
for: The Crown must protect the worker, the child and the pauper.
against: Thrift and self-help, low rates, and masters and men left to their own bargains.
opinion: 0.1
salience: 3
```

| Field | Required | Meaning |
| --- | --- | --- |
| `id` | yes | Slug, used in effects, bills and programmes. |
| `title` | yes | Shown everywhere, and matched in party programmes. |
| `for` | yes | The case for, shown to players. |
| `against` | yes | The case against. |
| `opinion` | yes | Starting base opinion, −1 (against) to 1 (for). Hidden. Only effects and crisis outcomes move it. |
| `salience` | yes | Starting salience, 0–5. Hidden. |

Province conditions add their `biases` (below) on top of `opinion`.

### `content/bills/*.yaml`: bills

One file per bill. A bill is one law on an issue, leaning `For` or `Against` it, so an issue can carry
rival bills. It becomes available to propose through a `bill.{id}` effect.

```yaml
id: ten_hours_bill
title: The Ten Hours Bill
issue: social_reform
lean: For
motion: Limit factory work to ten hours a day, ban the employment of children under twelve, and send Crown inspectors into every mill and pit.
pass:
  text: Crown inspectors walk into the mills and pits, the children walk out, and the owners swear the kingdom is ruined.
  effects: { economy: -50, purse.CommerceLabor: -20,
             discontent.iracterie: -3, discontent.aurenten: -2, discontent.north_arpal: -1 }
```

| Field | Required | Meaning |
| --- | --- | --- |
| `id` | yes | Slug. |
| `title` | yes | Shown in #assembly and #news. |
| `issue` | yes | The issue it belongs to. |
| `lean` | yes | `For` or `Against` the issue. A Yes vote logs this as the voter's stance; the sponsor is judged by it. |
| `motion` | yes | The law, as the Assembly votes on it. |
| `pass.text` | yes | Posted when it passes. |
| `pass.effects` | no | Applied when it passes. Add `close.{issue}` to settle the issue for good; add `bill.{id}` or `project.{id}` to chain into the next bill or a project. |

A rejected bill rests for `rejectedCooldownSeasons`, and its issue's other bills stay available. Only
one bill per issue can be before the Assembly at a time.

### `content/projects/*.yaml`: projects

One file per project. Paid season by season from one purse; applied once when finished.

```yaml
id: royal_railway
title: The Royal Railway
purse: CommerceLabor
province: provencie_gares
price: 90
term: 6
effects: { economy: 50, discontent.here: -2, discontent.soralenten: -1 }
bonus:
  by: { season: Spring, year: 1853 }
  text: The first train reaches the coast ahead of the guarantee, and the shareholders cheer the Crown.
  effects: { economy: 20, condition.iracterie.booming: 1 }
```

| Field | Required | Meaning |
| --- | --- | --- |
| `id` | yes | Slug. |
| `title` | yes | Display name. |
| `purse` | no | `War`, `Interior`, `CommerceLabor` or `Treasury`: who pays and runs it. Defaults to `CommerceLabor`. |
| `province` | no | Where it is built; `here` in its effects stands for it. Leave it out for a ministerial project, where the minister chooses. |
| `price` | yes | Total cost. |
| `term` | yes | Seasons it should take. The pace, `price / term`, is the most a season can usefully absorb. | At the default split Commerce and Labor gets about 60 a year, enough for one project at a pace of 15 a season.
| `effects` | no | Applied once when finished, and permanent. |
| `bonus.by` | no | `{ season, year }`: the bonus applies if it finishes by the end of that turn. |
| `bonus.within` | no | Instead of `by`: the bonus applies if it finishes within this many seasons of starting. |
| `bonus.text` / `.effects` | no | Posted and applied on top of `effects` when the bonus is earned. |
| `ministerial` | no | `true` lets the purse's minister start it with `/ministry project`. Defaults to `false`. |
| `requires` | no | A condition the province must have, such as `coastal`. |

A project starts through a `project.{id}` effect (from a bill, event, crisis answer or another project),
or, if ministerial, by its minister. The minister sets its spending each season with `/ministry fund`;
money above the pace is partly lost to graft (`project.graft` in `constants.yaml`).

### `content/events/*.yaml`: events

One file per event. Events fire at the start of a turn, and bring issues and crises into play.

```yaml
id: lanten_strike
title: Strike at the Lanten Docks
text: The dockers walk out over wages, and the harbour falls silent.
effects: { discontent.ocidanten: 1, salience.social_reform: 1,
           condition.ocidanten.striking: 1, crisis.lanten_dock_strike: true }
```

| Field | Required | Meaning |
| --- | --- | --- |
| `id` | yes | Slug. |
| `title` | yes | Headline in #news. |
| `text` | yes | The news story. |
| `effects` | no | Applied when it fires, and listed under the story with UP and DOWN arrows. |
| `schedule` | no | When it fires. Without one, queue it by hand with `/gm event queue`. |

`schedule` takes one of two forms:

- `{ season: Autumn, year: 1851 }` fires it at the start of that turn. `Spring 1850` means at
  `/gm start`.
- `{ delay: 3 }` fires it 3 turns after it is queued.

A scheduled event is queued as soon as it is marked Ready, and marking it Ready fails if that turn has
already started. Every template its effects reference must be Ready first.

### `content/crises/*.yaml`: crises

One file per crisis. A crisis is posted by an event (`crisis.{id}: true`), goes to one minister, and
is due at the next resolution.

```yaml
id: lanten_dock_strike
title: The Lanten Dock Strike
ministry: CommerceLabor
province: ocidanten
issue: social_reform
answers:
  - lean: For
    label: Meet the strikers' demands
    price: 30
    text: The docks reopen within the week, and the owners are furious.
    effects: { discontent.ocidanten: -2, economy: -10, condition.ocidanten.striking: -1 }
  - lean: Neutral
    label: Commission a royal inquiry
    price: 40
    text: Learned gentlemen take evidence, and the dockers wait.
    effects: { discontent.ocidanten: -1 }
  - lean: Against
    label: Declare the strike unlawful
    text: The strike collapses, and so does trust in Lanten.
    effects: { discontent.ocidanten: 2, condition.ocidanten.striking: -1 }
complication:
  text: The money runs out mid-negotiation, and the strike spreads.
  effects: { discontent.ocidanten: 1, discontent.provencie_sacs: 1 }
default:
  text: The strike drags on unanswered, and ships divert to Mugi.
  effects: { economy: -20, discontent.ocidanten: 2 }
```

| Field | Required | Meaning |
| --- | --- | --- |
| `id` | yes | Slug. |
| `title` | yes | Shown to the minister and in #news. |
| `ministry` | yes | `War`, `Interior`, `CommerceLabor` or `Treasury`. Goes to the Chancellor if vacant. |
| `province` | yes | Whose people judge the answer. |
| `issue` | yes | The issue the answers lean on. |
| `answers` | yes | At least two, numbered by their order from 1. |
| `answers[].lean` | yes | `For`, `Against` or `Neutral` on the issue. Several answers may share one. |
| `answers[].label` | yes | What the minister picks. |
| `answers[].price` | no | Paid from the ministry's purse. Defaults to 0. At the default split a year brings about War 50, Interior 30, Commerce and Labor 60 and Treasury 20, less upkeep, so 10–30 is a hard choice and 40 or more usually needs a grant. Keep Treasury crises at 20 or less. |
| `answers[].text` | yes | Posted when the answer is chosen. |
| `answers[].effects` | no | Applied when chosen. |
| `complication.text` / `.effects` | no | Added on top of the answer when the purse can't pay its price. Without one, the standard complication applies: discontent +1 in the crisis's province. |
| `default.text` / `.effects` | yes | Applied when no answer is chosen in time. |

Opposition factions can **condemn** a live crisis, publicly backing `For` or `Against`. That is a command, not part of the file.

## Config

### `config/constants.yaml`: rules and numbers

All the game's tuning lives here. Every field is required.

```yaml
seats: 180
display: { upEmoji: "<:stat_raise:1565442139855392808>", downEmoji: "<:stat_lower:1565442180057927860>" }
start: { season: Spring, year: 1850 }
resolutionTime: "08:00"
timeZone: "Europe/Prague"
minHoursBetweenResolutions: 12
preElectionDays: 3
seasonsPerTerm: 16
popularity: { min: 0, max: 100, start: 10, drift: 0.05, spread: 0.2 }
campaign: { factor: 4, baseline: 0.5, actionsPerTurn: 2, actionsPerPreElectionDay: 5, crowding: true }
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
            multiplierMin: 0.5, multiplierMax: 1.5, crowding: true, threshold: 0.08 }
senate: { campaignDays: 2, actionsPerCampaignDay: 4, maxHomeFactions: 4, overrideShare: 0.6 }
economy: { start: 1000, growth: 0.01 }
taxRate: 0.20
reserve: { start: 100, interest: 0.03 }
defaultAllocation: { War: 0.25, Interior: 0.15, CommerceLabor: 0.30, Treasury: 0.10, Navy: 0.20 }
favour: { start: 60, baseline: 60, drift: 1, navyFactor: 75, alertBelow: 30 }
threat: { start: 25, alerts: [60, 85] }
discontent: { min: 0, max: 10, start: 3, rest: 3, drift: 0.25, alertAt: 6, growthDrag: 0.01 }
division: { raise: 40, upkeep: 5, baseSides: 12, sidesStep: 2, maxSides: 18, trainCosts: [20, 30, 40] }
gendarmerie: { raise: 20, upkeep: 2.5, baseSides: 8, sidesStep: 2, maxSides: 12, trainCosts: [10, 20] }
crackdown: { cost: 10, reduction: 2, backlash: 1, failurePopularity: 2, dieBase: 4, diePerDiscontent: 1 }
project: { popularity: 2, graft: 0.5 }
startingForces:
  divisions: { aurenten: 2, iracterie: 1, north_arpal: 1, agrinenten: 1, ocidanten: 1 }
  gendarmerie: [aurenten, iracterie, north_arpal]
```

**Time**

| Field | Meaning |
| --- | --- |
| `start.season`, `start.year` | The first turn, the founding pre-election. |
| `resolutionTime` | Daily resolution time, `HH:mm`. |
| `timeZone` | IANA id for `resolutionTime`. |
| `minHoursBetweenResolutions` | Minimum gap before the next resolution. |
| `preElectionDays` | Real days in a pre-election turn. |
| `seasonsPerTerm` | Seasons from one election to the next. |

**Display**

| Field | Meaning |
| --- | --- |
| `display.upEmoji`, `display.downEmoji` | Arrows in effect summaries, as `<:name:id>` or Unicode. |

**Factions and campaigning**

| Field | Meaning |
| --- | --- |
| `popularity.min`, `.max` | Popularity range. |
| `popularity.start` | New factions start here, and popularity drifts back to it. |
| `popularity.drift` | Share of the gap to `start` closed each season. |
| `popularity.spread` | Share of each regional lead or deficit (a province's popularity minus the national one) lost each season, so regional gains spread across the kingdom. 0 keeps them forever, 1 wipes them every season. |
| `campaign.factor` | Popularity scale of a campaign's agreement term; also scales crisis gains and losses. |
| `campaign.baseline` | Popularity a campaign wins per point of salience whatever the province thinks, before crowding: being heard on an issue that matters. |
| `campaign.actionsPerTurn` | Actions per faction in a normal turn. |
| `campaign.actionsPerPreElectionDay` | Actions per faction on each day of the pre-election. |
| `campaign.crowding` | Divide a campaign's gain by √(factions on the same side of the issue in the same province × that faction's own campaigns there). |
| `attack.factor` | Popularity a successful attack takes. |
| `attack.backfire` | Popularity the attacker loses when it backfires. |
| `poll.opinionNoise` | ± noise on polled opinion. |
| `poll.salienceNoise` | ± noise on polled salience. |
| `strongStancePenalty` | Cost of a *Strongly* stance, deducted from its score. |
| `programme.flipPenalty` | Popularity each member loses per flipped stance outside a pre-election. |

**Issues and laws**

| Field | Meaning |
| --- | --- |
| `opinion.crisisShift` | Opinion move from a crisis answer. |
| `salience.min`, `.max` | Salience range. |
| `salience.campaignRaise` | Raise from a campaign, once per issue per season. |
| `salience.crisisRaise` | Raise from a resolved crisis. |
| `salience.decay` | Loss in a season with no raise. |
| `salience.rejectDrop` | Loss when a bill is rejected. |
| `rejectedCooldownSeasons` | Seasons a rejected bill rests before it can be proposed again. |
| `law.sponsorFactor` | Popularity scale for a passed bill's sponsor. |
| `seats` | Starting Assembly size. |

**Government, opposition and the Assembly**

| Field | Meaning |
| --- | --- |
| `government.billsPerSeason` | Government bills the Chancellor can schedule per turn. |
| `opposition.actionsPerSeason` | Opposition actions per faction per season. |
| `opposition.privateBillsPerYear` | How many of those can be bills in a year. |
| `opposition.unansweredQuestion` | Popularity a minister loses for an unanswered question. |
| `opposition.questionReward` | Popularity the asker gains for it. |
| `opposition.condemnReward`, `.condemnPenalty` | Gain and loss from a condemnation. |
| `assembly.noConfidenceShare` | Share of seats needed to bring no confidence to a vote. |
| `assembly.noConfidenceGatherResolutions` | Resolutions to gather those seats. |
| `assembly.noConfidenceCooldownSeasons` | Seasons after a failed motion before another. |

**Elections**

| Field | Meaning |
| --- | --- |
| `election.baseExponent` | Exponent on popularity in party strength. |
| `election.homeMultiplier` | Weight of a faction in its home province. |
| `election.programmeWeight` | How much the programme score moves strength. |
| `election.multiplierMin`, `.multiplierMax` | Clamp on the programme multiplier. |
| `election.crowding` | Weaken stances many parties share. |
| `election.threshold` | Share of the national vote a party needs for Assembly seats, once the Senate is established. |

**Senate**

| Field | Meaning |
| --- | --- |
| `senate.campaignDays` | Real days in a Senate campaign turn. |
| `senate.actionsPerCampaignDay` | Actions per faction on each day of a Senate campaign. |
| `senate.maxHomeFactions` | Most active factions that can call one province home. |
| `senate.overrideShare` | Share of Yes and No votes the Assembly needs to pass a bill the Senate returned. |

**Economy and state**

| Field | Meaning |
| --- | --- |
| `economy.start` | Starting economy. |
| `economy.growth` | Base yearly growth, applied each Winter. |
| `taxRate` | Starting tax rate; Budget = economy × taxRate. |
| `reserve.start`, `reserve.interest` | Starting reserve and its yearly interest. |
| `defaultAllocation` | First-year purse shares; must total 1. |
| `favour.start`, `.baseline` | Starting Favour and its baseline. |
| `favour.drift` | Favour's move toward the baseline each season. |
| `favour.navyFactor` | Favour, and Favour baseline, per unit of change in the Navy share: at 75, moving the Navy from 20% to 15% of the budget costs 3.75 for good. |
| `favour.alertBelow` | GM alert threshold. |
| `threat.start` | Starting Foreign Threat. |
| `threat.alerts` | GM alert thresholds. |
| `discontent.min`, `.max`, `.start` | Range and starting value. |
| `discontent.rest`, `.drift` | Resting value and the move toward it each season. |
| `discontent.alertAt` | GM alert threshold. |
| `discontent.growthDrag` | Growth lost per point of discontent above rest, averaged over the seasons since the last budget. |
| `project.popularity` | Popularity a project's minister gains when it finishes. |
| `project.graft` | Share of spending above a project's pace that is lost. |

**Forces**

| Field | Meaning |
| --- | --- |
| `division.*`, `gendarmerie.*` | `raise` cost, yearly `upkeep`, `baseSides` of a new unit, `sidesStep` per training, `maxSides`, and `trainCosts` per step. |
| `crackdown.cost` | Interior purse cost. |
| `crackdown.reduction` | Discontent removed on success. |
| `crackdown.backlash` | Discontent added on failure. |
| `crackdown.failurePopularity` | Popularity the Interior minister loses on failure. |
| `crackdown.dieBase`, `.diePerDiscontent` | The riot and crackdown die: `dieBase + diePerDiscontent × discontent` sides, rounded, at least 2. |
| `startingForces.divisions` | Divisions per province at the start. |
| `startingForces.gendarmerie` | One corps in each listed province. |

### `config/provinces.yaml`: provinces

Keyed by province id. Copied into the database at game start; after that, only effects change them.

```yaml
north_arpal:
  name: North Arpal
  population: 2.29
  mapKey: "#CCFF00"
  description: Mountain passes and transit corridors to the southern realm.
  conditions: [frontier, crossroads, rugged, coastal]
```

| Field | Required | Meaning |
| --- | --- | --- |
| `name` | yes | Display name. |
| `population` | yes | Millions; only the proportions matter (seats, national opinion, unrest drag). |
| `senateSeats` | yes | The province's seats in the Senate, safe boroughs included. |
| `mapKey` | yes | The province's flat colour in the map's index image. Unique. |
| `description` | yes | One public line, shown in `/info` and autocomplete. |
| `conditions` | no | Starting conditions, ids from `conditions.yaml`. Harbours need `coastal`. |

### `config/conditions.yaml`: conditions

Keyed by condition id. The name and description are public; the numbers are hidden.

```yaml
lawless:
  name: Lawless
  description: Gangs rule the back streets, and the watch looks away.
  biases: { rearmament: 0.1, social_reform: -0.2 }
  discontentRest: 1
  growth: -0.02
```

| Field | Required | Meaning |
| --- | --- | --- |
| `name` | yes | Display name. |
| `description` | no | One public line. |
| `biases` | no | Opinion shift in the province, per issue id. Missing issues get 0. |
| `discontentRest` | no | Added to the province's resting discontent. |
| `growth` | no | Added to the yearly growth, weighted by the province's share of the population. |

**Standing** conditions describe a province and are set in `provinces.yaml`: urban, industrial, farmland,
frontier, anzarian_border, crossroads, rugged, forested, marshland, remote, coastal. **Event**
conditions come and go through effects: lawless, epidemic, striking, famine, booming.

### `config/variables.yaml`: your own variables

Optional. Keyed by variable name (letters, digits and underscores). Effects change them with
`var.{name}`, formulas read them as `var.{name}`, `/gm peek` lists them and `/gm set var.{name}` sets one.
They are secret: no post ever shows them. A name not declared here is rejected on import, which catches
typos.

```yaml
plague:
  start: 0
  min: 0
  max: 10
  description: How far the plague has spread.
coup_stage:
  start: 0
  description: 0 quiet, 1 rumours, 2 plotting, 3 the coup.
```

| Field | Required | Meaning |
| --- | --- | --- |
| `start` | no | Its value until something changes it (default 0). |
| `min` / `max` | no | Clamps every change. |
| `description` | no | A note for you, shown in `/gm peek`. |

### `config/programme.yaml`: programme wording

Optional. The words `/gm party programme` accepts as opinions (see the spec's *Party programmes*). Each
key is a list of phrases, matched ignoring capitalisation and punctuation; a list left out, or the whole
file missing, keeps the built-in defaults. A phrase may appear in only one list.

```yaml
for: [for, support, in favour of, pro, "yes", aye, "+", "+1"]
against: [against, oppose, opposed to, anti, "no", nay, "-", "-1"]
stronglyFor: ["++", "+2"]
stronglyAgainst: ["--", "-2"]
silent: [silent, non-aligned, neutral, no stance, abstain, none, n/a, "0"]
strength: [strongly, very, firmly, fiercely, staunchly, wholly, fully]
filler: [we, we are, the party, stands, it]
```

| Field | Meaning |
| --- | --- |
| `for` / `against` | Phrases for *For* / *Against*. A strength word turns them into the strong stance. |
| `stronglyFor` / `stronglyAgainst` | Phrases that are the strong stance on their own. |
| `silent` | Phrases for no stance: recorded as if the issue were left out. |
| `strength` | Words before or after a For or Against phrase that make it strong. |
| `filler` | Words allowed around an opinion and otherwise ignored (`we are firmly against it`). |

Quote phrases YAML would read as something else: `"yes"`, `"no"`, `"0"`, `"+"`, `"-"`.


### `config/skeletons/<kind>.yaml`: starters for new templates

One file per kind (`issue.yaml`, `bill.yaml`, `project.yaml`, `event.yaml`, `crisis.yaml`), in the
format of that kind above, holding every required field with placeholder values. **New** in
`/content browse` opens a form pre-filled with it (see [Content Workbench.md](Content%20Workbench.md)).
They are read when New is used, never imported as content and never reloaded as config.
