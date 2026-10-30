# Parties

How factions (players) group into parties, how membership changes, and how Discord roles and chats
follow it. The rules are in [Human Rules.md](Human%20Rules.md) and spec section 6; this file describes
how the code implements them.

Code: [Database/DatabaseService.Factions.cs](Database/DatabaseService.Factions.cs),
[Database/DatabaseService.Parties.cs](Database/DatabaseService.Parties.cs),
[Services/PartySyncService.cs](Services/PartySyncService.cs),
[Commands/PartyModule.cs](Commands/PartyModule.cs) (`/party apply|leave`, join buttons),
[Commands/Gm/GmModule.PartyManage.cs](Commands/Gm/GmModule.PartyManage.cs) (`/gm party create|edit|move|delete`),
[Commands/Gm/GmModule.PartyProgramme.cs](Commands/Gm/GmModule.PartyProgramme.cs) and
[Game/ProgrammeParser.cs](Game/ProgrammeParser.cs) (programmes).

## Factions

A faction is a player, keyed on their Discord user id. It has no name of its own: it is shown as
`<@user_id> (Character Name)` (`Display.Faction`).

| Field | Meaning |
|---|---|
| `user_id` | Primary key: the player |
| `character_name` | The character the GM registered |
| `popularity` | 0–100 (`popularity.min/max`), clamped on every change; starts at `popularity.start` |
| `seats` | Seats held. Only elections change them; joining or leaving a party never does |
| `party_id` | The party; NULL means independent |
| `home_province_id` | The character's safe borough (a province id from `config/provinces.yaml`) |
| `private_channel_id` | The faction's private channel, made by `FactionChannelService` |
| `status` | `Active` or `Retired` |

- Only the GM registers a faction (`/gm faction create`). A player without one is refused with
  "You are not registered yet; ask the GM" (`[RequireFaction]`), and the GM is alerted once per player.
- **Retiring** (`RetireFaction`) makes the faction independent, empties any cabinet post or the
  Chancellorship it holds, cancels a nomination it is part of, and drops its pending actions and join
  applications. Its seats stay with it, vacant, until the next election. A retired player cannot be
  registered again under the same account.

## Parties

| Field | Meaning |
|---|---|
| `id` | Auto-assigned; a lower id is the older party (an election tie-break) |
| `name` | Unique, ignoring capitalisation |
| `colour` | `#RRGGBB`, used for the role, the seat chart and the map |
| `emoji_id` | Optional custom emoji |
| `created_season`, `created_year` | When it was founded |
| `role_id`, `channel_id` | Its Discord role and private chat |
| `programme_message_url` | The message its current programme was recorded from |

A party doesn't store a member list: a party's members are the **active** factions pointing at it.

## Membership

- **Founding:** only `/gm party create`, with two or more founders, each an active independent.
  The GM is warned (not refused) when the colour is close to another party's (CIE76 ΔE below 20).
- **Joining:** `/party apply` by an active independent, only during a pre-election. The application is
  posted in the party chat with Accept and Decline buttons. Only the party's active members may answer,
  each answer can be changed, and the request is decided as soon as more than half of the active
  members accept (joined) or decline (refused). Joining drops the faction's other pending applications.
  Pending applications are dropped when the election runs.
- **Leaving:** `/party leave` (behind a Confirm) at any time; the faction becomes independent and keeps
  its seats.
- **GM move:** `/gm party move` puts a faction into any party, or makes it independent, at any time,
  overriding the pre-election rule and the members' vote.
- **Dissolution:** a party left with fewer than two active members, through leaving, retiring or a GM
  move, is deleted in the same transaction (`DissolveIfUnderTwoTx`). Its last member becomes independent
  and keeps its seats. The result carries the last member, so `PartySyncService.HandleDissolutionAsync`
  can archive the chat, delete the role, announce it in #news and alert the GM.
- **Deletion:** `/gm party delete` (behind a Confirm) makes every member independent, deletes the party
  with its programme and join applications, deletes the role and archives the chat.

## Programmes

- A programme is the party's stance on each open issue, stored as `programme_stances` rows; a missing
  row means no stance (Silent).
- The GM records one from a Discord message link with `/gm party programme`. `ProgrammeParser` reads
  one stance per line, issue and opinion in either order, split by `:`, a spaced dash, `=`, `->`, `→`,
  `|` or a tab, or with no separator when the opinion starts or ends the line. It ignores headers (`#`),
  bullets, quotes, Markdown markers, emoji and punctuation. The issue is an open issue's title or id
  (case, punctuation, a leading *The* and underscores ignored), or whole words found in exactly one
  open issue's title. The opinion is Strongly For, For, Against, Strongly Against or Silent, or an
  alias: the alias, strength and filler lists live in `config/programme.yaml` (`ProgrammeVocabulary`,
  reloaded with the config; built-in defaults when the file is absent). Silent records no row, like
  leaving the issue out. The full rules are in the spec's *Party programmes*.
- Prose lines (no opinion, and not an issue on their own) are skipped and listed to the GM as ignored
  lines in the preview. Any bad line (an opinion with an unknown, closed or ambiguous issue, an issue
  without a valid opinion, or a repeated issue) fails the whole message, and every bad line is listed.
- Recording replaces the whole programme and writes a `Programme` entry to the stance log for each
  member and stance, so a programme is a faction's latest public stance until it campaigns or votes.
- Outside a pre-election, every stance that changes sign costs every active member
  `programme.flipPenalty` popularity, and each flip is announced in #news; the programme goes to
  #campaign-log. During a pre-election it is a manifesto in #news and changes freely.

## Discord roles and chats

The database is the truth; `PartySyncService` makes Discord match it and is idempotent, running after
every membership change, on startup and from the Repair button on the `/gm setup` checklist.

- **Role:** named after the party, in its colour, hoisted. Held by exactly its active members.
- **Chat:** a text channel under the Parties category, visible only to the party role, the GM role and
  the bot; members can pin.
- **Archive:** a deleted or dissolved party's chat moves to the Archive category, read-only for the GM
  role, and its role is deleted.
- **Cabinet role:** `CabinetSyncService` gives it to exactly the sitting cabinet's Chancellor and
  ministers.
- Rejoining the server re-syncs the player's party role and the Cabinet role; leaving it with an active
  faction alerts the GM. Any Discord refusal (usually the bot's role sitting too low) alerts the GM.
