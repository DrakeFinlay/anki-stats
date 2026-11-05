# Mappings

Use **Mappings** to resolve uncertain Anki dictionary matches and unresolved media
forms. Descriptive fronts such as `の particle`, `X のは Y`, `Short form verb + の`,
`〜つもりだ`, and `Sentence ending in 〜よ` can suggest dictionary entries without
renaming your Anki notes. Suggestions require confirmation for each note.

## Dashboard and review

The dashboard shows supporting **cards** with possible Anki matches, uncertain
**notes**, unresolved media **forms grouped within each title**, and the manual
cleanup count. These units differ: one Anki note can have several cards. Counts
describe the current snapshot, rather than updating from Anki continuously.
Anki review always uses **日本語 and its subdecks**, regardless of the active
profile. English, standalone 文法 and similarly named roots are excluded, even
when they use the same note types. **Refresh 日本語 mappings** reads the complete
日本語 tree using the field mappings in Settings. Before that refresh, available
cached data is filtered to the same tree; another deck is never used as a fallback.
Unmapped and Ignored note types cannot establish dictionary presence.

The default queue is **日本語 Anki matches**. Opt into **Media source forms** to
review downloaded vocabulary or local text separately. Character names and other
IDs absent from the installed dictionary remain available for manual review; they
are not silently discarded. Select **Launch mapping pop-up** to review the chosen
queue. The left side shows the original note expression,
reading, meaning, model, decks and IDs. For media items it shows the source form,
title, edition and a genuine source sentence when available. The right side shows
the proposed dictionary entry, readings and restrictions, English meaning,
Common/JLPT flags, and dictionary ID/version.

Jiten imports download analyzed vocabulary lists, not complete scripts. A missing
source ID, incompatible written form, or incompatible reading has its own status.
Names/provider-specific entries may be absent from our vocabulary dictionary.
Local script/subtitle/book imports can also contain unknown tokens, inflections
or ambiguous lemmas. These are source-analysis issues, separate from uncertain
matches to your Anki notes.

- **Space** confirms the selected entry and advances. Ctrl-select several entries
  when the note intentionally represents several dictionary identities.
- **1** leaves the item unconfirmed, advances, and adds it to **Manual cleanup**
  below the launcher. Deferred items are omitted from the normal launch queue.
- **Ignore item** suppresses that note's coverage or that media form's unresolved
  status locally. It does not create a dictionary identity.
- **Undo** restores the last decision and returns to that review item. Undo history
  lasts for the current popup session; decisions themselves survive restart.

Search a different word/reading candidate or enter comma-separated installed
JMdict IDs using **Search word / apply dictionary IDs**. Space and 1 type normally
while the editable lookup box has focus; move focus out to use review shortcuts.
Review a selected cleanup row to resolve it. The searchable full list includes
unresolved items; enable **Include confirmed / ignored mappings** to inspect saved
links. **Reset selected saved decision** removes the local decision and re-runs
automatic matching. It does not edit Anki.

## Aliases and coverage

Expand **Configure exact label aliases** to map an exact expression to a dictionary
lookup, with optional reading and note-type scope. Save the rules to generate
suggestions. An alias never approves every note automatically. For a complex
grammar pattern, select only the dictionary entries that the particular Anki note
actually teaches.

Confirmed grammar links count in **Word Coverage** and in media presence checks,
using the existing Known/Familiar deck rules. Grammar notes remain separate from
the vocabulary inventory and learning-pattern metrics. Media presence counts a
confirmed link anywhere in the collection, including suspended cards. Uncertain
candidate entries are held out of automatic missing-word creation until reviewed.
Live duplicate checks also honor confirmed grammar links before creating notes.

Links and cleanup records are stored in the app database per collection and note,
or per media corpus/source form. Note expression, reading or normalized meaning
changes invalidate a note's approval. A changed script hash, analyzer, corpus
dictionary version or source identity invalidates a media approval. Removed
dictionary IDs also need review. Saved source links are applied over the cached
raw corpus without rewriting it. No part of this workflow renames or modifies
existing Anki notes.

## Validation

`MappingChecks.cs` covers descriptive labels, scoped aliases, multi-entry grammar
links, deck cohorts, stale decisions, cleanup, source projections and live duplicate
checks. To create a synthetic UI session in an empty ignored folder:

```powershell
.tools/dotnet/dotnet.exe run --project tests/AnkiStats.Checks -c Release -- --seed-mappings-ui .tools/mappings-ui
```

Launch with `ANKISTATS_DATA_DIR` set to that folder following the README's isolated
session instructions. The fixture uses synthetic notes and source text only.
