# The Garaztian War Arc: the Bailiff's War

A draft outline for the GM, now built as content. It runs from the King of Garaztia's visit to Aurelle in
Summer 1857 to one of six endings in 1860–1861. It is the main story of the third term (1858–1862), and its
outcome sets how friendly Arrosia is when the Meridia dispute opens in Summer 1862. The roadmap is in
[Arcs.md](Arcs.md), the world in [Lore.md](Lore.md).

Lore marked *(extrapolated)* is my own invention; change or drop it freely.

## The world

**Garaztia** lies east of Aurelia, across the border of Provencie Gares. A line of strong kings swept away
the charters of its valleys and towns for one law, one tax and one army, and paid for it with loans from
Arrosian merchant houses. It is deep in Arrosian debt and can no longer pay the interest.

**The Arrosian Republic** fights only when it is owed money. Its own creditors once took its crown's
customs, then its crown; now it means to do the same to Garaztia. It sends regiments to seize the
Garaztian customs houses and collect the debt at the border. Arrosia calls it a collection, Garaztia an
invasion.

**Aurelia's stake.** Arrosian troops would stand on Aurelia's doorstep, refugees would cross into Gares,
and Gares has families of the old Garaztian border lords with kin across the border. Aurelian politicians
will see their own Anzarian charters in the valleys' quarrel with their king.

**The Union of Bon** watches nervously, since some of its princes owe Arrosian houses too. It hosts the
peace, as it would any peace.

### Cast

| Who | Role |
| --- | --- |
| King Antso V of Garaztia *(extrapolated name)* | Outgoing, charming, well liked abroad and broke. Visits Aurelle in 1857. |
| Signor Bertran Cascall *(extrapolated)* | The Arrosian envoy in Aurelle. Polite, precise, always taking notes. |
| Colonel-Paymaster Jaume Ferrol *(extrapolated)* | Commands the collection for the creditor houses; rides with ledgers behind his regiments. |
| Baron Ander Urkiola *(extrapolated)* | Head of a Gares border-lord family, an Aurelian subject whose grandfather held lands on both sides. |
| The Prince-Bishop of Hallerau *(extrapolated)* | A Bonnish prince who owes the Arrosian houses money and says so in the Diet. |
| The Duke of Corvasse | Existing character: toasts the valleys if they get their charters back. |

## The machinery

All of it is built: the issue, the variables, the condition and every file below.

**The Garaztian Debt** (`garaztian_question`), opened by the King's visit, or by the default if the visit
was missed.

- For: *Garaztia is our neighbour, and a creditors' army on the Gares border is Aurelia's business.*
- Against: *Garaztia borrowed and Garaztia must pay; Aurelia has no place in another king's ledger.*
- Opinion 0, salience 1. The `refugees` condition adds a bias for it of +0.3 in Provencie Gares.

**Variables** (`config/variables.yaml`):

| Variable | Meaning |
| --- | --- |
| `garaztia_war` | 0 peace, 1 default, 2 ultimatum, 3 Arrosian troops in the customs houses, 4 war in the valleys, 5 Arrosian columns on the Gares border. **3 or more means Arrosia is tied down**; the colonial foothold reads it. Every ending sets it back to 0. |
| `garaztia_goodwill` | −3 bitter to 3 grateful. Decides how the valleys choose, and whether the King holds. |
| `arrosia_goodwill` | −3 hostile to 3 friendly. **Read by the Meridia dispute.** |
| `garaztia_loan` | 1 once Aurelia lent the King his interest in 1857. |
| `valleys` | 0 undecided, 1 rallied to their king, 2 welcomed the Arrosians. |
| `garaztia_ending` | 0 running, then 1–6 as below. Read by later arcs. |

**The `refugees` condition** (Provencie Gares): discontent rests a point higher; leans towards Social Reform,
Rearmament, the Garaztian Debt and the Arpal Question.

## Timeline

Delays count from the season after the effect runs. On the normal path:

| Season | Beat |
| --- | --- |
| Summer 1857 | The King's visit, and his private request. |
| Winter 1857 | Pre-election; the issue is campaign ground. |
| Summer 1858 | Garaztia defaults (Winter 1858 if Aurelia lent him the interest). |
| Winter 1858 | The Arrosian ultimatum. |
| Summer 1859 | The collection: the Arrosians land. |
| Autumn 1859 – Winter 1859 | Refugees in Gares, the border lords arm, nerves in the Diet of Bon. |
| Spring 1860 | The valleys take counsel; Summer 1860, they rally or welcome. |
| Winter 1860 – Summer 1861 | The powers meet at Bon, and the ending. |

With the 1857 loan everything moves two seasons later, ending by Autumn 1861, before the 1861
pre-election and the Meridia dispute.

## Prologue: The King of Garaztia in Aurelle (Summer 1857)

**Event** `garaztian_king_visit` (scheduled). He drives through Aurelle in an open carriage and is cheered at
the opera; between the fish and the ices he asks the Treasury for a loan. Opens the issue and posts:

**Crisis: The King's Private Request** (`garaztian_kings_request`, Treasury, Provencie Gares)

| Answer | Lean | Result |
| --- | --- | --- |
| Lend him the interest from the reserve (price 20) | For | Reserve −40, `garaztia_loan` 1, goodwill +1. The scheduled default is cancelled and queued two seasons later. |
| Introduce him to the Bonnish bankers (price 10) | Neutral | Nothing. |
| Mention the request to the Arrosian envoy | Against | Arrosia +1, Garaztia −1. |
| *Default* | | Garaztia −1. |

## Act I: The Default and the Ultimatum (1858)

**Event: Garaztia Stops Paying** (`garaztian_default`, scheduled Summer 1858). `garaztia_war` 1, salience +1,
threat +3. Makes the three bills available. Queues the ultimatum.

**Event: The Arrosian Ultimatum** (`arrosian_ultimatum`). One season to hand over the customs houses.
`garaztia_war` 2, threat +5. Posts the crisis below and queues the collection.

**Crisis: The Envoy's Invitation** (`arrosian_envoy`, Treasury, Provencie Gares)

| Answer | Lean | Result |
| --- | --- | --- |
| Tell him Aurelia stands by Garaztia | For | Threat +5, Arrosia −1, Garaztia +1. |
| Offer the good offices of the Union of Bon (price 15) | Neutral | Even odds that the collection is put off a season; otherwise threat +2. |
| Accept a retaining fee and listen | Against | Reserve +10, Arrosia +1, Garaztia −1. |
| *Default* | | Threat +3. |

**Bills** (Garaztian Debt)

| Bill | Lean | What it does |
| --- | --- | --- |
| The Garaztian Guarantee (`garaztian_loan`) | For | Reserve −60, economy −10, Garaztia +2, Arrosia −1. Before the troops land (`garaztia_war` < 3) it cancels the ultimatum and the collection and leads to Ending 1, with `prestige.sponsor: 2`. After, it leads to Ending 2. |
| The Escas Convention (`arrosian_convention`) | Against | Escas and the Gares roads lent to the creditors for a share of the customs. Reserve +40, threat −5, Arrosia +2, Garaztia −3, Gares discontent +2. Decides Ending 4 whatever the valleys do. |
| The Neutrality Proclamation (`garaztian_neutrality`) | Against | Threat −3, Arrosia +1, Garaztia −1, Gares +1. Standing aside, formally. |

No bill closes the issue: closing it would cancel the arc's live crises. The endings close it, which also
withdraws whichever bills are left.

## Act II: The Collection (1859)

**Event: The Arrosians Collect** (`the_collection`). Regiments land and seize the customs houses.
`garaztia_war` 3, threat +10, economy −10, Gares +1. Queues the three beats below and the valleys.

**Event: Refugees on the Escas Road** (`refugees_in_gares`): the `refugees` condition on Provencie Gares,
Gares +1. Posts **Crisis: The Gares Refugees** (`gares_refugees`, Interior):

| Answer | Lean | Result |
| --- | --- | --- |
| Open relief camps at Escas (price 20) | For | Gares −1, Garaztia +1. |
| Pay their passage on from Escas (price 15) | Neutral | Removes `refugees`. |
| Close the border with the gendarmerie (price 10) | Against | Gares +2, Arrosia +1, Garaztia −1. |
| *Default* | | Gares +2 and `lawless`. |

**Event: The Border Lords Arm** (`gares_border_lords`). Posts **Crisis: The Volunteers of Urkiola**
(`gares_volunteers`, War):

| Answer | Lean | Result |
| --- | --- | --- |
| Look the other way at the bridges | For | Garaztia +1, Arrosia −1, threat +5. |
| Buy their muskets back (price 15) | Neutral | Gares +1. |
| Arrest the ringleaders (price 10) | Against | Gares +2, Arrosia +1, Garaztia −1. |
| *Default* | | They cross anyway: Arrosia −1, threat +5, Gares +1. |

**Event: Nerves in the Diet of Bon** (`bon_princes_nervous`, `when: "var.garaztia_war >= 3"`): flavour,
threat +2.

## Act III: The Valleys (1860)

**Event: The Valleys Take Counsel** (`valleys_choose`). A weighted roll:

```yaml
- weight: "max(0, 2 + var.garaztia_goodwill)"   # rally to the King
- weight: "max(0, 2 - var.garaztia_goodwill)"   # welcome the Arrosians
```

Even at goodwill 0. At +1 the valleys rally three times in four; at +2 or more they always rally; at −2 or
less they always welcome the Arrosians. A neighbour that stood by the King makes the King worth standing
by. **The GM can decide instead:** cancel the queued `valleys_choose` and queue `valleys_rally` or
`valleys_welcome` by hand.

**Branch A: The Valleys Rise for Their King** (`valleys_rally`). `garaztia_war` 4, `valleys` 1, threat +3.
Posts **Crisis: King Antso's Appeal** (`garaztian_appeal`, War):

| Answer | Lean | Result |
| --- | --- | --- |
| Send arms and officers (price 30) | For | Garaztia +2, Arrosia −2, threat +5. |
| Sell them arms at a fair price | Neutral | Reserve +15, Garaztia +1, Arrosia −1. |
| Refuse, and seal the arsenals | Against | Garaztia −1, Arrosia +1. |
| *Default* | | Garaztia −1. |

**Branch B: The Valleys Open the Passes** (`valleys_welcome`). The Arrosians restore the valleys' charters
for the customs. `garaztia_war` 5, `valleys` 2, threat +10, Gares +1. Posts **Crisis: Arrosian Columns on
the Gares Border** (`arrosian_border`, War):

| Answer | Lean | Result |
| --- | --- | --- |
| Man the border forts (price 30) | For | A division raised in Gares, threat −5, Arrosia −1. |
| Send a colonel to dine with their general (price 10) | Neutral | Threat −3. |
| Sell their quartermasters forage and remounts | Against | Reserve +20, Arrosia +1, Garaztia −1, Gares +1. |
| *Default* | | Threat +5, Gares +1. |

A season later, **A Toast to the Valleys** (`charters_echo`): the Duke of Corvasse drinks to the valleys
"who have their charters back from honest merchants". Favour +3, and Integration salience +1 if that issue
is open. It touches no Integration bill.

## Act IV: The Endings

Both branches queue **The Powers Meet at Bon** (`garaztian_war_ends`) one to three seasons later (one or two
after the welcome). It picks the ending:

1. The Escas Convention is law → Ending 4.
2. Else the valleys welcomed the Arrosians → Ending 5.
3. Else the valleys rallied and Garaztia's goodwill is 2 or more → Ending 6.
4. Else → Ending 3.

Endings 1 and 2 come from the Guarantee instead, and cancel everything still queued. Every ending sets
`garaztia_ending`, sets `garaztia_war` to 0, closes the issue, and, if the refugees are still in Gares,
queues **The Camps at Escas Empty** (`gares_camps_close`) two seasons later, which removes `refugees`.

| # | Ending | Effects |
| --- | --- | --- |
| 1 | **The Grateful Neighbour** (`debt_paid`): the Guarantee arrives before the regiments. | Garaztia +1, threat −5, economy +10, Gares −1. The sponsor's prestige came with the bill. |
| 2 | **The Bailiffs Paid Off** (`bailiffs_paid_off`): Aurelian gold buys back the customs houses. | Reserve −40 more, Garaztia +1, threat −5. |
| 3 | **The Collection Completed** (`garaztia_collected`): Arrosia keeps the customs for thirty years. | Garaztia −1, threat +5, economy −10. |
| 4 | **The Creditors' Partner** (`creditors_partner`): Aurelia shares the customs. | Reserve +30, Garaztia −1, Arrosia +1, Gares +1. |
| 5 | **Garaztia Under the Creditors** (`garaztian_client`): an Arrosian client with an Arrosian garrison on the border. | Garaztia −1, threat +10, Gares +1. |
| 6 | **The King Holds** (`king_holds`): the valleys and Aurelian muskets hold the roads; the houses settle for half. | Garaztia +1, Arrosia −1, threat −5, Gares −1. |

## What the next arcs read

- **The colonial foothold:** `garaztia_war >= 3` (about Summer 1859 to the ending) is the window in which
  Arrosia "can only protest".
- **The Meridia dispute:** `arrosia_goodwill` and `garaztia_ending`. Roughly: the Convention or the creditors'
  side leaves Arrosia friendly (+2 to +3); the Guarantee, arms for the King or the Volunteers leave it
  hostile (−1 to −3); standing aside leaves it near 0.

## The players' levers

| Lever | Effect on the arc |
| --- | --- |
| The 1857 loan | Puts the default off two seasons and starts Garaztia's goodwill. |
| The Guarantee | The only way to stop the collection, cheaply before the landing and dearly after. |
| The Escas Convention | Money and Arrosian friendship; decides the ending. |
| The Neutrality Proclamation | Standing aside on the record. |
| The five crisis answers | Move both goodwills, and so the valleys' choice and the King's fate. |
| Stances on the Garaztian Debt | Campaign ground in the 1857 pre-election, strongest in Provencie Gares. |
| Divisions in Provencie Gares | Raised by manning the forts; there for the GM if the border goes hot. |

## Prestige

Only the in-time Guarantee: `prestige.sponsor: 2`. Nothing else in the arc awards any.

## Building notes

- **Check the live calendar.** The visit is scheduled Summer 1857 and the default Summer 1858; a scheduled
  event can only be marked Ready before its turn starts. If the game is past Summer 1857, queue
  `garaztian_default` by hand; it opens the issue itself.
- **Readiness depth.** The arc's references run about ten templates deep, so the test harnesses that mark
  content Ready now try up to 50 passes instead of 5 or 6.
- Every mid-arc event has `when: "var.garaztia_ending == 0"`, so a copy left queued after an ending skips
  itself.
- Tests: `AureliaBot.Tests/GaraztianWarArcTests.cs` covers the schedule, the loan's postponement, both
  Guarantee endings, the dispatcher's four routes, the Convention and the valleys at the extremes.

## Open questions for the GM

1. The names: King Antso V, Signor Cascall, Colonel-Paymaster Ferrol, Baron Urkiola, the Prince-Bishop of
   Hallerau. Garaztia's capital is never named in the content.
2. Should the valleys' choice stay a roll, or would you rather decide it yourself every time?
3. Ending 5 leaves an Arrosian garrison on the Gares border. Should it become a lasting condition on
   Provencie Gares (a new `garrisoned_border`, say), or stay story?
4. The 1857 loan delays the default by two seasons rather than a year, so the war still ends before the
   Meridia dispute. Is that enough of a reward for lending?
5. Should the Guarantee also be offered again, at a higher price, once the collection has landed? Today the
   same bill covers both cases.
6. Does Anzaria have a voice here beyond the Duke's toast? The Anzarian lords might side with the valleys
   on principle, or with the King, who is one of their kind.
