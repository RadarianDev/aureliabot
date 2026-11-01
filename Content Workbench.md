# Content Workbench

**Status: implemented** (untested against live Discord; see "As built" below). A private panel for writing templates entirely inside Discord: a file browser,
the YAML with syntax highlighting, a showcase of what players will see, and editing that works for
templates of any length. Wherever this doc and the spec differ, the spec's `/content` section wins.

The workbench adds no new content rules. Every save, ready, duplicate and remove goes through the same
`ContentRules` calls as `/content import`, `edit`, `ready` and `remove`, so a template behaves the
same whichever route the GM takes. The existing commands stay, as the quick path.

## Opening it

`/content browse [kind] [status] [id]` (Manage Server) replies with an ephemeral **Components V2**
panel. With `id` it opens straight on that template; otherwise on the browser, filtered by `kind`
and `status` if given.

The panel is one message that every button and select **updates in place** (`UpdateAsync`). Each
click is a fresh interaction, so the panel keeps working after the original command's 15-minute
token runs out. Only the GM who opened it can use it; anyone else gets an ephemeral refusal.

## Browser view

```
┌ Content ───────────────────────────── 44 templates · 3 Draft · 41 Ready ┐
│ [ Kind: All ▾ ]  [ Status: Any ▾ ]                                       │
│ ──────────────────────────────────────────────────────────────────────── │
│ ✅ act_of_union        bill     The Act of Union            posted 1×    │
│ ✅ army_bill           bill     The Army Bill                            │
│ 📝 ballot_act          bill     The Ballot Act         ⚠️ 2 problems     │
│ ...                                                                      │
│ [ Open a template ▾ ]                                                    │
│ [◀]  Page 1 / 2  [▶]   [ New ]  [ Import ]  [ Export ]                   │
└──────────────────────────────────────────────────────────────────────────┘
```

- **Filters:** a kind select (All, Issues, Bills, Projects, Events, Crises, each with its count)
  and a status select (Any, Draft, Ready, Archived).
- **Listing:** one line per template on the page: status marker, id, kind, title, times posted, and
  a problem count for a Draft that would fail `/content ready`. Sorted by kind, then id. Markers:
  📝 Draft, ✅ Ready, 🗄️ Archived, ⚠️ problems.
- **Open:** a select of the same templates (at most 25 per page, Discord's select limit), opening
  the template view.
- **Pager** when there are more than 25.
- **New:** pick a kind, then a modal opens pre-filled with that kind's skeleton (see Editing).
- **Import:** a modal with a file upload component taking `.yaml`, `.yml` or `.zip`, exactly as
  `/content import`.
- **Export:** the filtered templates as a zip, exactly as `/content export`.

## Template view

```
┌ The Ballot Act ───────────────────────────────────────────────────────────┐
│ ballot_act · bill · 📝 Draft · never posted · edited <t:…:R> · 1,912 chars │
│ [ YAML ] [ Preview ] [ Problems (2) ] [ Links ]                           │
│ ───────────────────────────────────────────────────────────────────────── │
│ ```yaml                                                                   │
│ id: ballot_act                                                            │
│ title: The Ballot Act                                                     │
│ issue: electoral_reform                                                   │
│ ...                                                                       │
│ ```                                                                       │
│ Lines 1–58 of 58                                     [◀] [▶]              │
│ ───────────────────────────────────────────────────────────────────────── │
│ [ Edit ] [ Edit part ▾ ] [ Ready ] [ Duplicate ] [ Download ] [ Remove ]  │
│ [ Edit live ]  [ Back ]                                                   │
└───────────────────────────────────────────────────────────────────────────┘
```

The header shows the title, id, kind, status, times posted, last edit and size. Four tabs are
buttons, the open one disabled:

- **YAML:** the template in a ` ```yaml ` code block, which Discord's own client highlights. Long
  templates are paged by whole lines, each page fitting the message's text budget, labelled with
  its line range. No line numbers inside the block, since they would break both the highlighting and
  copying the text out.
- **Preview** (the showcase): exactly what players will see, built by `ContentPreview` through
  `Publisher.FormatEffectPost`. A crisis also shows its answer buttons, drawn disabled; an issue its
  `/info issue` card; a bill its motion. A **Share to #gm** button posts the same preview to #gm so a
  co-GM can review it.
- **Problems:** parse errors and readiness problems (`ContentRules.ReadinessProblems`), each with
  its line. The offending lines are quoted in a ` ```diff ` block with a `-` prefix, so Discord
  colours them red, with a line of context on either side. Clicking a problem's select option opens
  **Edit part** on the part holding it.
- **Links:** the templates this one opens, makes available, starts or posts, the templates that
  point at it, and any live copy in play, each with its status. A select jumps to any of them.

Actions:

- **Edit:** the whole YAML in a modal (see Editing).
- **Edit part:** a select of the template's top-level keys (`title`, `text`, `effects`, `answers`
  and so on). The modal holds only that key's block, and saving splices it back into the full YAML at
  the same place, so the order and comments of everything else survive.
- **Ready:** `ContentRules.MakeReady`. On failure the view switches to Problems.
- **Duplicate:** a modal asking for the new id, then saves a copy as a Draft, the same as changing
  the `id` in `/content edit`.
- **Download:** the template as a `.yaml` attachment.
- **Remove:** `ContentRules.Remove` behind the usual Confirm button (delete for a never-posted Draft,
  archive otherwise).
- **Edit live:** shown only when a live copy exists; the same as `/content edit id live`, within the
  limits in the spec's Editing live items.
- **Back:** returns to the browser on the page and filters it came from.

## Editing

- **Whole-template modal.** A modal holds up to five paragraph inputs of 4,000 characters each. A
  template up to 4,000 characters uses one input, as `/content edit` does today. A longer one is
  split at top-level keys across "Part 1" to "Part 5" and joined again on save, so templates up to
  about 20,000 characters can be edited in one modal. Past that, or when no split at top-level keys
  fits, Edit offers Edit part and Replace by upload instead.
- **Replace by upload.** A modal with one file upload component; the file replaces this template,
  exactly as an import of that one file would.
- **Skeletons for New.** One starter file per kind in `config/skeletons/<kind>.yaml`, holding every
  required field from [YAML Reference.md](YAML%20Reference.md) with placeholder values. Kept in
  `config/` so the GM edits them without a rebuild, and never imported as content.
- **Stale edits.** The modal's custom id carries the template's `updated_at`. If the template
  changed after the modal opened (another GM, an import), the save is refused and the GM's text is
  attached, as a failed edit is today.
- **After a save** the panel refreshes in place. A successful save shows a ` ```diff ` of the
  changed lines (`+` and `-`, capped to fit) above the YAML tab. A failed one opens the Problems tab,
  attaches the GM's text as today, and offers **Reopen with my text**, which reopens the modal
  holding what they typed. That text is held in memory for 15 minutes.

Syntax highlighting exists only in messages: Discord draws modal text as plain text, and nothing can
highlight it while the GM types. The diff after each save is the workbench's way round that.

## State

The panel is stateless where it can be: the view, filters, page, template id, tab and YAML page are
packed into each component's custom id (`cw:<view>:<kind>:<status>:<page>:<tab>:<yamlPage>:<id>`).
Custom ids are capped at 100 characters. When a template id is too long to fit, the panel falls back
to a short token for an in-memory session, the way `Confirmations` holds its callbacks, so after a
restart such a panel asks the GM to run `/content browse` again.

## Discord limits it works within

| Limit | Value | Where it matters |
| --- | --- | --- |
| Options in a select | 25 | Browser pages, Edit part, Links |
| Text in a Components V2 message | 4,000 characters | YAML pages, Preview, Problems |
| Components in a message | 40 | Panel layout |
| Inputs in a modal | 5, each up to 4,000 characters | Whole-template editing |
| Custom id | 100 characters | Panel state |
| Interaction token | 15 minutes | Follow-ups only; clicks on the panel are fresh interactions |

## Implementation notes

- **New files:** `Commands/Content/ContentWorkbench.cs` (the command and every component and modal
  handler), `ContentWorkbenchView.cs` (builds the panel for each view and tab), and
  `YamlParts.cs` (splitting a template at top-level keys and splicing a part back in, keeping
  comments and blank lines).
- **Reused as is:** `ContentRules.SaveEdit`, `Import`, `MakeReady`, `Remove`, `FindLive`,
  `ApplyLiveEdit`, `ReadinessProblems`; `ContentPreview.Render`; `Confirmations`.
- **Wording** lives in `config/texts.yaml` under `content.workbench.*`, as every bot text does.
- **Tests:**
  - `YamlParts` round-trips every starting template unchanged, for a split and for a splice of each
    part.
  - YAML paging never breaks a line, and every page fits the message budget.
  - Every custom id the panel builds stays within 100 characters.
  - A stale `updated_at` refuses the save.
  - `TextsTests` covers the new keys as usual.

## Later

- Browsing `config/` (texts, constants, provinces) in the same panel. Those files are read from disk
  and reloaded when they change, so editing them from Discord means the bot writing files on the
  server, which needs its own rules (backups, who may, and what a bad save does).

## As built

Where the code settled a detail this doc left open, or departs from it:

- **New** is a select of kinds rather than a button, and picking a kind opens the modal straight away.
- **Who may click:** the panel is ephemeral, so only the GM who opened it can see or use it; there is no
  separate owner check.
- **Stale edits:** the custom id carries `ContentRules.EditStamp`, the template's `updated_at` plus a
  hash of its YAML, so two saves within the same second are still told apart.
- **Comments** directly above a top-level key belong to that key's part, so Edit part shows and keeps
  them. Blank lines between keys stay with the part before.
- **Problems** come from `ContentRules.Problems`, the same checks as `/content ready`. A readiness
  problem has no line of its own; it points at the first line naming the missing or unready template.
- **Edit live** opens the existing `/content edit live` form, so its result comes back as a separate
  reply, as the command's does, and the panel is not refreshed.
- **Remove** answers with the usual Confirm buttons in a separate private reply; the panel shows the
  change on its next click.
- **Import and Replace by upload** download the files after deferring, then redraw the panel with the
  outcome.
- **State** fits in `cw:<action>:<arg>:<kind><status><tab>.<page>.<yamlPage>:<id>`; selects use `cwsel:`
  and modals `cwmod:`. An id that would push it past 100 characters becomes a `~token` held for 24 hours
  by `WorkbenchSessions`, which also holds a failed save's text for **Reopen with my text** for 15
  minutes.
