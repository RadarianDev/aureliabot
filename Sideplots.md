# Sideplots

Small stories that run between the arcs in [Arcs.md](Arcs.md), from the second term to the fourth
(1854–1865). Each is a short chain: an event that posts a crisis, brings in a bill, or starts a project.
They replace the first-term content, and they give every standing issue fresh bills both ways (the Tariff
Act stays the Against bill on Free Trade, and the Army Bill and the Militia Act stay on Rearmament).

None of these events is scheduled. The GM queues each one with `/gm event queue`, at the season suggested
below or whenever it fits. The arc-tied ones carry a `when`, so if the arc hasn't reached that situation
when they come due they skip themselves with a GM alert.

Names of people, firms, ships and places below Lore.md's level are *(extrapolated)*.

## General sideplots

| Event | Leads to | Issue | Province |
| --- | --- | --- | --- |
| `cobbler_member`: The Member Who Walked Home | Bill `payment_of_members_act` (For) | Electoral Reform | Aurenten |
| `iracten_match_works`: The Iracten Match Girls | Crisis `phossy_jaw_inquiry` (Commerce and Labor); bill `factory_inspection_act`, the Dangerous Trades Act (For) | Social Reform | Iracterie |
| `sagtias_toll_gates`: The Toll Gates of Provencie Sacs | Crisis `toll_gate_riots` (Interior); bill `turnpike_trusts_act` (Against) | Public Works | Provencie Sacs |
| `fidacren_telegraph`: The Fidacren Wire | Bill `telegraph_act` (For) | Public Works | Silvicarie |
| `sorales_burial_club`: The Sorales Burial Club Absconds | Crisis `burial_club_losses` (Treasury); bill `friendly_societies_act` (Against) | Social Reform | Soralenten |
| `tovres_navvy_poll`: The Navvies Go to the Poll | Crisis `tovres_poll_inquiry` (Interior); bill `residence_qualification_act` (Against) | Electoral Reform | Tovrerie |
| `escas_cholera_ship`: The Fever Ship at Escas | `epidemic` in Gares; crisis `escas_quarantine` (Commerce and Labor) | Public Works | Provencie Gares |
| `ironclad_trials`: The Iron Ship | Opens Rearmament if closed; crisis `ironclad_contract` (War) | Rearmament | North Arpal |
| `lanten_foreign_hulls`: The Hulls at Lanten | Bill `navigation_acts_repeal` (For) | Free Trade | Ocidanten |
| `agrines_poacher_case`: The Agrines Poacher | Bills `union_excise_act` (For) and `manorial_courts_act` (Against) | Anzarian Integration | Agrinenten |

**The Member Who Walked Home.** *(extrapolated)* Josiah Penn, a cobbler elected by the Aurelle tanneries,
resigns because members are unpaid. The Payment of Members Act pays them from the Treasury purse.

**The Iracten Match Girls.** *(extrapolated)* Pellow's match works poison its girls with white phosphorus
and fine them for complaining. The crisis is the strike; the Dangerous Trades Act bans the poisons and pays
for inspectors.

**The Toll Gates of Provencie Sacs.** Farmers in petticoats break the toll gates on a ruined Crown road.
The Interior mends it, guards it or leases it out. The Turnpike Trusts Act hands every Crown road to
private trusts: the Crown saves money, and the carriers pay.

**The Fidacren Wire.** *(extrapolated)* The Great Lake Telegraph Company sells prices early to the timber
merchants. The Telegraph Act buys out every wire for the Crown. The ministerial `telegraph_line` project
lets ministers extend the network either way.

**The Sorales Burial Club Absconds.** The club's secretary sails for Mugi with the members' pennies. The
Treasury crisis asks whether the Crown makes good the losses. The Friendly Societies Act audits the clubs,
and lets parishes refuse relief to men who join none.

**The Navvies Go to the Poll.** A contractor marches his navvies to the Tovres hustings. The Interior
decides what happens to the result. The Residence Qualification Act takes the vote from anyone who hasn't
lived a year in his parish.

**The Fever Ship at Escas.** *(extrapolated)* The barque Maribel brings cholera into Escas. Commerce and
Labor closes the port and pipes in clean water, quarantines the ship, or leaves it to the merchants. The
ministerial `waterworks` project is the lasting answer anywhere.

**The Iron Ship.** *(extrapolated)* A shipbuilder on the North Arpal shore of the southern bay launches
an ironclad, and offers the War Office a squadron of them for the Anzarian fleet. Buying it pleases the
admirals; refusing sends it to Mugi.

**The Hulls at Lanten.** The Lanten shipowners want foreign hulls allowed in the coasting trade. The Repeal
of the Navigation Acts makes freight cheap, and costs Favour with the Anzarian shipbuilders.

**The Agrines Poacher.** *(extrapolated)* Tom Wedder of Agrines is sentenced to seven years in the
Anzarian fleet by the Baron of Hesk's manor court. Two answers come before the Assembly: the Union Excise
Act (one tax for both realms, nobles' estates included) and the Manorial Courts Act (the lords' courts
recognised in law).

## Arc-tied sideplots

| Event | `when` | Starts | Purse, province |
| --- | --- | --- | --- |
| `arpal_passes_petition`: The Carden Petition for Forts | `var.tariff_war >= 3` | `arpal_pass_forts` (60, 4 seasons): threat −8 | War, North Arpal |
| `lanten_batteries_alarm`: Alarm on the Lanten Quays | `var.tariff_war >= 4` | `lanten_shore_batteries` (40, 2 seasons): threat −5 | War, Ocidanten |
| `gares_refugee_roads`: Work for the Camps | `condition.provencie_gares.refugees or var.garaztia_war >= 3` | `gares_relief_road` (40, 3 seasons): clears `refugees` | Interior, Provencie Gares |
| `kolgasi_road_survey`: The Road to Kolgasi | `var.south_arpal_ending == 4` (victory) | `kolgasi_road` (50, 4 seasons) | Commerce and Labor, South Arpal |

They read the arcs only through variables and conditions, never their templates. Queue each when its arc
is near the situation: the forts once Kolgasi is closed and the villages agitate, the batteries when the
squadron is off Lanten, the relief road a season after refugees reach Gares, the Kolgasi road the season
after the victory peace.

## New projects

| Project | Purse | Price, term | Effects |
| --- | --- | --- | --- |
| `telegraph_line` (ministerial) | Commerce and Labor | 30, 2 | economy +8, discontent here −1 |
| `waterworks` (ministerial) | Interior | 45, 3 | discontent here −2, ends an epidemic there; −1 more if done within 3 seasons |
| `lighthouse` (ministerial, coastal) | Commerce and Labor | 25, 2 | economy +5, discontent here −1 |
| `arpal_pass_forts` | War | 60, 4 | threat −8, North Arpal −1; threat −3 more if done within 4 |
| `lanten_shore_batteries` | War | 40, 2 | threat −5, Ocidanten −1; economy +5 if done within 2 |
| `gares_relief_road` | Interior | 40, 3 | Gares −2, economy +5, removes `refugees` from Gares |
| `kolgasi_road` | Commerce and Labor | 50, 4 | economy +10, South Arpal −2, North Arpal −1 |

## Suggested calendar

Avoiding the election turns (Spring 1858, Spring 1862) and the arcs' big beats (the Diet in Spring 1855,
the King of Garaztia in Summer 1857, the Garaztian default in Summer 1858, Meridia in Summer 1862):

| Season | Sideplot |
| --- | --- |
| Summer 1854 | The Member Who Walked Home |
| Autumn 1854 | The Iracten Match Girls |
| Winter 1854 | The Toll Gates of Provencie Sacs |
| Summer 1855 | The Fidacren Wire |
| Autumn 1855 | The Sorales Burial Club Absconds |
| Spring 1856 | The Navvies Go to the Poll |
| Autumn 1858 | The Fever Ship at Escas |
| Autumn 1859 | The Iron Ship |
| Spring 1860 | The Hulls at Lanten |
| Autumn 1862 | The Agrines Poacher |

The second term carries most of them because the South Arpal arc leaves the most room around it. The
third and fourth terms are lighter on purpose, while the Garaztian war and Meridia run.

## Open questions for the GM

1. The Electoral Reform issue says only men of property vote, aloud at the hustings, but Lore.md says the
   1848 Constitution brought universal male suffrage. Which holds? The Payment of Members and Residence
   Qualification Acts work under either, but their wording assumes a wide franchise.
2. If the live game passed any of the scrapped bills (the Ballot Act, the Reform Bill, the Ten Hours Bill,
   the Act of Union and so on), should a new bill acknowledge it, or stay as written?
3. The scrapped templates are still in the live database; `/content remove` takes out the ones you don't
   want in `/content browse`.
4. Is there a shipyard on the North Arpal shore of the southern bay, as The Iron Ship assumes? Tovrerie is
   the alternative.
5. The invented names: Pellow's match works, Josiah Penn, the Great Lake Telegraph Company, the Sorales
   Mutual Burial Club, the barque Maribel, the Sagtias Turnpike Trust, the Baron of Hesk, Tom Wedder.
