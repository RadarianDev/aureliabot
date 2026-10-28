# Parties

How factions (players) group into parties, and how seats and popularity are tracked.

Code: [Database/DatabaseService.Factions.cs](Database/DatabaseService.Factions.cs),
[Database/DatabaseService.Parties.cs](Database/DatabaseService.Parties.cs). Both tables are created in
`InitializeDatabase()` in [Database/DatabaseService.cs](Database/DatabaseService.cs).

## Factions

A faction is a player. Each Discord user has at most one faction, keyed on their user ID. Factions
have no name; display them as the user mention (`<@user_id>`).

| Field | Type | Default | Meaning |
|---|---|---|---|
| `user_id` | TEXT (Discord snowflake) | | Primary key: the player |
| `popularity` | REAL | 0 | The faction's popularity. No scale is enforced yet. |
| `seats` | INTEGER | 0 | Seats the faction holds |
| `party_id` | INTEGER, nullable | NULL | The faction's party; NULL means independent |

Popularity and seats are set directly with `SetFactionPopularity` and `SetFactionSeats`. Nothing
calculates seats from popularity yet.

Faction lists (`GetAllFactions`, `GetPartyMembers`) are sorted by popularity, highest first, then by
seats.

## Parties

| Field | Type | Meaning |
|---|---|---|
| `id` | INTEGER | Auto-assigned key |
| `name` | TEXT | Party name. It must be unique, and the check ignores capitalisation. |
| `colour` | TEXT | Hex colour (`#RRGGBB`). This is the same format the seat-chart renderer's `PartyData.HexColor` takes. |
| `emoji_id` | TEXT (Discord snowflake), nullable | Optional custom emoji representing the party |

- `CreateParty` returns `null` if the name is already taken.
- `UpdateParty` returns `false` if the new name belongs to another party.

## Membership

A party doesn't store a member list. A faction's membership is its own `party_id`, and a party's
members are every faction pointing at it (`GetPartyMembers`). This gives the following rules:

- **A faction belongs to at most one party.**
- **Joining a party leaves the previous one.** `SetFactionParty(userId, partyId)` overwrites
  `party_id`.
- **Leaving makes the faction independent.** Call `SetFactionParty(userId, null)`.
- **A party can't be joined if it doesn't exist.** The database enforces the foreign key, so
  `SetFactionParty` throws a `SqliteException` for an unknown party ID.
- **Deleting a party keeps its members.** `DeleteParty` turns its factions into independents
  (`ON DELETE SET NULL`); the factions themselves are kept.
- **An empty party still exists.** A party with no members stays until it is deleted.

## Seats

Seats are stored per faction only. A party's seat total is the sum of its members' seats, worked
out when needed; it is never stored, so it can't drift out of sync.

Those totals, together with each party's name and colour, are the input the parliament renderers
need ([Rendering/](Rendering/)). Independents would need their own entries in the chart.
