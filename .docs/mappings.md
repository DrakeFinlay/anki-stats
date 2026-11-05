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

- **Space** confirms the selected entry, applies the displayed Anki field correction
  when its checkbox is enabled, and advances. Ctrl-select several entries when
  the note intentionally represents several dictionary identities.
- **1** leaves the item unconfirmed, advances, and adds it to **Manual cleanup**
  below the launcher. Deferred items are omitted from the normal launch queue.
- **Ignore item** suppresses that note's coverage or that media form's unresolved
  status locally. It does not create a dictionary identity.
- **Undo** restores the last local decision and its Anki field correction, if one
  was applied, then returns to that item. It checks current fields before writing;
  a subsequent Anki edit blocks the reversal rather than overwriting it. Undo
  history lasts for the current popup session. Decisions and correction receipts
  survive restart.

Search a different word/reading candidate or enter comma-separated installed
JMdict IDs using **Search word / apply dictionary IDs**. Space and 1 type normally
while the editable lookup box has focus; move focus out to use review shortcuts.
Review a selected cleanup row to resolve it. The searchable full list includes
unresolved items; enable **Include confirmed / ignored mappings** to inspect saved
links. **Reset selected saved decision** removes the local decision and re-runs
automatic matching. It does not edit Anki.

## Correcting typos in existing Anki notes

Originally confirmation saved only an app-local dictionary link. It could count a
note as present without fixing its front. The popup now shows **Expression to save**
and **Separate reading field**, plus **Apply the expression / reading shown below
to Anki when confirming**. Inspect those values before pressing Space.

For ordinary Vocabulary fronts, selecting one entry proposes its compatible form
and reading. An existing supported written form and correct reading are preferred;
the dictionary study form is the fallback. The checkbox defaults on when that
proposal changes the expression or mapped reading. For example:

```text
Before: 影響力[えいきょうちょく]
After:  影響力[えいきょうりょく]
```

Space with the checkbox checked updates the mapped expression/reading fields of
that **existing Anki note**, verifies the result, then saves its dictionary link
against the corrected fingerprint. Every sibling card using those fields will
display the correction. Your English meaning, Sentence, Media, tags and study
history are preserved; the app sends no scheduling changes. A separate reading
field is updated when mapped; otherwise the reading is stored in the annotated
expression. Simple formatting wrappers and sound markers are preserved. Complex
fragmented formatting that cannot be changed reliably requires manual editing.

Grammar-category notes and descriptive Vocabulary labels such as `の particle`,
`X のは Y`, and `〜つもりだ` are **local links by default**. Their proposals retain
the existing label. Multiple-entry matches also default to local links. To correct
one deliberately, edit the displayed text and enable the checkbox yourself. You
can uncheck it on any item to confirm a local link without touching Anki. Editable
text boxes accept Space/1 as text rather than review shortcuts.

Keep the matching profile open in Anki with AnkiConnect enabled. The app checks
the current model, ordered fields, source fingerprint and 日本語 membership, and
rechecks raw fields before writing. If you already fixed the note in Anki, refresh
mappings first; an old snapshot is rejected. It never writes directly to the
collection database. Isolated/synthetic sessions cannot apply corrections.

### Buckets and correction history

| Bucket / status | Meaning and next action |
| --- | --- |
| 日本語 Anki matches | Uncertain note links: ambiguity, spelling/reading or meaning warnings, stale decisions, and unmatched notes. Confirm a link and optionally correct its fields. |
| Media source forms | Optional separate queue for unresolved downloaded vocabulary or imported text. Confirmation fixes local corpus analysis only; there is no Anki note to edit. |
| Manual cleanup | Items deferred with 1. They remain unconfirmed, are skipped by normal queue launch, and can be reopened individually. |
| Confirmed | Saved links count toward coverage and duplicate prevention. Show them with Include confirmed / ignored mappings. Anki field editing occurs only when its checkbox was enabled. |
| Ignored | Suppressed locally without a dictionary identity. Ignore never deletes or edits an Anki note. Reset makes the item eligible for analysis again. |
| Anki field corrections | Separate durable receipts of actual field edits, including the exact original and intended raw field values. This is not another vocabulary queue. |

Correction history records **Pending verification** before the write and **Applied**
after a successful read-back. A missing acknowledgement is checked once; the write
is never automatically repeated. A failed check leaves a pending receipt because
the note might already have changed. **Check selected correction status (read only)**
compares current Anki fields to the receipt: **Applied**, **Not applied**, or
**Changed since preview**. This check performs no correction and does not refresh
the full snapshot; use Refresh 日本語 mappings afterward. Applied describes the
verified operation at the recorded time, not a guarantee against later edits.
Undo generates a separate receipt for the reversal.

Resetting a mapping or alias never reverses an Anki field edit. Use popup Undo
during that session, or edit the note in Anki and refresh. Older local confirmations
are not retroactively rewritten in bulk; reopen an item to review a correction.

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
raw corpus without rewriting it. Only explicit confirmation with the correction
checkbox enabled, or its guarded Undo, modifies an existing Anki note.

## Validation

`MappingChecks.cs` covers descriptive labels, scoped aliases, multi-entry grammar
links, deck cohorts, stale decisions, cleanup, source projections and live duplicate
checks.
`NoteCorrectionChecks.cs` verifies correction proposals, live guards, preservation
of other fields, exact Undo, lost/no-op responses and durable correction receipts.
To create a synthetic UI session in an empty ignored folder:

```powershell
.tools/dotnet/dotnet.exe run --project tests/AnkiStats.Checks -c Release -- --seed-mappings-ui .tools/mappings-ui
.tools/dotnet/dotnet.exe run --project tests/AnkiStats.Checks -c Release -- --seed-corrections-ui .tools/corrections-ui
```

Launch with `ANKISTATS_DATA_DIR` set to that folder following the README's isolated
session instructions. The fixture uses synthetic notes and source text only.
