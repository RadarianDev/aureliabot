# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

AureliaBot is a Discord bot that helps a GM run a political simulation. It uses Discord.Net
(Interactions framework) for slash commands, Microsoft.Data.Sqlite for persistence and SkiaSharp
for parliament seat-chart images, targeting .NET 10.

`DiscordBotOne/` (SundamarBot, an earlier nation-state economy bot) and `DiscordBotTwo/` (a
standalone parliament-chart renderer) are **reference copies only**. The root project's core was
copied from them; they are excluded from compilation by the `Compile/EmbeddedResource/None Remove`
group in [AureliaBot.csproj](AureliaBot.csproj) (they live inside the project directory, so the
SDK's default recursive glob would otherwise compile them in). Don't edit them. Mine them for
patterns: `DiscordBotOne/CLAUDE.md` documents a much larger bot built on the same skeleton (turn
advancement, test fixture, help embeds).

## Conventions

Code should be self-explanatory: avoid comments, and rely on clear naming and structure instead.

**Every text the bot sends lives in [config/texts.yaml](config/texts.yaml)**, never as a literal in code:
posts, replies, refusals, alerts, button labels, modal labels, poll and summary wording. Code calls
`Texts.Get("section.key", ("name", value), ...)` with a literal key; the file holds the wording with
`{name}` placeholders, grouped by section (`party.apply.posted`). Logic stays in code: choose between
keys for different cases (singular and plural, success and failure) rather than putting conditions in
the text. Numbers and names are formatted in code (`Display.*`) and passed in as values. The file is
reloaded with the rest of `config/`, so wording changes need no rebuild. `TextsTests` fails if code uses
a key the file lacks, or the file has a key no code uses. Exempt: console logs, `LogEntry` payloads,
exception messages meant for developers, and slash command and option descriptions (Discord reads
those attributes at registration).

**Every post that carries effects** (events, laws, crisis outcomes, finished projects, public
`/gm apply`, and their `/content preview`) is built with `Publisher.FormatEffectPost(heading,
preamble, story, effectLines)`: a heading, plain preamble lines, the story in italics, then
**-- EFFECTS --** and one bullet per `EffectSummary` line. Never assemble such a post by hand.
Document large mechanics and features in dedicated `.md` files rather than in code comments.
Existing mechanic docs:
- [Human Rules.md](Human%20Rules.md): the player-facing rules, written by the GM. They win over
  every other doc; where the spec contradicts them, fix the spec.
- [Aurelian Assembly Bot Spec.md](Aurelian%20Assembly%20Bot%20Spec.md): the full game spec the bot is
  being built towards. Where it differs from the current code, the spec is the target.
- [Implementation.md](Implementation.md): code layout, build stages and how to split work across
  subagents, for building the spec.
- [Parties.md](Parties.md): factions, parties, membership and seats, as currently implemented.
- [YAML Reference.md](YAML%20Reference.md): every config and content YAML format with all its fields,
  collected from the spec. Keep it in step when a format changes.

`SQLitePCLRaw.bundle_e_sqlite3` is pinned to 2.1.12 in the csproj on purpose. It overrides the
2.1.11 that `Microsoft.Data.Sqlite` pulls in transitively, whose bundled SQLite is vulnerable to
CVE-2025-6965. Keep the pin until `Microsoft.Data.Sqlite` itself brings in 2.1.12 or newer.

## Commands

```
dotnet build    # build (run from repo root)
dotnet run      # run the bot; needs a token, see Configuration
```

No test project or linter exists yet. If adding tests, follow `DiscordBotOne/SundamarBot.Tests`: an
xUnit project in a subfolder (add it to the csproj's `Remove` globs, and add `InternalsVisibleTo`)
with a fixture that builds `DatabaseService` through its `internal` raw-connection-string
constructor against a temp SQLite file.

## Configuration

`BotConfig.Load()` reads, in increasing priority: `appsettings.json` → `appsettings.Local.json`
(gitignored) → environment variables.

- `Discord:Token`, or the `DISCORD_BOT_TOKEN` env var. Keep it in `appsettings.Local.json`.
- `Discord:GuildId`: if set, commands register to that guild (instant); if empty, they register
  globally (slow propagation).
- `Database:Path`: SQLite file, default `aurelia.db` (gitignored).
- `Game:Root`: the folder holding `config/` and `content/`. If empty, the bot walks up from its
  output folder to the repo (the folder with `AureliaBot.csproj`), so it reads the files you edit, not
  the build copy. Both folders are loaded on startup and watched while the bot runs (see
  [YAML Reference.md](YAML%20Reference.md)).

## Architecture

**Startup / dispatch ([Program.cs](Program.cs), [Bot.cs](Bot.cs))**
- `Program.cs` pins the default thread culture to invariant first thing. The dev machine's locale is
  Czech, so without this, numbers would format with comma decimals.
- `Bot` builds a DI container (`BotConfig`, `DiscordSocketClient`, `InteractionService`,
  `DatabaseService`). New services are registered there.
- Command modules are discovered by reflection (`AddModulesAsync`) and **must be loaded before
  login**. Otherwise `Ready` can register an empty command set to the guild and wipe every command.
- Each interaction is detached onto the thread pool via `Task.Run`, because all DB calls are
  synchronous and would otherwise block Discord.Net's single gateway event loop. The `try/catch`
  around it logs dispatch exceptions that would otherwise vanish as unobserved task exceptions, which
  looks like the bot hanging.
- Failed commands, buttons and modals get a central ephemeral `⚠️` reply in
  `HandleInteractionAsync`. The failure is logged (`[CommandFailure]`) *before* that reply is
  attempted, because the reply itself can fail once Discord's 3-second acknowledgement window has
  passed. Autocomplete is never replied to.
- Slash commands are gated by the per-guild channel whitelist. `/whitelist` itself is exempt, and an
  empty whitelist means every channel is allowed. Buttons, select menus and modals are not gated,
  since they come from a message the bot already posted in an allowed channel.

**Commands (`Commands/`)**
- Each module is an `InteractionModuleBase<SocketInteractionContext>` with a `[Group]` and
  constructor-injected `DatabaseService`.
- GM/admin commands use `[RequireUserPermission(GuildPermission.ManageGuild)]` and mostly reply
  ephemerally.

**Persistence (`Database/`)**
- `DatabaseService` is one `partial class` split by domain (`DatabaseService.<Domain>.cs`). All
  schema lives in `InitializeDatabase()` in `DatabaseService.cs` as `CREATE TABLE IF NOT EXISTS`, so
  there is no migration framework. Add a column to an existing table with the
  `AddColumnIfMissing` helper, not by editing the `CREATE TABLE` alone.
- Every method opens its own short-lived `SqliteConnection`; pooling is by connection string.
- Discord snowflakes (user, channel, guild, emoji ids) are stored as `TEXT` and converted to/from
  `ulong` at the boundary.
- Read models are positional `record`s with a matching `SelectXColumns` constant and `ReadX(reader)`
  helper. Keep all three in sync when changing columns.
- The connection string includes `Foreign Keys=True`, because SQLite ignores `REFERENCES` without it
  per connection. The `internal` raw-connection-string constructor does not add it, so callers must.

**Domain model**
- A **faction** is simply a player: it is keyed on the Discord user id, has no name, and is shown as
  `<@user_id>`.
- Parties don't store their members; membership is `factions.party_id`.
- See [Parties.md](Parties.md) for the full rules.

**Rendering (`Rendering/`)**
- `ArchGenerator` (hemicycle) and `WestminsterGenerator` (facing benches) take `List<PartyData>`
  (name, seat count, hex colour) and write a PNG **to a file path**. There is no stream overload yet.
  To post a chart to Discord, render to a temp file and upload it, or add a stream overload.
- `PartyData.Position` (`left`/`right`/`center`/`head`) only affects Westminster; Arch draws in list
  order. `PartyData.EmptySeats` is Westminster's internal layout state, not an input.
- `WestminsterGenerator` keeps per-run state that is never cleared, so use a **new instance per
  render**.
- `ProvinceMapGenerator` colours the province map from three aligned PNGs in `assets/map/` (base,
  province index, text overlay), keyed by each province's index colour. The assets, method and art
  rules are in the "Province map" part of the spec's Election section.
- Linux hosting additionally needs the `SkiaSharp.NativeAssets.Linux` package.
