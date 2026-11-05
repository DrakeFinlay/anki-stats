# Media vocabulary library

Media supports two inputs with the same local corpus format: a selected Jiten
vocabulary list, or user-selected text analyzed locally. Its Import tab can
explicitly create previewed missing vocabulary notes from completed media. The
Word list also imports selected missing entries from an individual title on any shelf. The
first integration target is Fate/stay night, Jiten deck 233. The user
reads the remaster and accepts a nearby edition such as Réalta Nua; the provider
record does not establish an exact edition, so the app keeps that uncertainty
visible. Search supports visual novels, anime, novels, nonfiction and web novels.
Manga and other media types are outside this version.

## Use

1. Install dictionary reference data in Word Coverage and configure vocabulary
   field mappings in Settings.
2. Open Media → Add, search for a title and select the matching work. Annotate
   edition/routes/episodes where known, then download its vocabulary. Next 50
   retrieves another search page. A title result is not proof of complete text.
3. Alternatively, enter a local title, choose the type and edition, and select
   TXT, SRT, ASS, VTT or a DRM-free EPUB. UTF-8 and Shift-JIS text are supported.
   Scripts must already be extracted plain text; engine archive extraction and
   DRM removal are not implemented.
4. Click Compare all Anki vocabulary. This performs a separate read-only import
   across all decks using configured Vocabulary mappings, including suspended
   cards. Unmapped/ignored note types cannot establish word presence. The media
   comparison does not change saved deck profiles or Word Coverage's scope.
5. New titles go to **Planned** by default; choose a different destination in Add
   when needed. Each title has a Shelf picker to move it between Planned,
   Currently reading and Completed without downloading or analyzing again.
6. Currently reading holds up to two titles. A single title fills the available
   width with a large cover, title, mining counts and progress. Two titles share
   the width on large windows and stack on smaller windows. Planned and Completed
   retain grids capped at four columns, reducing the column count to leave room
   for statistics beside each cover. All covers preserve their original aspect
   ratio without gradient side panels or image cropping. Existing cached shelf assignments
   are preserved, including currently reading titles from earlier versions.
7. Click a cover or Explore vocabulary to inspect resolved words, remaining
   eligible words, other words, or unresolved source forms.
8. **Refresh covers** refreshes artwork without downloading vocabulary again.
   VNs with a verified VNDB link prefer the original VNDB image over provider
   thumbnails. Anime/books retain the provider image. Failed replacements preserve
   old artwork. Original resolution varies (the checked Fate image is 600×900);
   enlarging it cannot restore detail absent from the source. Rendering uses
   native decoded resolution and high-quality scaling.

Media comparison runs again after a normal successful refresh when the library
is nonempty. Its timestamp appears separately from profile statistics. It reads
the full collection in this first version, including review records required by
the shared importer; incremental and vocabulary-only imports remain future work.
Comparison is not persisted across app restarts; cached title corpora are.
Cancellation during an Anki read takes effect after that safe read completes.

## Browse and import missing words from Completed

### Import selected words from one title

Open a title's **Word list** from its cover or **Explore vocabulary**. Ctrl-click
individual rows, Shift-click a range, or press Ctrl+A to select all displayed rows.
Select **Preview selected for Anki…** to prepare those entries with the shared
Anki preview and live duplicate checks. Nothing is written until you select
**Create** in that preview. The selection counter shows how many selected rows
are available to import.

This works for Reading, Planned and Completed titles. Both Common/JLPT and other
resolved words are supported, without a minimum-title threshold. Existing Anki
entries, possible unresolved Anki matches and unresolved media forms are skipped.
If a live duplicate check removes a selected entry, no unselected replacement is
added. **Include available source sentence** uses a genuine sentence retained for
that title when available; otherwise preview shows Sentence blank. Changing the
word filter or opening another title clears the row selection.

### Shared words across completed titles

Open **Media → Import** after Compare all Anki vocabulary has completed.
Two pools are available: **Common or JLPT** (the union), and **Uncommon: outside
Common and JLPT** (neither membership, not a separate dictionary rarity tag).
Both contain resolved entries absent from mapped Anki vocabulary across all
decks, including suspended notes. Possible unresolved Anki matches are excluded
from automatic creation. The active profile does not narrow this presence check.

Minimum titles defaults to 2 and can be set to any positive whole number. Each
library title counts once per entry, despite spelling variants or repeated
occurrences. Only Completed corpora contribute to title counts and occurrence
totals. Ranking is title count descending, then occurrence count descending,
then dictionary ID for stable ties. Search filters words, readings, definitions
and source titles without changing ranking. Separate library corpora/editions
are counted as separate items; duplicate references to the same corpus ID are not.

Use **Preview selected**, **Preview first N**, or **Preview all filtered**. The
last two follow the current pool, minimum and search filter. Selected rows keep
the same rank order. These buttons perform live duplicate checks and open the
existing new-note preview; they do not write until **Create** is clicked there.
Notes use the existing Japanese model and 日本語::勉強してる deck. Both pools
require a usable preferred dictionary form and definition. The same global
duplicate/possible-match exclusions, tag-free payloads, HTML encoding,
profile/schema checks and pending-write journal apply to media batches. Pending
additions can be reconciled through Word Coverage's existing control. No genuine
Anki notes are created during tests or in isolated/synthetic app sessions.

### Optional script sentences

New local analyses retain one real source sentence (up to 500 characters) per
token form. The local analyzer resolves that form to a JMdict entry before the
sentence can accompany it. **Find sentences in local scripts** reanalyzes saved
local text with the selected Python/Sudachi environment to enrich older imports.
This can update local analysis counts/identities and records the analyzer version.
It never uploads scripts or searches online for replacement text. Inflected
forms are handled through the same local morphology rather than substring guesses.

A candidate can use a resolved sentence from any local library script, including
a non-Completed title; that does not change its Completed-only ranking. Preview
and the table show the source title. Source selection is deterministic, not a
pedagogical quality ranking, and contextual word senses still follow the local
resolver's limits. No sentences are generated or invented. Online Jiten vocabulary
imports do not download full scripts, so sentences stay blank without a local
source. Missing old raw text cannot be reconstructed; reimport the text if needed.

**Include available source sentence** is optional. Preview shows exactly which
notes have a Sentence and which are blank. Media records the sentence's source
title, or the completed source-title list when Sentence is blank. Only fields
present in the live model are populated; absent Sentence/Media fields remain
unavailable and preview reflects that. Other fields stay blank. Word Coverage's
reference-list creation continues to leave Sentence and Media blank.

### Local analyzer setup

Use a Python installation/environment with these optional dependencies:

```powershell
python -m pip install SudachiPy SudachiDict-core
```

Enter that environment's full Python executable path in Media if `python` is
not on PATH. The development validation environment, when prepared, is
`.tools/media-python/Scripts/python.exe`. Python and Sudachi are not bundled in
the Windows app. No installation happens automatically. Missing dependencies
produce an error, retaining previously imported titles.

The embedded helper runs with isolated Python imports (`-I`), receives cleaned
text on stdin, and returns aggregated surface/lemma/reading/POS records. It uses
Sudachi split mode C, records analyzer and core-dictionary package versions, and
skips punctuation/symbol/space tokens. Dictionary forms are resolved against the
installed JMdict, with compatible readings where safe. Inflected readings do
not constrain lemma readings. Homographs with multiple candidates stay unresolved;
there is no contextual semantic disambiguation or compound-repair pipeline yet.
All Common/JLPT entries remain eligible, including particles and auxiliaries.

## Counting

All resolved counts deduplicate by JMdict entry ID. Multiple forms, occurrences,
notes and sibling cards do not multiply an entry. Eligibility is Common ∪ N5 ∪
N4 ∪ N3 ∪ N2 ∪ N1 using the app's reference classifications, not provider tags.

- **In Anki / mined:** confirmed dedicated vocabulary anywhere in the mapped
  collection, independent of title provenance, deck profile or queue state.
- **Remaining:** eligible entries with neither confirmed presence nor a possible
  unresolved Anki match. Possible matches have a separate Needs review count.
- **Other:** resolved title entries outside both Common and JLPT, including those
  already present in Anki.
- **% mined:** confirmed eligible In Anki / total resolved eligible entries.
  An empty eligible set displays No eligible words.
- **Unresolved source forms:** ambiguous local tokens, missing IDs, or online
  ID/form mismatches. These are excluded from resolved-entry denominators and
  remain inspectable. They are forms, not a guessed count of unique words.

Counts describe whole-title source vocabulary, not unread text or comprehension.
Provider analysis and local analysis can disagree. Importing local text creates
an independent corpus so both results remain inspectable. Automatic corpus
reconciliation, curated sentence selection and local-cover selection remain
future work. Media Import currently mines completed corpora only.

## Acquisition, cache and privacy

[Jiten's public API](https://jiten.moe/guides/using-the-api) supplies title search
and paginated vocabulary without an account. The client downloads only selected
titles, spaces vocabulary requests, rejects empty/truncated/overlapping pages and
changing totals, and validates ID/form membership against local JMdict. Provider
analysis versions and corpus completeness are not exposed in these responses;
they are marked unknown. No complete script is downloaded through this path.
Rate-limit failures retain old corpora; retry later rather than restarting a
large download automatically. No bulk catalogue mirroring is performed.
Jiten's annotated forms use kanji-run furigana; the adapter preserves intervening
okurigana (for example, 差[さ]し込[こ]む → さしこむ). Source IDs are retained for
local revalidation when the installed reference or adapter changes.
Kana-only headwords validate against dictionary readings even where those
readings restrict their associated kanji spellings. Written kanji still must obey
those restrictions. Parser v2 revalidates cached Jiten lists locally on comparison;
no re-download or script is needed. Review distinguishes missing IDs from
incompatible forms and readings instead of reporting one generic mismatch.

The initial live Fate test retrieved all 24,064 provider forms, yielding 20,742
distinct validated JMdict IDs and 233 mismatched forms with the pinned reference.
These are historical parser-v1 acquisition-test results, not current personal
Anki mining statistics; the corrected kana validation can reduce the mismatches. Unknown
proper-name IDs are among the excluded forms. The actual included edition/routes
remain unverified.

Versioned compressed JSON corpora live under `media/` in the app-data directory,
independent of Anki caches. They retain source, title/edition annotation, time,
analyzer/reference versions, content hash, word identities/forms and counts.
Writes replace a title atomically. Local imports also retain cleaned text in a
companion gzip file. Optional Jiten/VNDB cover images are cached under `media/covers/`;
unavailable artwork falls back to a text tile. Artwork is separate third-party
material and is not redistributed with the app.

Only public title queries/IDs and artwork requests, including public VNDB ID
lookups for VN covers, go online. Anki text, local
scripts, file paths and personal coverage statistics are not uploaded. Downloaded
data, cleaned text and test artifacts are private app data or ignored `.tools/`
data, never checked into the repository. Jiten-derived vocabulary carries
[CC BY-SA 4.0 attribution](https://jiten.moe/terms); API access follows its
[personal-tool and no-bulk-mirroring policy](https://jiten.moe/guides/api-and-scraping).

## Development checks

Use [Mappings](mappings.md) to confirm descriptive Anki grammar/particle links or
resolve media source forms. Confirmed Anki links prevent duplicate mining and
count in Word Coverage. Source-form links apply over the saved raw corpus and
flow into shelf statistics, word lists and Completed import pools. Changed source
fingerprints invalidate the saved link. Ignoring a source form suppresses its
unresolved warning without inventing a match.

The ordinary checks exercise deduplication, cohort scopes, ambiguity, cache
replacement, cleaning, local lemma resolution and simulated online responses.
Optional live acquisition and synthetic local morphology checks:

```powershell
.tools/dotnet/dotnet.exe run --project tests/AnkiStats.Checks -c Release -- --media-smoke .tools/coverage-reference .tools/media-smoke
.tools/dotnet/dotnet.exe run --project tests/AnkiStats.Checks -c Release -- --local-media-smoke .tools/coverage-reference D:/Drake/git-projects/anki-stats/.tools/media-python/Scripts/python.exe
.tools/dotnet/dotnet.exe run --project tests/AnkiStats.Checks -c Release -- --media-revalidate .tools/coverage-reference .tools/media-smoke
```

These commands use the existing installed reference snapshot and never read or
write Anki. The live check downloads one selected title, not the catalogue.
