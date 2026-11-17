# Sideplots

Small stories that run between the arcs in [Arcs.md](Arcs.md), from the second term to the fourth
(1854–1865). Each is a short chain: an event that posts a crisis, brings in a bill, or starts a project.
They replace the first-term content, and they give every standing issue fresh bills both ways (the Tariff
Act stays the Against bill on Free Trade, and the Army Bill and the Militia Act stay on Rearmament).

Only Firedamp at Harrow Vale and the three King's Speeches are scheduled. The GM queues every other one
with `/gm event queue`, at the season suggested below or whenever it fits. The arc-tied ones carry a `when`, so if the arc hasn't reached that situation
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

## Firedamp at Harrow Vale

*(extrapolated: the Harrow Vale colliery)* A firedamp explosion kills sixty-one colliers near Iracten.
`harrow_vale_disaster` is **scheduled for Autumn 1853**, so it fires on its own. It makes the **Mining
Protections Bill** (`mining_protections_bill`, Social Reform, For) available and posts the Commerce and
Labor crisis `harrow_vale_inquiry`: pay relief and open a Crown inquiry (20), head a public subscription
(10), or accept the coroner's verdict of accidental death.

The bill is a player's proposal (`suggested-bills/suggested-bills.md`). Its motion keeps all three sections:
ventilation, props and first aid; the owners' share of injury, disablement and widows' pay; quarterly Crown
inspection with fines, closure, and the Crown taking a pit after twelve failed inspections. On passing:
economy −20, Commerce and Labor −15, Iracterie −2, North Arpal −1.

## The King's Speeches

Each new Assembly opens with a speech that puts a slate of small bills on the order paper. They are
scheduled, so they fire on their own. Bills on Integration and Rearmament are left out if that issue has
closed.

| Event | Season | Slate |
| --- | --- | --- |
| `kings_speech_1854`: The King Opens the Second Assembly | Summer 1854 | Adulteration of Food, Corrupt Practices, Limited Liability, Smoke Nuisance, Elementary Education, Union Penny Post, Abolition of Purchase |
| `kings_speech_1858`: The King Opens the Third Assembly | Autumn 1858 (Summer has the Garaztian default) | Provincial Asylums, Polling Places, Standard Measures, Free Libraries, Separate Seals, Army Retrenchment |
| `kings_speech_1862`: The King Opens the Fourth Assembly | Autumn 1862 (Summer has the Iratic expedition) | Vaccination, Members' Qualification, Rates Relief, Master and Servant |

## Small bills

| Bill | Issue, lean | Comes in by | On passing |
| --- | --- | --- | --- |
| `adulteration_act` The Adulteration of Food Act | Social Reform, For | Speech 1854 | economy −5, Aurenten −1, Iracterie −1 |
| `elementary_education_act` The Elementary Education Act | Social Reform, For | Speech 1854 | Interior −10, Aurenten −1, Provencie Sacs +1, starts `elementary_schools` (the Interior chooses where) |
| `asylums_act` The Provincial Asylums Act | Social Reform, For | Speech 1858 | Interior −10, Aurenten −1, starts `provincial_asylum` |
| `vaccination_act` The Vaccination Act | Social Reform, For | Speech 1862 | Interior −5, Aurenten −1, Iracterie −1, Provencie Sacs +1 |
| `vagrancy_act` The Vagrancy Act | Social Reform, Against | `aurelle_vagrants` | Interior +5, economy +5, Aurenten +1 |
| `master_and_servant_act` The Master and Servant Act | Social Reform, Against | Speech 1862 | economy +10, Iracterie +1, Ocidanten +1 |
| `corrupt_practices_act` The Corrupt Practices Act | Electoral Reform, For | Speech 1854 | Interior −5, Aurenten −1, Tovrerie −1 |
| `polling_places_act` The Polling Places Act | Electoral Reform, For | Speech 1858 | Interior −10, Provencie Gares −1, Soralenten −1 |
| `property_qualification_act` The Members' Qualification Act | Electoral Reform, Against | Speech 1862 | Aurenten +1, Iracterie +1, Favour +2 |
| `limited_liability_act` The Limited Liability Act | Free Trade, For | Speech 1854 | economy +12, Aurenten +1 |
| `standard_measures_act` The Standard Measures Act | Free Trade, For | Speech 1858 | economy +8, Commerce and Labor −5, Favour −2 |
| `corn_bounty_act` The Corn Bounty Act | Free Trade, Against | `sacs_corn_glut` | reserve −15, Provencie Sacs −1, Agrinenten −1, Aurenten +1 |
| `union_post_act` The Union Penny Post Act | Integration, For | Speech 1854 | Commerce and Labor −10, economy +5, Favour −2, Agrinenten −1 |
| `separate_seals_act` The Separate Seals Act | Integration, Against | Speech 1858 | Favour +3, Aurenten +1 |
| `free_libraries_act` The Free Libraries Act | Public Works, For | Speech 1858 | Interior −5, Iracterie −1, starts `free_library` |
| `smoke_nuisance_act` The Smoke Nuisance Act | Public Works, For | Speech 1854 | economy −10, Aurenten −1, Iracterie −1 |
| `rates_relief_act` The Rates Relief Act | Public Works, Against | Speech 1862 | Interior +10, economy −5, Provencie Sacs −1, Provencie Gares +1 |
| `purchase_abolition_act` The Abolition of Purchase Act | Rearmament, For | Speech 1854 | War −10, threat −3, Favour −2 |
| `army_retrenchment_act` The Army Retrenchment Act | Rearmament, Against | Speech 1858 | reserve +15, threat +3 |

`aurelle_vagrants` (The Beggars of the Aurelle Bridges) and `sacs_corn_glut` (A Glut of Corn in Provencie
Sacs) are unscheduled; queue them when they fit.

## Small projects

| Project | Purse | Price, term | Effects |
| --- | --- | --- | --- |
| `corn_exchange` (ministerial, farmland) | Commerce and Labor | 25, 2 | economy +6, discontent here −1 |
| `gas_lighting` (ministerial, urban) | Interior | 20, 1 | discontent here −1, economy +3 |
| `canal_cut` (ministerial) | Commerce and Labor | 35, 3 | economy +10 |
| `customs_house` (ministerial, coastal) | Treasury | 20, 2 | reserve +10, economy +3 |
| `military_road` (ministerial, frontier) | War | 30, 2 | threat −3, economy +3 |
| `drill_hall` (ministerial) | War | 20, 2 | threat −2 |
| `harbour_of_refuge` (ministerial, coastal) | Commerce and Labor | 30, 2 | economy +5, discontent here −1, Favour +1 |
| `elementary_schools` (ministerial, once per province) | Interior | 40, 3 | gives the province *Educated* (Electoral Reform +0.2, Social Reform +0.1, Free Trade +0.1, growth +1%), discontent here −1 |
| `model_lodging_house` (ministerial, urban) | Interior | 15, 1 | discontent here −1 |
| `provincial_asylum` The First Public Asylum (from the Asylums Act; built once, the Interior chooses where) | Interior | 30, 2 | discontent there −2 |
| `free_library` The First Free Library (from the Free Libraries Act; built once, the Interior chooses where) | Interior | 15, 1 | discontent there −1 |

## Suggested calendar

Avoiding the election turns (Spring 1858, Spring 1862) and the arcs' big beats (the Diet in Spring 1855,
the King of Garaztia in Summer 1857, the Garaztian default in Summer 1858, Meridia in Summer 1862).
Scheduled events are marked; the rest are for the GM to queue:

| Season | Sideplot |
| --- | --- |
| Autumn 1853 | Firedamp at Harrow Vale (scheduled) |
| Winter 1853 | The Walking Gentlemen Return (scheduled, South Arpal foreshadowing) |
| Summer 1854 | The King Opens the Second Assembly (scheduled); The Member Who Walked Home |
| Autumn 1854 | The Iracten Match Girls |
| Winter 1854 | The Toll Gates of Provencie Sacs |
| Summer 1855 | The Fidacren Wire |
| Autumn 1855 | The Sorales Burial Club Absconds |
| Spring 1856 | The Navvies Go to the Poll |
| Autumn 1856 | A Glut of Corn in Provencie Sacs |
| Winter 1856 | The Beggars of the Aurelle Bridges |
| Autumn 1858 | The King Opens the Third Assembly (scheduled) |
| Winter 1858 | The Fever Ship at Escas |
| Autumn 1859 | The Iron Ship |
| Spring 1860 | The Hulls at Lanten |
| Autumn 1862 | The King Opens the Fourth Assembly (scheduled) |
| Winter 1862 | The Agrines Poacher |

The second term carries most of them because the South Arpal arc leaves the most room around it. The
third and fourth terms are lighter on purpose, while the Garaztian war and Meridia run.

## Open questions for the GM

1. *Answered:* every man votes (the 1848 Constitution), and a reform of the first Assembly gave the vote to
   women of property too. The Electoral Reform issue now says so.
2. If the live game passed any of the scrapped bills (the Ballot Act, the Reform Bill, the Ten Hours Bill,
   the Act of Union and so on), should a new bill acknowledge it, or stay as written?
3. *Answered:* the scrapped templates are still in the live database; `/content prune` removes them all at
   once.
4. Is there a shipyard on the North Arpal shore of the southern bay, as The Iron Ship assumes? Tovrerie is
   the alternative.
5. The invented names: Pellow's match works, Josiah Penn, the Great Lake Telegraph Company, the Sorales
   Mutual Burial Club, the barque Maribel, the Sagtias Turnpike Trust, the Baron of Hesk, Tom Wedder.
