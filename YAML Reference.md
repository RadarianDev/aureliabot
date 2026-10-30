# YAML Reference

Every YAML file the bot reads, with every field. The [spec](Aurelian%20Assembly%20Bot%20Spec.md) is the
source of truth; this file collects its formats in one place.

There are two kinds of file:

- **Config** (`config/`): the rules and the map. Reloaded with `/gm reload`, which changes constants
  only, never values already in play.
- **Content** (`content/`): issues, bills, projects, events and crises, written by the GM. Imported with
  `/content import` (attach the files in Discord) as Drafts, edited with `/content edit`, and marked
  Ready with `/content ready`. `/content export` writes them back out in the same format.

Field names are camelCase. Ids are lowercase slugs (`north_arpal`, `ten_hours_bill`).

## Effects

Bills, projects, events and crisis answers all carry an `effects` map: a key and a change.

```yaml
effects: { economy: -2, discontent.north_arpal: 1, open.labour: true, bill.ten_hours_bill: true }
```

| Key | Value | Meaning |
| --- | --- | --- |
| `economy` | number | National economy (starts at 100). |
| `taxRate` | number | Tax rate, as a fraction (`0.05` is five points). |
| `favour` | number | Anzarian Favour, 0–100. Drifts back to its baseline. |
| `favourBaseline` | number | Where Favour drifts to; a lasting change. |
| `threat` | number | Foreign Threat, 0–100. |
| `reserve` | number | The Treasury reserve. |
| `seats` | number | Assembly size, applied at the next election. |
| `purse.{ministry}` | number | Changes that ministry's current balance at once, not its yearly allocation, and never below 0. `War`, `Interior`, `CommerceLabor` or `Treasury`; never `Navy`. Shows in the ministry's ledger under the bill or event's name. |
| `discontent.{province}` | number | Province discontent, 0–10. |
| `condition.{province}.{condition}` | `1` or `-1` | Gives or removes a condition. |
| `opinion.{issue}` | number | Opinion shift, −1 to 1. Lasting: opinion never drifts. |
| `salience.{issue}` | number | How much the issue matters, 0–5. |
| `popularity.{faction}` | number | A faction's popularity, 0–100. |
| `open.{issueTemplate}` | `true` | Opens a live issue from a Ready template. |
| `close.{issue}` | `true` | Closes a live issue, and withdraws its bills. |
| `bill.{billTemplate}` | `true` | Makes a bill available to propose. Its issue must be open. |
| `project.{projectTemplate}` | `true` | Starts a project. |
| `crisis.{crisisTemplate}` | `true` | Posts a live crisis. **Events only.** |

In a project's effects, `here` stands for the project's province: `discontent.here: -1`.

Every key is validated on import, and an unknown one is rejected. Values are clamped to their range
after each change. In modals and `/gm apply`, effects are written as text instead:
`discontent.north_arpal -2, economy -1, open.public_health`.

## Content

### `content/issues/*.yaml`: issues

One file per issue. An issue is a broad question that parties take stances on. It has no law of its
own; its laws are bills. It enters play through an `open.{id}` effect, and leaves only through a
`close.{id}` effect, which a bill can carry.

```yaml
id: labour
title: The Condition of Labour
for: Protect the workers and their children.
against: Leave industry free to compete.
opinion: 0.2
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
issue: labour
lean: For
motion: Limit factory work to ten hours a day, ban the employment of children under twelve, and send Crown inspectors into every mill and pit.
pass:
  text: Crown inspectors walk into the mills and pits, the children walk out, and the owners swear the kingdom is ruined.
  effects: { economy: -5, purse.CommerceLabor: -2,
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
price: 9
term: 6
effects: { economy: 5, discontent.here: -2, discontent.soralenten: -1 }
bonus:
  by: { season: Spring, year: 1853 }
  text: The first train reaches the coast ahead of the guarantee, and the shareholders cheer the Crown.
  effects: { economy: 2, condition.iracterie.boom: 1 }
```

| Field | Required | Meaning |
| --- | --- | --- |
| `id` | yes | Slug. |
| `title` | yes | Display name. |
| `purse` | no | `War`, `Interior`, `CommerceLabor` or `Treasury`: who pays and runs it. Defaults to `CommerceLabor`. |
| `province` | no | Where it is built; `here` in its effects stands for it. Leave it out for a ministerial project, where the minister chooses. |
| `price` | yes | Total cost. |
| `term` | yes | Seasons it should take. The pace, `price / term`, is the most a season can usefully absorb. |
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
id: carden_strike
title: Strike at the Carden Docks
text: The dockers walk out over wages, and the harbour falls silent.
effects: { discontent.north_arpal: 1, salience.labour: 1,
           condition.north_arpal.strike: 1, crisis.dock_strike_carden: true }
schedule: { season: Autumn, year: 1851 }
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
    effects: { discontent.north_arpal: -2, economy: -1, condition.north_arpal.strike: -1 }
  - lean: Neutral
    label: Commission a royal inquiry
    price: 4
    text: Learned gentlemen take evidence, and the dockers wait.
    effects: { discontent.north_arpal: -1 }
  - lean: Against
    label: Declare the strike unlawful
    text: The strike collapses, and so does trust in Carden.
    effects: { discontent.north_arpal: 2, condition.north_arpal.strike: -1 }
complication:
  text: The money runs out mid-negotiation, and the strike spreads.
  effects: { discontent.north_arpal: 1, discontent.iracterie: 1 }
default:
  text: The strike drags on unanswered, and ships divert to Mugi.
  effects: { economy: -2, discontent.north_arpal: 2 }
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
| `answers[].price` | no | Paid from the ministry's purse. Defaults to 0. |
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
popularity: { min: 0, max: 100, start: 10, drift: 0.05 }
campaign: { factor: 4, actionsPerTurn: 2, crowding: true }
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
| `campaign.factor` | Popularity scale of a campaign; also scales crisis gains and losses. |
| `campaign.actionsPerTurn` | Actions per faction per turn (per day in a pre-election). |
| `campaign.crowding` | Divide a campaign's gain by √(factions on the same side). |
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
| `favour.navyFactor` | Favour per unit of change in the Navy share. |
| `favour.alertBelow` | GM alert threshold. |
| `threat.start` | Starting Foreign Threat. |
| `threat.alerts` | GM alert thresholds. |
| `discontent.min`, `.max`, `.start` | Range and starting value. |
| `discontent.rest`, `.drift` | Resting value and the move toward it each season. |
| `discontent.alertAt` | GM alert threshold. |
| `discontent.growthDrag` | Growth lost per point of discontent above rest. |
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
| `mapKey` | yes | The province's flat colour in the map's index image. Unique. |
| `description` | yes | One public line, shown in `/info` and autocomplete. |
| `conditions` | no | Starting conditions, ids from `conditions.yaml`. Harbours need `coastal`. |

### `config/conditions.yaml`: conditions

Keyed by condition id. The name and description are public; the numbers are hidden.

```yaml
lawless:
  name: Lawless
  description: Gangs rule the back streets, and the watch looks away.
  biases: { army: 0.1, poor_relief: 0.2 }
  discontentRest: 1
  growth: -0.002
```

| Field | Required | Meaning |
| --- | --- | --- |
| `name` | yes | Display name. |
| `description` | no | One public line. |
| `biases` | no | Opinion shift in the province, per issue id. Missing issues get 0. |
| `discontentRest` | no | Added to the province's resting discontent. |
| `growth` | no | Added to the national yearly growth, per province with it. |

**Standing** conditions describe a province and are set in `provinces.yaml`: urban, industrial, farmland,
frontier, anzarian_border, crossroads, rugged, forested, marshland, remote, coastal. **Event**
conditions come and go through effects: lawless, epidemic, strike, famine, boom.

