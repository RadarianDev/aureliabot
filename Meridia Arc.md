# The Meridia Arc: the Dispute over the Southern Coast

A draft outline for the GM. It is the main story of the fourth term, opening in Summer 1862, the first
turn after the 1862 election, and running to about 1865. The Iratic Company, free of Garaztia, sails for
the Bhartunian coast where the Southern Company already trades ([Colonial Arc.md](Colonial%20Arc.md)),
and Arrosia invites Mugi to divide Meridia between them, leaving Aurelia-Anzaria out. It reads the
three earlier arcs only through variables. The roadmap is in [Arcs.md](Arcs.md).

Lore marked *(extrapolated)* is my own invention; change or drop it freely.

## The world

- Meridia is the one continent the Mugi–Arrosia treaty leaves unclaimed. Arrosia, making up for
  Lurberria, wants it. Mugi is glad to see Arrosia challenged, but wary of a colonial Aurelia-Anzaria.
- **The Makan**, the great power of the continent, *(extrapolated)* tolerates foreign posts on the
  Bhartunian coast as long as they pay and don't quarrel. Its envoy watches the bidding at Seravad.
- **The Union of Bon** again hosts the conference, since it threatens no one.
- *(extrapolated)* The Iratic Company's post sits up the coast from Seravad; the dividing line on
  offer is the **Seravad river**, north to Aurelia-Anzaria, south to Arrosia.

### What earlier arcs hand over

| Variable | Read as |
| --- | --- |
| `colonial_foothold` | What the Company holds at Seravad. Raises the starting tension, wins the conference. |
| `arrosia_goodwill` | From the Garaztian war. Lowers the starting tension, pays damages for the Constance. |
| `south_arpal_ending` | Mugi's mood: a Treaty of Bon (1) makes it friendlier, a victory (4) or a long tariff war (7) hostile. |
| `coaling_station`, Favour | Whether the Anzarian fleet can and will sail south. |

## The machinery

All built. New variables: `mugi_stance` (−1 backs Arrosia, 0 aside, 1 backs Aurelia), `sea_battle_won`
(the GM sets it after the battle), `meridia_ending` (0 running, 1 Partition of Bon, 2 Arrosian Meridia,
3 war won, 4 war lost, 5 renunciation). `meridia_tension`, 0–6, already existed; 5 or more at the
conference means war.

Every event after the prologue has `when: "var.meridia_ending == 0"` (and most also
`issue.colonies.open`), so whichever ending comes first stops the rest. Endings never `cancel.`
anything: that would make reference cycles, which readiness refuses.

**Bills on The Southern Trade**, made available by the Arrosian note:

| Bill | Lean | What it does |
| --- | --- | --- |
| The Bhartunian Coast Declaration (`meridian_claim`) | For | Reserve −20, threat +10, Favour +3, tension +2, `arrosia_goodwill` −1, foothold at least 1. |
| The Meridian Accession (`meridian_renunciation`) | Against | Accedes to the Mugi–Arrosia treaty. Favour −8, `arrosia_goodwill` +2, tension 0, closes the issue → Ending 5. |

## Prologue: The Iratic Company Sails South

`iratic_expedition`, scheduled Summer 1862. Reopens `colonies` if it was closed, threat +5, salience
+2, and sets `meridia_tension = clamp(tension + foothold + 1 − arrosia_goodwill, 0, 4)`. Queues the
note two turns later.

## Act I: The Note and the Court

**Event: The Treaty Is to Be Extended** (`meridian_treaty_note`). Tension +1, both bills, posts the
crisis below; queues **A Second Flag at Seravad** (two turns), **Anpurgi Considers Meridia** (four)
and **The Emperor Offers Bon** (eight).

**Crisis: The Arrosian Note on Meridia** (`meridian_note`, Interior, Iracterie)

| Answer | Lean | Result |
| --- | --- | --- |
| Demand a seat at the table (price 10) | For | Tension +1, `arrosia_goodwill` −1. |
| Ask the Union of Bon to call a conference (price 15) | Neutral | The conference comes four turns from now instead of later. |
| Let the note lie unanswered | Against | Tension −1, `arrosia_goodwill` +1. |
| *Default* | | Tension +1. |

**Event: A Second Flag at Seravad** (`rival_posts`): tension +1, posts **The Court of Seravad**
(`court_of_seravad`, CommerceLabor): outbid the Iratic Company (price 30, foothold at least 2, tension
+1, goodwill −1); offer the Makan a share of the customs (price 20, tension −1, economy −5); refuse to
bid (foothold −1, tension −1); default foothold −1, tension +1. Queues the incident.

**Event: The Constance Is Stopped** (`incident_at_sea`, only if tension ≥ 2): posts **The Seravad
Incident** (`seravad_incident`, War, Ocidanten): ask the Anzarian squadron to sail (with Favour ≥ 50:
threat −5, tension +1, goodwill −1; otherwise Favour −5, threat +5); demand damages (price 10: reserve
+10 if `arrosia_goodwill ≥ 0`, else tension +1); let it drop (tension −1, Ocidanten +1).

## Act II: Mugi's Choice

**Event: Anpurgi Considers Meridia** (`mugi_chooses`), a formula-weighted roll:

| Outcome | Weight |
| --- | --- |
| Mugi Declines the Invitation (`mugi_backs_aurelia`): stance 1, threat −5, tension −1 | 1, +3 after a Treaty of Bon, +1 after the Settlement, −1 if the post is fortified |
| Mugi Signs with Arrosia (`mugi_backs_arrosia`): stance −1, threat +10, tension +1 | 1, +2 after a victory or a long tariff war, +1 after a defeat, +1 if the post is fortified |
| Mugi Waits (`mugi_stands_aside`): stance 0 | 2 |

Each weight is at least 1. The GM can override by cancelling the queued result and queuing another.

## Act III: Bon

**Event: The Emperor Offers Bon** (`bon_meridia_offer`): if tension is 5 or more, war instead
(Ending 3/4); otherwise posts **The Conference of Bon on Meridia** (`meridia_conference`, Treasury):

| Answer | Lean | Result |
| --- | --- | --- |
| Hold out for the whole coast (price 20) | For | A roll: weight `1 + foothold + mugi_stance + (Favour ≥ 50)` for the Partition; weight `2 + tension / 2 − mugi_stance` for a breakdown: tension +2, then war if it reaches 5, else an Arrosian Meridia. |
| Accept a line at the Seravad river (price 10) | Neutral | The Partition if the foothold is 2 or more, else an Arrosian Meridia. |
| Sell the Company's claims to Arrosia | Against | Reserve +30, goodwill +2, an Arrosian Meridia. |
| *Default* | | An Arrosian Meridia. |

## The war

**Event: War in the Southern Sea** (`meridian_war`): threat +20, economy −20, tension 6, salience up.
Without Favour ≥ 50 the Anzarian fleet stays home and Ocidanten is `blockaded`, with **Arrosian Sails
off Lanten** (`meridian_blockade_bites`) costing economy −15 and Ocidanten +1 every season until peace.
Queues **The Squadrons off Seravad** next turn and **Bon Offers Peace** three to five turns later.

**The battle.** The GM runs `/gm roll battle` with ad-hoc dice: the Anzarian squadron `3d12`, +`1d12`
with the coaling station, +`2d12` if Favour ≥ 50; the Iratic Company `3d12`, +`2d12` if Mugi backs
Arrosia. A win is recorded with `/gm set var.sea_battle_won 1`.

**Bon Offers Peace** (`meridian_peace`): a won battle → Victory; otherwise, if Favour ≥ 50 (the fleet
held its own) → an Arrosian Meridia; otherwise → Defeat.

## The endings

| Ending | Event | Effects |
| --- | --- | --- |
| 1. The Partition of Bon | `partition_of_bon` | North of the river is Aurelia-Anzaria's sphere. Foothold at least 2, economy +20, Favour +5, Iracterie −1, goodwill +1, `prestige.chancellor` 2. |
| 2. An Arrosian Meridia | `arrosian_meridia` | Foothold at most 1, a warehouse on sufferance. Favour −5, threat +5. |
| 3. The Peace of Seravad (war won) | `meridian_victory` | The whole coast. Foothold 3, economy +10, Favour +8, threat −10, goodwill −1, `prestige.chancellor` 2. |
| 4. The Loss of Port Valerie (war lost) | `meridian_defeat` | Foothold 0, reserve −50, Favour −5, threat +10, government popularity −3. |
| 5. The Southern Flag Comes Down | `meridia_renounced` | From the Accession. Foothold 0. |

Every ending sets `meridia_ending`, lifts the blockade, and closes `colonies` if it is open, which
withdraws the remaining bills.

## The players' levers

| Lever | Effect |
| --- | --- |
| The third-term foothold | Starting tension and the conference odds. |
| The Garaztian war (`arrosia_goodwill`) | Starting tension and damages. |
| The South Arpal ending | Mugi's choice. |
| Navy contributions (Favour) and the station | Whether the fleet sails, the battle dice, the peace. |
| The Declaration or the Accession | Raise the stakes, or walk away. |
| Every crisis | Condemnable by the opposition. |

## Building notes

- Ready order: endings first, then the peace, the war and its battle and blockade, the conference
  crisis, the offer, Mugi's three results then the roll, the crises, the note, the prologue.
- The prologue is scheduled for Summer 1862; check the live calendar first.
- No template depends on another arc's templates; only on variables, Favour and the issue.

## Prestige

`prestige.chancellor: 2` at the Partition or the war's victory, which can't both happen.

## Open questions for the GM

1. Who rules Bhartunia, and how does the Makan treat foreigners on its coasts?
2. Should Saghiria or the Seven Kingdoms come into the dispute?
3. Is a short naval war with Arrosia acceptable, or should the dispute stay diplomatic?
4. Should Ending 2 (an Arrosian Meridia) also follow a drawn war, as drafted, or be its own ending?
5. Does the Meridia arc close the Southern Trade for good, or should a fifth term reopen it?
