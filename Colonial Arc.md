# The Colonial Arc: the Southern Trade

A draft outline for the GM. It runs under the second term as a handful of cheap seeds, and becomes a
small arc of its own in the third term (1858–1862), when a chartered company plants the first
Aurelian-Anzarian post in Bhartunia while Arrosia is tied down in Garaztia. It sets up the fourth-term
dispute over Meridia ([Meridia Arc.md](Meridia%20Arc.md)). The roadmap is in [Arcs.md](Arcs.md).

Lore marked *(extrapolated)* is my own invention; change or drop it freely.

## The world

- Meridia is the one continent the Mugi–Arrosia treaty of spheres leaves unclaimed, and
  Aurelia-Anzaria never signed that treaty. Ukulwian cotton reaches the Iracterie mills only through
  Mugi, so the South Arpal tariff war shows Aurelia how much it depends on other powers' colonies.
- **Bhartunia**, on the Makan's western side, faces the southern sea. *(extrapolated)* Its coast is
  ruled by harbour lords who owe the Bhartunian court a loose allegiance and the Makan a looser one.
- *(extrapolated)* **Seravad**, a harbour town at the mouth of the Seravad river, is where the
  Company lands. Its ruler, the **Lord of Seravad**, takes gifts from everyone and promises little.
- *(extrapolated)* **The Southern Company of Aurelia and Anzaria**: the Union's answer to Arrosia's
  Iratic Company. Lanten shipowners, Iracten mill masters and Anzarian naval men hold its shares.
- *(extrapolated)* **Port Valerie**, after Queen Valerie Aureliene: the Company's stockade above the
  harbour of Seravad, if the minister fortifies the post.

### Cast

| Who | Role |
| --- | --- |
| The Lord of Seravad *(extrapolated)* | Harbour lord on the Bhartunian coast, selling his favour to the higher bidder. |
| The Company's factor *(extrapolated, unnamed)* | The man on the spot, writing letters home. |
| The Anzarian admirals | Existing: they want a southern coaling station, and Favour follows their mood. |
| The Arrosian envoy | Shared with the Garaztian war. |

## The machinery

All built: the variables, the issue, and every event, crisis, bill and project below.

| Variable | Meaning |
| --- | --- |
| `colonial_push` 0–6 | How hard the mills, admirals and public push. Set by the seeds, read when the issue opens. |
| `admirals_demand` 0–1 | 1 once the Anzarian fleet sailed against Mugi and held (set by South Arpal's squadron crisis). |
| `colonial_foothold` 0–3 | 0 none, 1 a merchants' post, 2 a treaty post, 3 a fortified post under the flag. Read by Meridia. |
| `coaling_station` 0–1 | 1 once the southern coaling station is finished. Read by Meridia (the GM's battle dice). |

**Issue: The Southern Trade** (`colonies`). For: *Aurelia-Anzaria must have its own cotton, its own
coaling ports and its own flag beyond the seas.* Against: *Colonies are a rich man's adventure, paid
for with the poor man's taxes and the sailor's life.* Opinion −0.1, salience 1, shifted when it opens.

## Second term: the seeds (built)

| Event | When | Effect |
| --- | --- | --- |
| `cotton_famine_petition` | Queued by South Arpal's duty-for-duty answer, the war or the long tariff war, once | `colonial_push` +2, Free Trade salience +1 |
| `voyage_to_bhartunia` | Scheduled Autumn 1856 | `colonial_push` +1, economy +5 |
| `admirals_memorandum` | Queued by the Squadron off Lanten or the Arpal war, once | `colonial_push` +1 (+1 more if `admirals_demand`), Rearmament salience +1 |
| `arrosian_note` | Scheduled Winter 1857 | Threat +2 |

How the South Arpal arc ends sets the push: a Treaty of Bon leaves only the admirals pushing; a long
tariff war or a war brings the petition; a fleet that sailed and held sets `admirals_demand`.

## Third term: the foothold

**Event: The Southern Company Prospectus** (`southern_company_prospectus`, scheduled Autumn 1859,
after the Summer 1859 Senate election and while Arrosia should be tied down in Garaztia). Opens
`colonies`, shifts opinion by `0.05 × colonial_push` and sets salience to `1 + colonial_push / 2`, and
makes both bills available. If `admirals_demand` is 1, queues **The Admirals Press for a Station**.

**Bills**

| Bill | Lean | What it does |
| --- | --- | --- |
| The Southern Company Charter (`southern_company_charter`) | For | Reserve −30, Favour +3, foothold at least 1, `prestige.sponsor` 1. Queues the landing two turns later. |
| The Foreign Ventures Resolution (`foreign_ventures_resolution`) | Against | Reserve +15, Favour −5, Iracterie +1; closes `colonies`. |

**Event: The Company Lands in Bhartunia** (`bhartunian_landing`, `when: issue.colonies.open`). Posts
the crisis below, queues both Arrosian protests (exactly one fires), Bhartunian cotton (three turns)
and Mugian unease (four turns).

**Crisis: The Terms at Seravad** (`bhartunian_terms`, CommerceLabor, Iracterie, Southern Trade)

| Answer | Lean | Result |
| --- | --- | --- |
| Raise the flag and fortify the post (price 30) | For | Foothold 3 (Port Valerie), threat +3, Favour +2, `arrosia_goodwill` −1, `meridia_tension` +1; asks for the station if it hasn't been. |
| Treat with the Bhartunian court (price 20) | Neutral | Foothold 2, economy +5. |
| Keep to a merchants' warehouse | Against | Foothold stays 1. |
| *Default* | | Foothold 1, economy −5. |

**Events that follow**

| Event | Gate | Effect |
| --- | --- | --- |
| The Republic Protests (`arrosian_protest_mild`) | `garaztia_war >= 3` | Threat +2, `arrosia_goodwill` −1. "Arrosia can only protest." |
| A Sharp Note from Arrosia (`arrosian_protest_sharp`) | `garaztia_war < 3` | Threat +8, `arrosia_goodwill` −1, `meridia_tension` +1. |
| Bhartunian Cotton at Iracten (`bhartunian_cotton`) | foothold ≥ 1 | Economy +15 (+10 more at foothold ≥ 2), Iracterie −1. |
| Unease in Anpurgi (`mugian_unease`) | foothold ≥ 2 | Threat +3. |
| The Admirals Press for a Station (`admirals_press_station`) | the station crisis not yet posted | Posts the crisis below. |

**Crisis: The Southern Station** (`southern_station`, War, Ocidanten, Southern Trade): fit out the
station (price 20, Favour +2, starts the project), promise it later (Favour −2), refuse (Favour −6,
baseline −2), default Favour −4.

**Project: The Southern Coaling Station** (`southern_coaling_station`, War purse, price 60, four
seasons). Projects must have a province, so it is fitted out from Ocidanten (`requires: coastal`).
On completion: Favour +4, baseline +2, economy +5, `coaling_station` 1.

## The players' levers

| Lever | Effect |
| --- | --- |
| South Arpal choices | Set `colonial_push` and `admirals_demand`. |
| The Charter or the Resolution | Whether there is a foothold at all. |
| The Terms at Seravad | How deep the foothold goes, and how angry Arrosia is. |
| The Southern Station | Favour, and the fleet's reach in the Meridia dispute. |
| Timing | Charter it while `garaztia_war >= 3` and Arrosia can only protest. |

## Building notes

- Ready order: the protests, cotton, unease and station project before the crises, the crises before
  the landing, the landing before the charter, the charter before the prospectus.
- The prospectus is scheduled; if the live calendar is past Autumn 1859 when this goes in, move it.
- The two protest events are queued together and gated by opposite `when` formulas, so exactly one
  posts and the other is skipped with a GM alert.

## Prestige

Only `prestige.sponsor: 1` on the Charter.

## Open questions for the GM

1. Names: the Southern Company, Seravad, the Lord of Seravad, Port Valerie. Keep or replace?
2. What is Bhartunia's government: a court with harbour lords under it, as drafted, or something else?
3. Should the Foreign Ventures Resolution close the issue for good? The Meridia prologue reopens it.
4. Is Autumn 1859 right for the prospectus, given when the Garaztian collection lands?
