# Media vocabulary library

Media supports two inputs with the same local corpus format: a selected Jiten
vocabulary list, or user-selected text analyzed locally. It does not create Anki
notes. The first integration target is Fate/stay night, Jiten deck 233. The user
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
5. Click a cover to inspect resolved words, remaining eligible words, other
   words, or unresolved source forms. Mark completed to move a title to Completed.
   Both shelves use a grid capped at six columns, reducing with available width.

Media comparison runs again after a normal successful refresh when the library
is nonempty. Its timestamp appears separately from profile statistics. It reads
the full collection in this first version, including review records required by
the shared importer; incremental and vocabulary-only imports remain future work.
Comparison is not persisted across app restarts; cached title corpora are.
Cancellation during an Anki read takes effect after that safe read completes.

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
an independent corpus so both results remain inspectable; automatic reconciliation,
sentence selection, local-cover selection and media-driven Anki creation are
future work.

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

The initial live Fate test retrieved all 24,064 provider forms, yielding 20,742
distinct validated JMdict IDs and 233 mismatched forms with the pinned reference.
These are acquisition-test results, not personal Anki mining statistics. Unknown
proper-name IDs are among the excluded forms. The actual included edition/routes
remain unverified.

Versioned compressed JSON corpora live under `media/` in the app-data directory,
independent of Anki caches. They retain source, title/edition annotation, time,
analyzer/reference versions, content hash, word identities/forms and counts.
Writes replace a title atomically. Local imports also retain cleaned text in a
companion gzip file. Optional Jiten cover images are cached under `media/covers/`;
unavailable artwork falls back to a text tile. Artwork is separate third-party
material and is not redistributed with the app.

Only public title queries/IDs and artwork requests go online. Anki text, local
scripts, file paths and personal coverage statistics are not uploaded. Downloaded
data, cleaned text and test artifacts are private app data or ignored `.tools/`
data, never checked into the repository. Jiten-derived vocabulary carries
[CC BY-SA 4.0 attribution](https://jiten.moe/terms); API access follows its
[personal-tool and no-bulk-mirroring policy](https://jiten.moe/guides/api-and-scraping).

## Development checks

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
