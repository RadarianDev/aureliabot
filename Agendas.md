# Secret agendas

Each faction secretly wants the kingdom to move a certain way. Agendas are inspired by the secret agendas of
the board game *The King's Dilemma*: the mechanic, not its content.

## The rules

- An **agenda** wants two of the kingdom's tracks to rise or fall over a term.
- Every active faction holds one, privately. It is dealt at the start of each term, and never the same one
  twice in a row (the last agenda a faction held, voided ones included, is left out of the draw).
- **Balanced:** each random deal draws only among the agendas held by the fewest factions, counting the
  agendas the GM has chosen for the next deal. Twelve factions and six agendas give two of each.
- **GM choice:** the GM can choose a faction's agenda. It is held for the faction's next deal, the next
  election or `/gm agendas deal`, or with `now` it replaces the current agenda at once. A GM choice may
  repeat an agenda; the bot warns but allows it.
- On election night, after the returns, every agenda is revealed in #news and scored: a wish is met when its
  track moved the wanted way by more than the track's margin, between the turn the agenda was dealt and the
  election. Each wish met gives `reward.perWish` popularity, and meeting all of them `reward.allBonus` more.
- Then every active faction is dealt its next agenda, sent to its office channel.
- Only Assembly elections count, snap elections included: an early election scores agendas early. Senate
  elections do not.

## Tracks

| Track | Value | Known to players as |
|---|---|---|
| `economy` | the economy | a number |
| `reserve` | the reserve | a number |
| `army` | total sides of every division | a number |
| `discontent` | provinces' discontent, averaged by population | words |
| `favour` | Anzarian Favour | words |
| `threat` | Foreign Threat | hints only |

The reveal shows only whether each track moved the wanted way, never a hidden number.

## Edge cases

- **Dealt in a pre-election turn** (new factions, or the founding pre-election): the agenda is not scored at
  that election. It carries into the new term, measured from the election.
- **Mid-term deals** (`/gm agendas deal`, a new or unretired faction): scored at the next election, from the
  turn of dealing.
- **Retired factions:** their agenda is voided unscored at the next election.
- **An id removed from `config/agendas.yaml`:** hands holding it are voided unscored at the next election, a
  new one is dealt, and the GM is alerted. Rename names and descriptions freely, but not ids.

## Commands

- `/me` shows a player their own agenda.
- `/gm agendas list` shows every agenda with each wish's start, current value and whether it is met so far.
- `/gm agendas deal` deals one to every active faction without one; use it once to start agendas in a running
  game.
- `/gm agendas redeal faction` voids a faction's agenda and deals a different one (or the GM's choice, if one
  is waiting).
- `/gm agendas assign faction agenda [now]` chooses a faction's agenda, for the next deal or at once.
- `/gm agendas cancel faction` drops a choice that is waiting.
- `/gm agendas list` also shows the choices waiting and how many factions hold each agenda.

## Code

`config/agendas.yaml` (format in [YAML Reference.md](YAML%20Reference.md)), `Game/Agendas.cs` (config and
rules), `Game/AgendaWording.cs`, `Database/DatabaseService.Agendas.cs` (the `agendas` table, dealing and
scoring, called from the election in `Resolve`), `Services/AgendaService.cs` (dealing with a private
message), `Commands/Gm/GmModule.Agendas.cs`.
