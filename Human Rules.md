# Rules of Aurelia
## Turns
- The game consists of **turns**, which last a real day. 
- One **turn** is an in game season (Spring, Summer, Autumn, Winter).
- A special turn is called the **pre-election turn**, which lasts 3 days.
- A **year** are four **turns**.
- Every **turn** opens with the news of the season: the **events** that happened, the outcomes of **crises**, and what the Assembly decided.
## Factions
- Players form **factions**, which are described by a **character** that leads it.
- A **faction** also has a **popularity**, and **seats** when in parliament.
- Every **character** has a **home province**, where they hold a *safe borough*: as long as their faction stands in an election with a party, it is guaranteed one **seat**.
## Parties
- **Parties** are formed from multiple **factions**.
- Two or more factions can form a party at any time by applying in #party-registry and getting approved by the GM.
- A faction can leave a party at any time, but joining a party can only be done during the **pre-election turn**.
- A party that drops below two factions is not dissolved automatically; the GM decides its fate.
## Assembly
- The *Aurelian Assembly* is the core of polsims made by me.
- After **elections**, each faction is assigned a number of **seats** based on the election algorithm.
- The total seats in the Assembly are 180, but this can be changed through reform.
- A *vote of no confidence* can be held, which declares new **snap elections**: the next turn becomes the **pre-election turn**, and a new 4 year term starts after it.
- **Reforms** are passed like any other law: an issue such as *Electoral Reform* or a new tax, which changes the rules of the game when it passes.
## Variables
- A **variable** is a metric of the game state.
- Variables include:
	- **Economy:** An abstract unit of Aurelian production. **Budget** is derived from economy through a tax coefficient set by law.
	- **Anzarian Favour:** How positively Anzarian nobles views the union with Aurelia. Too low can risk a revolt. *Shown to players only in words, from content to defiant.*
	- **Foreign Threat:** How world tensions apply to Aurelia. Too high is usually a signature of an imminent conflict. *Hidden from players.*
	- **Discontent:** How unhappy are people in Aurelia feeling and how much they riot. *Unique for each province.*
## Provinces & Conditions
- Aurelia has ten **provinces**, each with its own population, **discontent** and **conditions**.
- A **condition** describes the state of a province, such as *Lawless*, *Epidemic*, *Strike* or *Coastal*. Conditions are public, and anyone can look them up.
- Conditions shape **public opinion** in their province, can raise or lower how restless it is, and can help or hurt the **economy**. *How much they do so is hidden.*
- **Events** and **crises** bring conditions and take them away.
- **Crime** is a condition too: a *Lawless* province.
## Issues & The Political Programme
- The game is governed by **issues**, which are topics the common people care about.
- An issue has a **public opinion** and a **salience** metric. Both are hidden to the players, but hinted at through **events**, news and the **conditions** of each province.
- Players never see these as numbers. A **poll** reports them in words, such as *"the Factory Act is a burning question in North Arpal, and the people are strongly for it"*.
- **Public opinion** is global, but is shaped by **conditions** in each province.
- A **party** is tasked with creating a **political programme** that consists of a list of opinions on **issues**. Valid opinions are *Strongly For*, *For*, *Against*, or *Strongly Against*. An issue left off the programme means the party has no stance on it.
- The full **political programme** weighs a party's collective popularity for the election algorithm.
- During the **pre-election turn**, the programme is the party's *manifesto* and can be changed freely. Outside of it, turning a stance around (from *For* to *Against*, or the reverse) is announced publicly and costs every party member some **popularity**.
- An **issue** can enter a game via an **event**, or leave when resolved (usually through a parliament vote or via an **event**).
## Events
- An **event** is piece of lore with effects, which include changing **public opinion** or **salience** of an **issue**, bringing a new **issue**, resolving an existing one, including changes to **variables** or province **conditions**.
- An **event** can also start a **crisis**.
- **Events** spawn at the start of a **turn** and are GM made.
- Every **event** shows a summary of its effects. A variable going up or down is marked with an UP or DOWN arrow. Values players can see show the amount too; hidden ones, such as **public opinion** or **salience**, show only the arrow.
## Crises
- A **crisis** is tied to an **issue** and is assigned to a **minister** who has to resolve it through one from two or more answers.
- **Crises** arrive with **events** at the start of a **turn**, and the minister has until the end of that turn to answer.
- Each answer leans *For* or *Against* the **issue**, or is *Neutral*. Leaning the way the people lean earns the **minister's faction** popularity, leaning against them costs it, and a *Neutral* answer is the safe, usually costly, way to stall.
- Answers have a price, paid from the minister's **ministry**. If the ministry cannot pay, the answer goes ahead with a *complication*.
- A **minister** who does not answer at all lets the crisis run its course, and loses popularity.
- A **crisis** has the potential to shift **public opinion** or **salience** of an **issue**, bring a new one or resolve an existing one, including changes to **variables**, province **conditions**, or the **minister's faction popularity**.
## Campaign
- **Factions** can do one **campaign action** per turn (one per day in the **pre-election turn**):
	- **Campaign** on an issue in a province to gain **popularity**, if the people there agree with you.
	- **Attack** a rival's stance on an issue where the people disagree with them. If they don't, the attack backfires.
	- **Poll** an issue in a province to learn the public mood.
- Factions that are in opposition (members of non-government parties) can also do **opposition actions**, which usually grant popularity at the expense of the government. 
	- Each season, an opposition faction gets one opposition action, either:
		- a **question** to a minister, who has to answer it publicly; an unanswered question costs the minister popularity and rewards the asker, or
		- a **shadow response** to a crisis, saying what they would have done; being right when the minister was wrong earns popularity.
	- Once a year, an opposition faction can also propose a **private bill**.
	- Opposition factions can table a **vote of no confidence**.
- Independent factions outside the cabinet are neither government nor opposition.
## Elections
- An **election** is resolved after the **pre-election turn**.
- The *election algorithm* works as such:
	- The combined **popularity** of all **party factions** is added up to form the *gross party popularity*, which is weighed by the party's **political programme** to form the *net party popularity*.
	- The *net party popularity* decides the distribution of seats per party. For a party, the **popularity** of each **faction** decides the distribution of **seats** per **faction**.
	- The count is done province by province: a faction is stronger in its **home province**, and a programme counts for more where the local mood agrees with it.
	- Every standing faction also keeps its *safe borough* seat.
- The results come with a seat chart of the Assembly and a map coloured by the party that won each province.
## Government
- After an **election**, *His Majesty King Arthas II* (the GM) will appoint a **character** to become a **chancellor**.
- A **chancellor** is tasked with building a *cabinet of ministers*, which will later receive a confidence vote from the Assembly to begin their **4 year term**.
- Until the Assembly votes, the previous **chancellor** and his *cabinet* can execute government power.
- The **government** receives a new income to the **budget** every **year**.
- **Ministers** and **chancellor** can propose laws to the **Assembly.**
- His Majesty's acts, such as appointing a **chancellor** or summoning a new Assembly, are announced as *royal proclamations*.
### Minister of Treasury
- Tasked with dividing the **budget** among the **ministries** and the **navy contributions**.
- Increasing **navy contributions** increases **Anzarian Favour**, lowering contributions decreases it. Keeping them the same changes nothing, and Favour slowly settles towards the mood of the Anzarian nobles.
- Money left unallocated, or unspent at the end of the year, goes to the *reserve*, which the Treasury can grant to any ministry.
### Minister of War
- Tasked with maintaining the **Army**. Spends his part of the budget on division upkeep, purchasing equipment and training.
### Minister of Interior
- Tasked with maintaining **Order**. Situates **gendarmerie** divisions in provinces, cracks down on **discontent** and tackles **crime**.
- A **crackdown** is a gamble: the gendarmerie in a province face the crowd. If they prevail, **discontent** falls and a *Lawless* province is brought to order. If they fail, discontent rises and the minister's faction loses popularity.
- The gendarmerie's upkeep is paid from the Interior's part of the budget.
- The Interior can also place **informants** in provinces, who report the local mood, but may be discovered.
### Minister of Commerce and Labor
- Tasked with **projects**, which usually take a various time to complete. Spends his part of the **budget** on pushing the progress of projects.
- A project is paid for season by season. If the ministry cannot pay, the project stalls.
- Finishing **projects** usually has a positive effect on **variables** and **issues**.