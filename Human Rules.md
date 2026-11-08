# Rules of Aurelia
## What You Know
- The state is public in numbers: money (the **economy**, **budget**, every ministry's funds, what each ministry spent its money on, and every price), **popularity**, **seats** and the armed forces.
- The mood of the people is told only in words, through **events**, **polls** and province **conditions**: public opinion, salience, **discontent** and **Anzarian Favour**. **Foreign Threat** is only ever hinted at.
- Every **event** lists its effects with UP and DOWN arrows. Numbers are shown with an amount; the people's mood shows only the arrow. Some effects stay secret.
## Turns
- A **turn** lasts one real day and is one season (Spring, Summer, Autumn, Winter). Four turns make a **year**.
- Every turn opens with the news: new **events**, the outcomes of last turn's **crises** and actions, and what the Assembly decided.
- The **pre-election turn** lasts three days and ends in an **election**. A term is four years.
- Once the Senate exists, the **Senate campaign turn**, the Summer of every odd-numbered year, lasts two days and ends in a **Senate election**.
- The game opens with a pre-election turn in Spring 1850. The first issues are announced at the start, so parties can write their programmes before the first election.
## Factions
- Each player is a **faction**, led by a **character** with a **home province**. The GM registers every player, with the character and home province agreed between them. At most four factions can share a home province.
- A faction has **popularity**, and **seats** once elected. Seats belong to the faction, not its party.
- Popularity is kept **per province**. Campaigns and attacks change it only where they happen; everything else (crises, laws, rewards, penalties) moves it everywhere at once. A faction's national popularity is the average of its provinces, each counting by its population.
- A lead or a loss in one province spreads out: every season each province moves part of the way back towards the faction's national popularity.
- Popularity fades: every season it slides a little back towards 10, so standing has to be kept up, and a faction that fell low slowly recovers.
- The GM may reward excellent roleplay, such as a propaganda poster, a well-written manifesto or a stirring speech, with popularity. Rewards are announced publicly.
## Parties
- **Parties** are founded by the GM, at the request of two or more factions.
- A faction can apply to join a party only during a pre-election or Senate campaign turn, and joins with the agreement of a majority of the party's factions.
- Each party otherwise governs itself by its own constitution, written by its members. How it decides is roleplay.
- A faction can leave at any time and keeps its seats. A party left with one faction dissolves.
- Only parties stand in elections. A faction outside a party is an **independent**.
- Each party also has a **foreign affairs** channel for talks with outsiders. Any member can let someone in with `/party foreign add`, take them out with `/party foreign remove`, see who is in with `/party foreign list`, and wipe every message in it with `/party foreign clear`, which gives you a private .txt transcript of what was deleted. Guests can read and write there but nowhere else in the party, and they lose access if the party dissolves.
## The World
- Aurelia has ten **provinces**, each with a population, **discontent** and **conditions**.
- A **condition** is a public fact about a province, such as *Lawless*, *Epidemic*, *Striking* or *Coastal*. Conditions push local opinion, discontent and the economy; by how much is hidden.
- National **variables**:
	- **Economy:** Aurelian production. It grows every year, but restless provinces slow it down, each counting by its population. The **budget** is the economy times a tax rate set by law, paid out once a year.
	- **Anzarian Favour:** how the Anzarian nobles feel about the union, from *content* to *defiant*. Too low risks revolt.
	- **Foreign Threat:** world tension around Aurelia. Too high means war is coming.
## Issues & Programmes
- **Issues** are what the people care about. Each has a **public opinion** (for or against), which conditions shift province by province, and a national **salience** (how much it matters).
- Issues are broad questions, such as Free Trade or the Franchise. They arrive through events and crises, and leave only when an event or a law settles them for good.
- Laws come as **bills**. Each bill is one law on an issue, leaning *For* or *Against* it, so an issue can carry rival bills. New bills arrive through events, crises and other laws.
- Every party keeps a **programme**: a stance on any issues it chooses, from *Strongly For*, *For*, *Against*, *Strongly Against* or *Silent* (non-aligned). Silent, like an issue left off, means no stance.
- A party sends its programme to the GM, who records it.
- In the pre-election turn the programme can change freely. At any other time, flipping a stance between for and against is announced publicly and costs every member popularity.
## Events & Crises
- **Events** are written by the GM and appear at the start of a turn. They change issues, variables and conditions, and can start a **crisis**.
- A **crisis** is tied to an issue and goes to one **minister**. They have until the end of the turn to pick one of its answers.
- An empty ministry's crises and questions go to the **chancellor**.
- Each answer leans *For* or *Against* the issue, or is *Neutral*. A crisis is judged by the people of its province: leaning with them gains popularity, leaning against them costs it, and *Neutral* is the safe but usually expensive way to stall.
- Answers cost money from the minister's ministry. If it can't pay, the answer still happens, with a *complication*.
- No answer lets the crisis run its course, and the minister loses popularity.
## Actions
- Each turn, a faction takes two **actions** (five per day in the pre-election turn, four per day in a Senate campaign turn). Campaigns and attacks happen in one **province**, your home province unless you name another:
	- **Campaign** on an issue: a politician talking about an issue people care about always wins some popularity there, the more the issue matters. On top of that you gain if the province agrees with you, and lose if it doesn't.
	- **Attack** a rival's stance: if the province disagrees with them, they lose popularity there. Otherwise it backfires, and you lose some there.
	- **Poll** an issue: the mood across the kingdom and in every province.
- You can run one campaign and one attack in each province per turn, and one poll per issue. In the pre-election there is no limit on campaigns: run as many as you like, in any provinces, even several on the same issue in the same place.
- A faction campaigns on its party's stance by default. It may take a different one, but that is announced as going **off-programme**.
- A faction's **stance** on an issue is the last one it took in public: its party's programme, a campaign, or a vote once counted (voting Yes on a bill takes the bill's side of the issue, No takes the other). That is the stance rivals can attack. Factions of the same party can't attack each other.
- Each province is judged on its own mood. The more factions campaign on the same side of an issue in the same province in a turn, the less each gains.
- All actions are revealed together when the turn ends.
## Government & Opposition
- A party is in **government** if any of its factions holds a cabinet post, and so is every cabinet member. Every other party is in **opposition**. Independents outside the cabinet are neither.
- Each turn, an opposition faction also gets one **opposition action**:
	- **Question** a minister, who must answer publicly within two turns. An unanswered question costs the minister and rewards the asker.
	- **Condemn** a crisis by publicly backing *For* or *Against* before the turn ends. Being right where the minister was wrong earns popularity; being wrong where the minister was right costs it.
	- **Propose a bill**, once a year.
	- **Whip count** a bill under vote: secretly predict that a cabinet member will break from the government's line (voting against the government's bill, or for an opposition one). It is revealed at the count. Being right earns popularity, being wrong costs it, and if the member abstains or doesn't vote, nothing happens.
## The Assembly
- The *Aurelian Assembly* has 180 seats.
- The **chancellor** sets the government's agenda: each turn they may pick one **bill** to put before the Assembly. With no chancellor, the government proposes no bills. An opposition faction can propose one bill a year as its opposition action.
- A bill proposed during a turn is voted on during the next turn, and counted when that turn ends. Only one bill per issue can be before the Assembly at a time.
- Cabinet and no-confidence votes stay open until the end of the next turn. Each faction votes all its seats one way, and a bill passes when more seats vote for it than against.
- On bills and cabinet votes, a party member who doesn't vote follows the first of the party's factions to vote Yes or No. Voting yourself, even to abstain, always keeps your own vote. Following a vote takes no stance. A vote of no confidence is never followed.
- Votes are **secret** until the count: nobody sees how many seats stand for or against, or who voted how, until the vote is resolved. Then the totals and each faction's vote are published. The one exception is that a member who hasn't voted can see which way their own party is voting so far.
- A passed bill becomes **law**: its effects apply, and its proposer gains popularity if the people back the bill's side of the issue, or loses some if they don't. The issue stays open unless the law settles it.
- Laws can change the rules themselves, from taxes to the number of seats.
- Any faction outside the cabinet, including members of governing parties, can table a **vote of no confidence**. It goes to a vote once its backers hold a sixth of the seats, and lapses if they don't within two turns. It passes with more than half of all seats, and the next turn becomes a pre-election turn. If it is voted down, no new motion can be tabled for a year.
- A rejected bill rests for a year before it can be proposed again. Other bills on the same issue can still be proposed.
## The Senate
- The Senate does not exist until the *Senate Bill* becomes law. Until then, nothing in this section applies.
- The *Senate* has 90 seats, nine for each province. The whole Senate is elected every two years.
- Every bill is voted on by both chambers in the same turn. Each faction casts one vote, counted in each chamber with its seats there.
- A bill the Assembly rejects fails. A bill the Assembly passes and the Senate rejects returns to the Assembly the next turn, and becomes law only if three-fifths of the votes cast are for it.
- Cabinet and no-confidence votes belong to the Assembly alone.
- Every faction in a standing party wins a *safe borough* seat in its home province. The rest of each province's seats are split between parties by how strong each party is there. There is no threshold.
- Seats change only at a Senate election. If it falls on an Assembly election, both are held together.
## Elections
- Assembly seats are counted province by province, by population, and split between parties by how strong each party is there.
- Until the Senate exists, every faction in a standing party also wins a *safe borough* seat in its home province.
- Once the Senate exists, the safe boroughs belong to it, and a party needs at least 8% of the national vote, each province counting by its population, to win any Assembly seats.
- A party's strength in a province is its factions' combined popularity there, weighed by how well its programme fits the local mood: stances on salient issues the province agrees with count for most. A stance shared by many parties counts for less.
- A faction counts for more in its **home province**.
- Inside a party, seats go to factions by popularity.
- Results are posted with a seat chart and a map coloured by the party that won each province: the one holding the most seats there, any safe boroughs included, with the vote breaking a tie.
## Cabinet
- After an election, *His Majesty King Arthas II* (the GM) appoints a **chancellor**, who names a cabinet of four **ministers**: Treasury, War, Interior, and Commerce and Labor. A faction holds at most one post, and the chancellor holds none. The Assembly then votes on the cabinet. If it fails, the chancellor tries again.
- Until a cabinet is confirmed, the previous one stays in office.
- The chancellor and ministers share a private #cabinet chat, which they lose access to when they leave office.
- Anyone in the cabinet can resign at any time. The post stays empty until filled.
- The King's acts are announced as *royal proclamations*.
### Treasury
- Divides the yearly budget between the ministries and the **navy contributions** each Winter, for the year that starts in Spring. If it doesn't, last year's split repeats.
- Raising navy contributions raises Anzarian Favour, cutting them lowers it, and the nobles remember: the change lasts. Other swings in Favour drift back towards the nobles' mood.
- Unallocated money, and money unspent at the end of the year, goes to the *reserve*, which earns interest each year and which the Treasury can grant to any ministry.
### Forces
- The army's **divisions** and the Interior's **gendarmerie** work alike. A unit is a die: raising it is bought once, training it gives the die more sides, and stationing it in another province is free.
- Every unit costs upkeep each year. If its ministry can't pay, its units lose training.
### War
- Keeps the **divisions**. They are what stands between Aurelia and a high Foreign Threat.
### Interior
- Keeps the **gendarmerie**, who hold the provinces against riots.
- A **crackdown** is a gamble in one province: if the gendarmerie prevail, discontent falls and *Lawless* is lifted; if they fail, discontent rises and the minister loses popularity.
### Commerce and Labor
- Runs most **projects**. Some it starts itself; others are ordered by a law or an event, and some of those fall to other ministries.
- A project has a price and a pace: the most it can usefully take each turn. The minister sets its spending each turn. Money spent above the pace is partly lost to **graft**, so rushing costs more. A project that gets no money stalls.
- Finished projects improve the economy and the provinces, and earn their minister popularity. Some carry a bonus for finishing by a deadline.