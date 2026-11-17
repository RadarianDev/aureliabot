# The South Arpal Arc: the Tariff War

A draft outline for the GM. It runs about three years, from the election of an anti-Aurelian Tsar in Mugi
in 1855 to one of five endings, one of them a war that can win South Arpal as Aurelia's eleventh
province. It is the main story of the second term (1854–1858), and its outcome is likely to decide the
1858 election. Nothing here is content yet: once the outline is agreed, each
beat becomes an event, crisis, bill or issue file under `content/`.

Lore marked *(extrapolated)* is my own invention; change or drop it freely.

## The world

**Mugi** lies west of Aurelia. It is a colonial power with a strong navy, suzerain over Ukulwia, with a
small exclave in the north of Anzaria. Its capital is **Anpurgi**. It is a **Tsardom-Electorate**: the
nobility elect a Tsar for ten years at a time. Ukulwia is held by a colonial army of mixed Mugian and
Ukulwian ranks under Mugian officers. Ukulwians can hold only a lesser native commission, and the guns are
crewed by Mugians alone.

**The history.** Aurelia, Mugi and the lands around them were once part of the **Bon Empire**, which
survives, reformed by the Act of Union, as today's **Union of Bon**. Aurelia won its independence in a revolt about five hundred years ago, and the treaty that ended
it drew the border through the Arpal country: the north went to Aurelia, the south stayed in the Empire.
Mugi stayed in the Empire longer than Aurelia did, and when it left, it took South Arpal with it. Anzaria
was never conquered by the Empire.

- *(extrapolated)* Mugi has no parties, only great houses. Each Diet is a contest between a few noble
  families, with their cousins, in-laws and clients, over which of their heads will be Tsar. What a house
  wants follows from what it owns. The **Senarstei** and their kin hold the shipping and the cloth mills,
  and fill the **Board of Trade** with their own people, so they want open markets. The **Tamasians** have
  married into the colonial commands for generations and treat the **Admiralty** as a family seat, so
  they want prestige and colonies. A lesser house sides with whichever looks likely to win and pay. The arc
  opens when the Diet crowns a Tamasian.
- *(extrapolated)* Ukulwian cotton feeds the Mugian mills, and Mugian cloth is the largest foreign import
  in the Aurelian markets. Aurelia is Mugi's best customer, which is why the Senarstei and their kin suffer
  first when the two fall out.
- *(extrapolated)* Mugi is Lanten's rival port: ships "divert to Mugi" whenever Lanten falters. Aurelian
  money that runs from the law (Mr Grimble, the absconding railway directors) runs there too.

**South Arpal** is Mugian territory south of North Arpal. Its countryside is Aurelian, villages of the same
speech and families as North Arpal. Its capital, **Kolgasi**, is mostly Mugian: garrison, governor,
merchants and the market every Arpal valley sells into.

- The North Arpal passes lead straight down to Kolgasi, and the trade of both halves of the Arpal
  country runs through its market *(extrapolated)*.
- *(extrapolated)* The history gives the Arpal Question its legal quarrel. Aurelian patriots argue that
  the independence treaty gave South Arpal to the *Empire*, not to Mugi, so Mugi had no right to take it
  when it left. Mugi answers that the treaty is five centuries old and the Empire that signed it has been remade
  into a Union of princes. The Union of Bon, still the Empire in law, is the one power both sides could
  accept as judge.
- *(extrapolated)* The "foreign gentlemen" sketching bridges in the North Arpal passes (the
  `passes_sketchers` event) were Mugian officers from the Kolgasi garrison. Queued a season or two before
  the election, that event foreshadows the arc.

**The Anzarian angle.** Aurelia's Navy purse is a contribution to the Anzarian fleet. Against a strong
Mugian navy, Aurelia's sea defence depends on Anzarian goodwill, so **Favour** matters throughout the arc.
Mugi's northern exclave gives it a door into Anzaria *(extrapolated)*: Mugian gold for the Duke of
Corvasse is a natural side thread.

**The Union of Bon**, the large realm south-east of South Arpal, is the old Empire as reformed by the Act of
Union: an Emperor elected for life with little power, and a Diet of princes. Because it threatens no one,
it is the natural host of any peace conference, and its word on the old treaty carries weight.

### Cast

| Who | Role |
| --- | --- |
| Tsar Gerau I, formerly Admiral Prince Gerau Tamasian | Admiral of the colonial fleet, elected on a promise to make Aurelia pay for Mugi's markets and recover the Arpal valleys' "loyalty". |
| Count Ilaruan Senarstei | Head of House Senarstei and President of the Board of Trade, the defeated candidate; the peace has his family's money behind it. |
| Governor Astrau | Mugian governor of Kolgasi, the new Tsar's man. |
| General Sir Hannibal Strake | Existing character: the Aurelian hawk, commanding on the frontier. |
| The Duke of Corvasse | Existing character: the Anzarian magnate Mugi may court. |

## Why the election, not the Tariff Act

- **Mugi acts first.** The arc arrives as something done to Aurelia, at a date the GM chooses, whatever
  the Assembly has passed. The players' choices decide how it ends, not whether it starts.
- **The Tariff Act becomes a weapon.** It stays broad and well meant (protection for the weavers and the
  ironworks), but once Mugi has struck it is also Aurelia's strongest answer, and the Assembly has to
  decide whether to use it. If it has already passed, Tsar Gerau cites it as his grievance.
- **The squeeze cuts both ways.** Aurelian duties hurt the Mugian merchants, which builds pressure on the
  Tsar to make peace, but they also anger the Tsar's Tamasian kinsmen in the Admiralty. That tension carries the middle
  of the arc.

## The machinery

Built: both variables, the `mugian_quarter` condition, the frontier bias, the Arpal Question issue
(`content/issues/arpal_question.yaml`) and the Tariff Act's conditional pass effects. **The content is
written too:** every event, crisis and bill below is under `content/`, as listed in "Files to write", and
`SouthArpalArcTests` plays its branches. "Content as built" near the end records where the files settle
details the outline left open.

**`var.tariff_war`**, 0 to 6: how far relations have broken down. A new entry in `config/variables.yaml`.

| Value | Meaning |
| --- | --- |
| 0 | Normal trade. |
| 1 | Mugian duties on Aurelian goods. |
| 2 | Kolgasi market closed to Aurelians. |
| 3 | South Arpal agitating, Kolgasi garrison reinforced. |
| 4 | Mugian ships off Lanten, ultimatums. |
| 5–6 | War, or one incident from it. |

**`var.mugi_pressure`**, 0 to 6: how hard the tariff war is hitting the Mugian merchant nobles, and so
how strongly the Electoral Diet pushes the Tsar to make peace. Aurelian duties, a Lanten that holds firm
and a patient Assembly raise it. It decides the pivot in Act III.

**New issue: The Arpal Question** (`arpal_question`), opened in Act II.

- For: *South Arpal is Aurelian land and Aurelian people, and the Crown must stand by them.*
- Against: *South Arpal is Mugi's by treaty, and Aurelia's quarrels are at home.*
- Starting opinion around 0.2, salience 1. The `frontier` condition could add a bias for it of about +0.3.

**Bills**

| Bill | Issue, lean | What it does |
| --- | --- | --- |
| The Tariff Act *(existing)* | Free Trade, Against | Made available by the Tsar's decrees, if it isn't already. Passing it during the arc adds `mugi_pressure` +2 and `tariff_war` +1. |
| The Mugian Commercial Treaty *(new)* | Free Trade, For | Both sides cut their duties. Lowers `tariff_war` sharply; Iracterie weavers lose their protection. |
| The Arpal Guarantee *(new)* | Arpal Question, For | The Crown pledges to protect the Aurelians of South Arpal. North Arpal is delighted; `tariff_war` and threat rise. |
| The Kolgasi Settlement *(new)* | Arpal Question, Against | Aurelia renounces South Arpal for good, and Mugi lifts its duties. Closes the issue; North Arpal is bitter. |
| The War Loan *(new, only in war)* | Rearmament, For | Fills the War purse from the reserve and future taxes. |

The Tariff Act's pass effects get a condition, so it behaves as before outside the arc:

```yaml
  - if: "var.tariff_war >= 1"
    then: { var.mugi_pressure: 2, var.tariff_war: 1 }
```

## Prologue: The Electoral Diet

**Event: The Electoral Diet Chooses** (`mugi_electoral_diet`, `schedule: { season: Spring, year: 1855 }`)

The first Assembly term ends with the 1854 election, so the new Assembly has barely sat before Mugi
moves. Queuing `passes_sketchers` in 1854 foreshadows it during that campaign.

> In Anpurgi the Electoral Diet has chosen its Tsar for the next ten years. Count Senarstei and
> his house are beaten, and Admiral Prince Gerau Tamasian is crowned Gerau I. He tells the
> Diet that Mugi has fed Aurelia's looms for nothing long enough, and that the Arpal valleys will be
> reminded whose they are.

Threat +5. Queues **The Tsar's Decrees** next turn. If the Tariff Act has already passed, the story names
it as the Tsar's grievance and `tariff_war` starts at 1.

## Act I: The Duties (the first year)

**Event: The Tsar's Decrees** (`mugi_decrees`)

> Tsar Gerau's first decrees lay a duty of one part in four on Aurelian coal, iron and corn, and the
> governor of Kolgasi closes the Arpal road to Aurelian carters "pending review".

Economy −20, North Arpal and Iracterie discontent +1, Free Trade salience +1, `tariff_war` +1. Makes the
Tariff Act available. Posts the crisis below.

**Crisis: The Mugian Duties** (CommerceLabor, North Arpal, issue Free Trade)

| Answer | Lean | Result |
| --- | --- | --- |
| Approach Count Senarstei and the Board of Trade (price 20) | For | Makes the Mugian Commercial Treaty available, though the Tsar won't sign it yet. `mugi_pressure` +1. |
| Look away from the mountain paths | Neutral | Arpal carters smuggle round the road. Economy +5; queues **Smugglers in the Passes** in two turns. |
| Answer duty with duty | Against | Duties on Mugian wine, silk and Ukulwian cotton. Reserve +20, economy −15, Iracterie +1 (dearer cotton), `tariff_war` +1, `mugi_pressure` +1; queues **The Kolgasi Market Closes**. |
| *Default* | | Nobody answers. Economy −20; queues **The Kolgasi Market Closes** in two turns. |

**Event: Smugglers in the Passes** (from the smuggling answer)

The road trade goes underground. A small economic gain, but North Arpal gains the `lawless` condition
unless the Interior holds it with gendarmerie (the GM can use a riot roll). It mirrors the existing
Saltmouth smugglers.

**Event: Empty Berths at Lanten** (flavour, if `tariff_war` ≥ 2)

Mugian hulls carry the western trade past Lanten. Ocidanten discontent +1, economy −10.

## Act II: Kolgasi (the second year)

**Event: The Kolgasi Market Closes** (`kolgasi_market_closed`)

> Governor Astrau decrees that only holders of Mugian papers may sell in Kolgasi market. The Aurelian
> villages of South Arpal, whose every sack of wool and cart of cheese goes to Kolgasi, are cut off from
> the only town they have.

Opens **The Arpal Question**, makes the Arpal Guarantee and the Kolgasi Settlement available, North Arpal
discontent +1, `tariff_war` +1. Queues **The Petition of the Arpal Villages** next turn.

**Event: The Petition of the Arpal Villages** (`south_arpal_petition`)

> Thirty thousand marks and signatures, smuggled over the passes in a cheese cart, ask the King of Aurelia
> to take the villages of South Arpal under his protection.

Arpal Question salience +1. Posts the crisis below.

**Crisis: The Arpal Petition** (Interior, North Arpal, issue Arpal Question)

| Answer | Lean | Result |
| --- | --- | --- |
| Receive the petitioners at court (price 10) | For | North Arpal −2, threat +5, `tariff_war` +1; queues **A Note from the Tsar**. |
| Forward the petition to the Mugian envoy | Neutral | North Arpal +1. |
| Turn the petitioners back at the border | Against | North Arpal +2, `tariff_war` −1. |
| *Default* | | The petitioners camp at Carden. North Arpal +2 and `lawless`. |

**Event: A Note from the Tsar** (from receiving the petitioners)

A sharp diplomatic note, and the Kolgasi garrison is doubled. Threat +5, `tariff_war` +1.

**Event: Mugian Gold in Corvasse** (side thread, if `tariff_war` ≥ 2 and Favour < 50)

> A Mugian banker from the northern exclave is seen at Castle Corvasse, and the Duke's retainers have new
> boots.

Favour −5. If the Anzarian Musters crisis hasn't been used yet, it posts it. Mugi's answer to a hostile
Aurelia is to stir the union from inside.

**Event: Grumbling in the Mugian Exchange** (flavour, if `mugi_pressure` ≥ 2)

> Mugian cloth piles up unsold in the Senarstei warehouses, and the Count's cousins and in-laws in the
> Diet ask the Tsar how long Aurelia's custom is to be thrown away.

No effects for players; it tells them the squeeze is working.

## Act III: The Diet's Remonstrance (the turn of the second and third years)

**Event: The Diet Remonstrates** (`mugi_remonstrance`, about a year after the decrees)

> The Senarstei and the lesser houses that trade through their ships send the Tsar a remonstrance from the
> Electoral Diet: Mugi's warehouses are full, her
> ships half empty, and Aurelia still will not bend. The Tsar answers it in person.

The Tsar can't be removed, but he can be forced to the table. A weighted roll, so the players' choices set
the odds without fixing the outcome:

```yaml
effects:
  - random:
      - weight: "max(1, 1 + var.mugi_pressure)"   # the Tsar gives way
        effects: { event.senarstei_mission: true }
      - weight: "max(1, var.tariff_war)"          # the Tsar purges the Senarstei
        effects: { event.squadron_off_lanten: true }
```

At pressure 4 and `tariff_war` 2, the Tsar gives way five times in seven; at pressure 1 and `tariff_war` 4,
he gives way only one time in three. **Pressure** comes from duty for duty, the Tariff Act, patience and a
Lanten that holds firm. **Hostility** comes from the Arpal Guarantee, receiving the petitioners and
anything that looks like a claim on South Arpal. Pure economic war pushes Mugi towards peace; pressing the
claim on South Arpal pushes it towards war.

**Branch A: The Senarstei Mission** (`senarstei_mission`)

> The Tsar sends his beaten rival to the Union of Bon with authority to treat. Count Senarstei offers to
> lift every Mugian duty if Aurelia lifts hers, and asks that agitation in South Arpal cease.

`tariff_war` −1. Posts **Crisis: The Bon Conference** (CommerceLabor):

- **Accept the terms (price 20, For Free Trade):** queues the Commercial Treaty at a better price →
  Ending 1.
- **Ask for market rights for the Arpal villages too (price 30, Neutral):** a 50/50 roll (`chance(0.5)`).
  Success brings Ending 1 with the villages protected; failure means talks break down, `tariff_war` +1.
- **Refuse (Against):** `tariff_war` +2; the Tsar recalls Senarstei and Branch B follows a season later.

**Branch B: The Squadron off Lanten** (`squadron_off_lanten`)

> The Tsar turns the Senarstei men out of the Board of Trade, gives their places to his own cousins, and sends a squadron to cruise off Lanten "in the interests of
> commerce".

Threat +10, `tariff_war` +1. Posts **Crisis: The Squadron off Lanten** (War, Ocidanten, issue
Rearmament):

- **Ask the Anzarian fleet to sail (For).** Only works if Favour is high enough:
  `if: "favour >= 50"` the squadron withdraws, threat −10, `mugi_pressure` +1; otherwise the nobles
  refuse, Favour −5, threat +5. Every Navy contribution of the past years pays off here.
- **Fortify Lanten and Saltmouth (price 30, Neutral):** Ocidanten −1, threat −5.
- **Offer the Kolgasi Settlement (Against):** makes the Settlement available; pass it for Ending 2.
- **Default:** the squadron stops Aurelian ships, economy −30, Ocidanten +2, `tariff_war` +1.

**Event: The Arpal Rising** (if the Arpal Guarantee has passed and `tariff_war` ≥ 3, or the GM's call)

> The villages around Kolgasi refuse the governor's papers and drive out his tax farmers. Mugian dragoons
> ride out from Kolgasi.

Posts **Crisis: The Rising in South Arpal** (War, North Arpal, issue Arpal Question):

- **March to the villages' aid (price 30, For):** `tariff_war` "=6" → Ending 4, the war.
- **Open the border to refugees (price 20, Neutral):** North Arpal +2, Interior purse −10. Refugees
  flood in → Ending 3.
- **Seal the border (Against):** North Arpal +3, the rising is crushed → Ending 3.
- **Default:** the rising is crushed, refugees cross anyway, North Arpal `lawless` → Ending 3.

**Event: The Ultimatum** (if `tariff_war` ≥ 5 at any point)

Mugi demands that the Arpal Guarantee be repealed and the petitioners handed over within a season. The
only way to avoid the war is to pass the Kolgasi Settlement before the next resolution. If that fails →
Ending 4.

## Act IV: The Endings

### Ending 1: The Treaty of Bon (trade and peace)

The Commercial Treaty passes. Both sides cut their duties, the Arpal road reopens, and, if negotiated, the
villages get their market rights. `tariff_war` "=0". Economy and North Arpal recover; the Iracterie
weavers lose the protection the Tariff Act gave them, which seeds the next Social Reform fight. The
Arpal Question stays open but drops in salience, and Tsar Gerau reigns until 1865.

### Ending 2: The Kolgasi Settlement (renunciation)

Aurelia gives up South Arpal for good, and Mugi lifts its duties. The issue closes. North Arpal discontent
rises a lot for a long time, and a later event, **Exiles from Kolgasi**, brings Aurelian families who
won't live under Mugi into North Arpal. A patriotic grievance the next term can use.

### Ending 3: The Rising Crushed

No war, but no peace either. The duties stay, Kolgasi is garrisoned, and North Arpal fills with refugees.
Discontent is high and the province may turn `lawless`. The tariff war goes quiet: `tariff_war` falls by
one a year until the Treaty or the Settlement ends it.

### Ending 4: The Arpal War

Mugi declares war, or Aurelia marches into South Arpal.

**Event: War with Mugi** (`arpal_war`): opens Rearmament if it is closed; makes the Army Bill, the Militia
Act and the War Loan available; threat +20; economy −30; Ocidanten loses its trade
(`condition.ocidanten.striking` stands in for the blockade, or a new `blockaded` condition).

The fighting uses the existing battle roll. Each beat is a GM-queued event followed by
`/gm roll battle`:

1. **The Battle of the Passes.** The Aurelian divisions in North Arpal attack the Kolgasi garrison
   (around `3d12` of ad-hoc dice, plus `2d12` if the Tsar has shipped colonial regiments home from
   Ukulwia, at the cost of stripping that colony of its Mugian officers and guns and leaving its Ukulwian ranks
under their native officers). A defeat means Mugi comes over the
   passes into North Arpal.
2. **The Lanten Blockade.** At sea: the Anzarian fleet sails only if Favour ≥ 50. Without it, economy −20
   each season until peace.
3. **The Siege of Kolgasi** (if the passes were won): a second battle against the reinforced garrison.

**Peace**, at the Union of Bon's offer, after two to four seasons. A war that goes badly for Mugi also
raises `mugi_pressure`, and at 5 or more the Diet forces the Tsar to accept Bon's offer at once:

- **Victory** (Kolgasi taken): by the Treaty of Bon, Mugi cedes South Arpal, Kolgasi included, and it
  becomes **Aurelia's eleventh province** (see below). North Arpal −3, government popularity up, Mugi's
  duties gone. Tsar Gerau is humiliated before the Diet, a grievance for the next arc.
- **Defeat** (the passes lost): an indemnity (reserve −60 or more), Mugian troops in North Arpal until
  it's paid, the Arpal Question closed against, and a likely no-confidence motion.
- **Stalemate**: a white peace; both sides drop their duties out of exhaustion. The economy has paid for
  everything and nobody has won anything.

### The new province: South Arpal

| Field | Draft value |
| --- | --- |
| id / name | `south_arpal` / South Arpal |
| population | about 0.9 million *(extrapolated)*: 0.7 million Aurelian villagers, 0.2 million Mugians, mostly in Kolgasi |
| conditions | `frontier`, `rugged`, `farmland`, plus a new `mugian_quarter` condition |
| discontent | starts high, around 6: the villages celebrate, Kolgasi doesn't |
| description | The Arpal valleys below the passes, Aurelian fields around a Mugian town. |

The new **`mugian_quarter`** condition stands for Kolgasi's Mugian population: discontent rests a point
higher, with a bias against Integration and Rearmament and for Free Trade. Removing it later (by
assimilation, an exodus or a charter for Kolgasi) is material for the next term.

**Aftermath events:** Kolgasi's Mugian merchants and the arrival of the new province's gendarmerie, a
**Kolgasi Charter** bill (Integration, For, giving the town its own council), and the first Assembly
seats for South Arpal at the 1858 election.

**What the bot needs for this.**

1. **Built: the `annex.{province}` effect** (`AnnexEffect` in `Game/EffectKinds/ProvinceEffects.cs`). It
   adds a province from `provinces.yaml` to the running game: its row, its starting `discontent` and its
   conditions. South Arpal is listed there with `annexed: false` and `discontent: 6`, so `/gm start`
   skips it and content can already name it. Effects aimed at it before then are skipped with a GM
   alert. The places that used to read `provinces.yaml` directly (the `/info` list and province page,
   standings' province leaders, Senate seats, province autocomplete and lookups, unit orders and
   `/gm peek popularity`) now go through `ProvincesInPlay`, the live list. Covered by `AnnexTests`.
2. **Map assets:** South Arpal drawn into the base, index and text-overlay PNGs in `assets/map/`, with
   its own index colour. While it isn't part of Aurelia it is drawn as foreign land. **Built:** the new
   set is in `assets/map/south_arpal/` (index colour `#996633`), beside the untouched original set.
   Once `annex.south_arpal` has run, every map render (election night, `/map`, `/gm peek`) uses it.
3. **Checks everywhere provinces are counted:** seat allocation, Senate seats, polls, regional
   popularity, election night (one more province to reveal), `each: province` effects and `/info`. Most
   of these read the live `provinces` table and should just work, but each needs a test.
4. **Lore.md:** the census and the province list.

**In a game that is already running**, none of this blocks the arc. Acts I to III and every other
ending use only effects that exist today, so they can go live as they are. Only the victory peace event
needs `annex.south_arpal`, and with the Diet in Spring 1855 a war can't be won before about 1857. The
feature has to be in by then. Shipping it is an ordinary deploy, and an annexation in, say, 1857 gives
South Arpal its first seats at the 1858 election, because elections read the live province list.

### Ending 5: The Long Tariff War (nobody chooses)

If nothing moves for long enough, the duties stay. A slow bleed: economy −10 a year, smuggling in North
Arpal and Ocidanten, and Mugi's hand in Anzaria. The default when the players ignore the arc.

## The players' levers

| Lever | Effect on the arc |
| --- | --- |
| The Tariff Act | Aurelia's strongest economic answer: raises pressure on the Tsar, and raises the temperature. |
| The CommerceLabor crisis answer | Sets the first steps of `tariff_war` and `mugi_pressure`. |
| The Commercial Treaty / Arpal Guarantee / Kolgasi Settlement | The three ways out, each with its price. |
| Stances on The Arpal Question | Campaign ground, especially in North Arpal. |
| Navy contributions (Favour) | Decide whether the Anzarian fleet sails. |
| Divisions in North Arpal | Decide the Battle of the Passes. |
| Gendarmerie in North Arpal | Keep smuggling and refugees from making it `lawless`. |
| Opposition condemnations | Every crisis in the arc is condemnable. |

## Building the acts

What it takes to turn Acts I to IV into files under `content/`, checked against the content formats in
[YAML Reference.md](YAML%20Reference.md) as of the commit that added South Arpal and prestige.

### Check the live game first

- **The calendar.** The Diet is scheduled for Spring 1855. A scheduled event can only be marked Ready
  before its turn starts, so check the live date and move the schedule if needed.
- **The Tariff Act.** Changing `tariff_act.yaml` sends the template back to Draft on the next deploy, so it
  needs `/content ready tariff_act` again. If the bill is already available or before the Assembly, its
  live copy keeps the old pass effects (bills copy them when made available). If it has already passed,
  the arc starts with `tariff_war` at 1 (the Diet reads it with `law.tariff_act`).
- **Rearmament and Free Trade.** The bills chain onto these issues, and `bill.*` fails while an issue is
  closed. Check both are open, or open them in the beat that needs them.
- **Ready order.** A template can be marked Ready only once every template it references is Ready. Mark
  the arc from its leaves back to the Diet: endings, then Act III, II, I, prologue. The arc has no
  reference cycles, and a template may reference itself.
- **`passes_sketchers`** already exists; queue it during the 1854 campaign as foreshadowing.

### Files to write

| Kind | Ids |
| --- | --- |
| Events | `mugi_electoral_diet`, `mugi_decrees`, `arpal_smugglers`, `lanten_empty_berths`, `kolgasi_market_closed`, `south_arpal_petition`, `tsars_note`, `corvasse_mugian_gold`, `mugian_exchange_grumbles`, `mugi_remonstrance`, `senarstei_mission`, `squadron_off_lanten`, `arpal_rising`, `mugi_ultimatum`, `arpal_war`, `battle_of_the_passes`, `lanten_blockade`, `siege_of_kolgasi`, `treaty_of_bon`, `kolgasi_settlement_signed`, `rising_crushed`, `peace_victory`, `peace_defeat`, `peace_stalemate`, `exiles_from_kolgasi`, `long_tariff_war`, plus the aftermath (`kolgasi_merchants`, `south_arpal_gendarmerie`) |
| Crises | `mugian_duties` (CommerceLabor, North Arpal, Free Trade), `arpal_petition` (Interior, North Arpal, Arpal Question), `bon_conference` (CommerceLabor), `squadron_crisis` (War, Ocidanten, Rearmament), `south_arpal_rising` (War, North Arpal, Arpal Question) |
| Bills | `mugian_commercial_treaty` (Free Trade, For), `arpal_guarantee` (Arpal Question, For), `kolgasi_settlement` (Arpal Question, Against, with `close.arpal_question`), `war_loan` (Rearmament, For), `kolgasi_charter` (Integration, For, after annexation) |
| Issue | `arpal_question`: done |

### What works

- Fixed dates (`schedule`), follow-ups a few turns later (`event.{id}: n`), crises posted by events,
  crisis prices, leans, defaults and complications.
- Thresholds such as "if Favour ≥ 50" as `if` blocks in effects.
- **Flavour that only fits while a situation lasts** (Empty Berths, Grumbling in the Exchange, Mugian Gold
  in Corvasse): give the event `when: "var.tariff_war >= 2"` (or `var.mugi_pressure >= 2`,
  `var.tariff_war >= 2 and favour < 50`). It is checked when the event comes due, and skipped with a GM
  alert if it no longer holds.
- **The Tariff Act as the Tsar's grievance:** two decree events, `mugi_decrees` with
  `when: "not law.tariff_act"` and `mugi_decrees_grievance` with `when: "law.tariff_act"`, both queued by
  the Diet: exactly one fires. The Diet's effects can start `tariff_war` at 1 with
  `if: "law.tariff_act"`.
- **The Remonstrance roll**, exactly as the outline writes it: `random` weights may be formulas,
  `weight: "max(1, 1 + var.mugi_pressure)"` and `weight: "max(1, var.tariff_war)"`.
- **"If the Anzarian Musters haven't been used yet":** `if: "posted.anzarian_musters == 0"`.
- **The Ultimatum's deadline:** the Ultimatum queues `arpal_war` one turn later with
  `when: "issue.arpal_question.open"`. The Kolgasi Settlement closes the issue, so passing it in time
  makes the war skip itself.
- "Lawless unless the Interior holds it": `if: "gendarmerie.north_arpal >= 1"`.
- A 50/50 negotiation: `random` with two equal outcomes.
- Repeating effects (Ending 3's yearly easing, Ending 5's yearly bleed, the blockade's seasonal loss): an
  event that queues itself (`event.long_tariff_war: 4`), stopped by `cancel.long_tariff_war` in whatever
  ends it, or by its own `when`.
- "Two to four seasons until peace": a `random` block choosing between `event.peace_*: 2`, `: 3` and `: 4`.
- The blockade: the `blockaded` condition (discontent rests a point higher, growth slows, leans towards
  Rearmament and Free Trade) on Ocidanten, removed at peace.
- Battles: the GM queues each battle event, runs `/gm roll battle` with the North Arpal divisions against
  dice for the garrison (`3d12`, or `5d12` with the colonial regiments), records the result with
  `/gm set var.passes_held` or `var.kolgasi_taken`, and the peace event reads it.
- Annexation: `annex.south_arpal` in `peace_victory`. The map and the 1858 seats follow on their own.

**One thing stays a content choice:** bills have no price, so "the Commercial Treaty at a better price"
means a second treaty bill with kinder effects (`mugian_commercial_treaty_bon`), made available by the
Bon answer instead of the first.

### Prestige, sparingly

Agendas are the main source of prestige, so the arc awards it only at its two real triumphs, and keeps
the amounts below a single agenda wish (4):

- The Treaty of Bon: `prestige.sponsor: 2` in the Commercial Treaty's pass effects.
- Victory and the annexation of South Arpal: `prestige.chancellor: 2` and `prestige.minister.War: 1` in
  `peace_victory`.

Nothing else in the arc awards it. The Settlement, a defeat or a crushed rising award none; prestige is
never lost, so they simply don't add any.

### Variables the content will want

Add to `config/variables.yaml` alongside `tariff_war` and `mugi_pressure`: `arpal_market_rights` (the
Bon answer won the villages their market rights), `colonial_regiments` (Mugi shipped its Ukulwian
regiments home: +2d12 at the Passes), `passes_held` and `kolgasi_taken` (battle results the GM records
with `/gm set`, so the peace event can choose its ending).

### After the content

- [Lore.md](Lore.md): the census and the province list, once South Arpal is annexed.
- The colonial seeds in [Arcs.md](Arcs.md) hook into this arc: the Cotton Famine Petition after the
  duty-for-duty answer, and the Admirals' Memorandum after the Squadron or the war.

## Content as built

Where the files settle what the outline left open:

- **Timing.** The Diet fires in Spring 1855 and queues both decree events for the next turn; exactly one
  fires. The decrees queue the rest of the arc on a clock: Empty Berths in 2 turns, Mugian Gold in
  Corvasse in 3, the Kolgasi market in 4 (Spring 1856, the second year), the Exchange grumbling in 5, the
  Remonstrance in 6 (winter 1856/57) and the Long Tariff War in 10 (Winter 1857). Duty for duty brings the
  market closure forward to the next turn, the default to two turns. The flavour events and the long war
  skip themselves (`when`) if their situation no longer holds.
- **The Ultimatum at `tariff_war` 5.** Nothing fires on a variable reaching a value, so every beat that
  raises `tariff_war` (the market closure, receiving the petitioners, the Tsar's note, the Arpal
  Guarantee, the failed or refused Bon talks, the Squadron and its default) ends with
  `if: "var.tariff_war >= 5 and posted.mugi_ultimatum == 0 and posted.arpal_war == 0"`. The Tariff Act
  itself does not check, since its file is shared with the rest of the game.
- **The Commercial Treaty needs the Tsar.** Passed while `mugi_pressure` is below 3, the Tsar returns it
  unsealed (`tsar_refuses_treaty`, pressure +1). Since a passed bill cannot be offered again, the way back
  to peace is then the Bon treaty (`mugian_commercial_treaty_bon`, titled *The Treaty of Bon*), which
  always succeeds. Either one queues the ending event `treaty_of_bon`, which carries the economic effects.
- **The Rising** is queued two turns after the Arpal Guarantee passes and fires only if `tariff_war` is 3
  or more then. A rising the GM queues by hand obeys the same rule, so raise `tariff_war` first.
- **The war.** `arpal_war` opens Rearmament if needed, makes the War Loan (and, if never offered, the Army
  Bill and Militia Act) available, blockades Ocidanten, and rolls the colonial regiments one time in three.
  It queues the Battle of the Passes for the next turn and the blockade for the turn after. The battle
  queues the Siege of Kolgasi for the next turn, which only fires if `passes_held` is 1, and the peace talks
  two to four turns later. So the GM's job is: during the Passes turn, `/gm roll battle` and
  `/gm set var.passes_held 1` on a win; during the Siege turn, roll again and `/gm set var.kolgasi_taken 1`
  on a win. The siege raises `mugi_pressure`, and at 5 or more it brings the peace talks forward to the
  next turn. The blockade repeats each season until the Anzarian fleet sails (Favour 50 or more when it
  comes due) or peace is made; the fleet sailing adds pressure but never forces the peace on its own,
  since that could end the war before the Passes are fought.
- **Endings and `south_arpal_ending`.** The peace talks (`arpal_peace_talks`) choose victory, stalemate or
  defeat from the two battle records. Every ending event sets `south_arpal_ending`; the rising crushed sets
  3 and the long tariff war 7, and both stay "unresolved", so the treaty, the Settlement or the war can
  still follow. Treaty, Settlement and peace events cancel every pending beat of the arc.
- **Refugees.** Opening the border, the rising's default and the Exiles give North Arpal the `refugees`
  condition. `arpal_refugees_settle`, eight turns later, clears it; the Treaty of Bon and victory clear it at
  once.
- **Additions to the outline:** `mugi_decrees_grievance`, `tsar_refuses_treaty`, `arpal_duties_ease` (the
  yearly easing after the crushed rising), `arpal_peace_talks` and `arpal_refugees_settle`.
- **The Kolgasi Charter** is made available after victory only if Integration is still open, since a bill
  can't be offered on a closed issue.

## Open questions for the GM

1. What is the independence treaty called? It was signed about five hundred years ago.
2. Should a victory also cost something lasting, say a higher threat baseline from a Mugi that wants
   revenge? Today it only lowers threat.
3. Integration may already be closed by the time South Arpal is annexed. Should the Kolgasi Charter move to
   another issue so it can always be offered?
4. The peace of defeat keeps Mugian troops in North Arpal only in its story. Do you want a mechanical cost
   (a garrison condition, or the indemnity paid in instalments)?
