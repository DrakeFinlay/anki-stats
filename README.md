# Anki Stats

A local Windows desktop app for exploring Japanese vocabulary and Anki review history. Includes vocabulary inventory, common-word/JLPT coverage, review analysis, daily discoveries, exports, and explicitly triggered creation of missing vocabulary notes.

This project was **vibe coded with Codex**, with human direction and review guiding its design and development.

## Documentation

- [Development workflow](.docs/development-workflow.md) — branch, validation, pull request, and merge rules for contributors and coding agents.
- [Repository structure](.docs/repository-structure.md) — where the code, checks, build scripts, and documentation live.
- [Tools used](.docs/tools-used.md) — languages, libraries, development tools, and build commands.
- [Program guide](.docs/program-guide.md) — a beginner-to-intermediate explanation of what the app does, how it was written, and how to use it.

## Run

During development, use **Anki Stats.lnk in the repository root**. It builds and packages the current checkout before opening, including uncommitted source changes, so it stays current as we iterate. It uses your normal saved profile and clears inherited test-data overrides. Startup may take a few seconds; a failed build shows a message and records details in `.tools/launch.log` instead of opening an old version. Close the app before launching after code changes. This development shortcut requires the project-local or an installed .NET 10 SDK. Regenerate it with `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/create-shortcut.ps1` if the repository moves.

For a standalone packaged build, open `artifacts/AnkiStats/AnkiStats.exe`. Published executables are snapshots: other artifact folders can contain older versions and do not update themselves. No terminal, browser, or separately installed .NET runtime is required for a self-contained build. Analysis needs no Anki add-on; optional note creation requires AnkiConnect.

On first launch:

1. Close Anki completely.
2. In Settings, browse to the Anki profile folder containing `collection.anki2` and select **Inspect profile**.
3. Include your Japanese deck tree. Child decks inherit parent rules; exclude any subtrees you do not want counted.
4. Confirm each note type's category and fields. Only Vocabulary types contribute to the vocabulary inventory; Grammar and Kanji types can contribute review events.
5. Select **Preview selection**, check the extracted expressions, then **Save and refresh**.

Subsequent launches refresh the saved selection automatically. Close Anki before refreshing. A failed refresh leaves the latest successful import available with its original scope and timestamp.

## Deck profiles

Use **Active profile** in the header to switch the summary cards, Vocabulary, Reviews, Insights, Calendar, and exports together. Switching uses the last imported data and does not require Anki to be closed. Overview displays a separate Review rhythm chart for every enabled profile. Calendar also has a profile dropdown above its streak ranges; it stays synchronized with the header.

In Settings, select a row under **Deck profiles** to edit that profile's include/exclude rules. You can rename, add, remove, or enable profiles. Preview and save after changing profiles. Field mappings and review time zone are shared. Refresh imports the union of saved profiles once, including disabled profiles, while disabled profiles stay out of the analysis pickers and overview charts.

When upgrading an existing selection, the app creates **日本語**, **漢字**, and **日本語 + 漢字** if those deck trees exist, preserving descendant exclusions and the previous active selection. Other existing selections are retained as **Saved selection**. The combined profile counts each card and answer once; activity in either deck keeps its streak going, and daily counts include both decks. Vocabulary entries still exclude notes mapped as Kanji or Grammar. Failed refreshes retain the previous imported profile definitions and results. Daily insight rotation is saved separately for each deck profile.

## Measurements

- **Learning Patterns** ranks difficulty candidates in the active profile using three or more recorded Again answers during ordinary reviews. Historical counts, current Anki lapse counters, and six-month counts are shown separately. 日本語::永遠 is known and excluded from difficulty rankings; Seen in Immersion is ignored. 日本語::Suspended and 漢字::Relearn are recovery groups, including new/suspended cards, while 漢字::Unused is reference data excluded from rankings. Recovery placement is a separate signal, not proof of repeated failure. Cross-deck comparison uses both imported Japanese/Kanji trees and includes known words only as context. Shared patterns cover explicit same readings, single-kanji reading families, one-character written-form differences, and English-gloss overlap. Findings are candidate associations, not diagnoses; Japanese failures may involve reading or meaning. Ambiguous compound readings remain unresolved without a dictionary. Double-click a card for IDs and its latest ordinary review evidence. Grammar is excluded.
- Overview's **Recorded review time** sums positive Anki answer durations for the active profile across all history. **Time Analysis** adds All history, Last 30 days, Last 90 days, and Last 6 months filters; daily/weekly time charts with all-history and recent average lines; mean/median seconds per answer; weekday/hour breakdowns; and exact daily/weekly tables. Six months runs inclusively from the current study date minus six calendar months through today. Study days use Calendar's 4 AM cutoff, while hour buckets use local clock hours. Daily averages include zero-review dates from the later of the selected start or first available answer through today; weekly averages are daily averages × 7. Current days and edge weeks may be partial. Missing/nonpositive durations do not contribute to time metrics but retain their activity counts. These are recorded, potentially capped answer times, not uncapped wall-clock study time.
- Calendar shows consecutive review streaks with seven weekday rows, review-count heat colors (blue 1–499, purple 500–999, gold 1,000+; darker means more within each band, with gold scaling to the streak maximum), and a dropdown of historical ranges. Streaks span years and scroll horizontally. All rated answer kinds count within the selected decks; a complete day without answers breaks the streak. Calendar uses a fixed 4 AM cutoff in the selected review time zone (Anki's default; custom Anki cutoffs are not imported). The current study day remains open until the next cutoff. Hover or select a day for counts; arrow keys and Home/End navigate the calendar.
- Vocabulary entries are distinct normalized expression/reading pairs. Repeated notes and reverse cards do not multiply an entry; all card states count by default.
- 報う, 報いる, and 報われる stay separate inventory/dictionary entries and form one explicitly curated stem family in Word Coverage.
- Reviews show daily/weekly volume, Again rate and median recorded answer time by period of day, plus card rankings. Ordinary review, learning, relearning, and filtered/cram events are distinct.
- The overview's **Total review answers** includes learning, ordinary review, relearning, and filtered answers, with a visible breakdown. Its **Again rate** uses ordinary reviews only, as labeled. Administrative scheduling records are excluded from both.
- The source profile is visible in the header. Isolated UI test sessions carry a prominent test banner and a different window title; marked synthetic sources retain that warning even outside an isolated session.
- Every daily discovery explains its calculation and scope on the card. Consistency counts midnight-based active dates, shows the denominator and date range, and is not claimed to match Anki's configured study-day rollover.
- Daily discoveries use all available rated-answer history and the vocabulary inventory; card rankings use ordinary reviews. The featured topic stays stable during a day and does not repeat within seven calendar days. A small-data collection may exhaust eligible topics and show a neutral fallback.
- Click vocabulary column headers to sort and double-click an entry for supporting IDs. Search filters the vocabulary export. Review exports identify daily rows, time buckets, card rankings, and their scope.

The inventory includes unstudied cards and is not a fluency score. Review charts use current deck membership and available card-linked history. Recorded response time may be capped by Anki. See [DESIGN.md](DESIGN.md) for definitions and scope.

## Word Coverage

Open **Word Coverage** and select **Download / reinstall reference data** once.
This downloads a pinned, checksum-verified English JMdict snapshot and OpenJLPT
vocabulary lists. Matching, corrections, and reports then work offline. There is
no standalone dictionary browser. No Anki data is sent to the providers.

- **Known**: vocabulary supported by cards in `日本語::永遠` and descendants.
- **Familiar**: vocabulary supported by cards in `日本語::勉強してる` and descendants.
- **Combined**: the union of those dictionary entries, counting overlap once.
- **In database**: all matched vocabulary in the active imported selection,
  including `日本語::Suspended`. Excluded decks are outside this metric.

These categories follow deck placement, regardless of queue state. Common and
N5–N1 rows show numerator/denominator and percentage. Cumulative rows use unions;
Through N3 includes N5, N4, and N3. Counts use unique linked JMdict entry IDs,
rather than cards or reference rows. Unlinked JLPT rows are disclosed and excluded.
OpenJLPT uses corrected community classifications; these can differ from live
Jisho tags and are not official exam inventories.

Select a row and export a Markdown report for either **not covered** entries
(outside Known/Familiar) or entries **absent from database**. Reports include
readings, meanings, dictionary IDs, database status, versions, and scope. They
are created only when requested, in your chosen location. Reports separate
possible matches requiring review from entries with no supporting vocabulary;
an unresolved candidate is not labeled as proven absent.

Matching preserves written script before using kana-folded fallback, so a
katakana loanword is not confused with a hiragana homophone. Explicit alternatives
such as `昨夜[ゆうべ・さくや]` are resolved individually; one note can support multiple
entries when its declared readings identify them independently. Multiple readings
of the same entry still count once. Spacing before annotations and numeric
headwords are supported. Dictionary-backed phonetic separators, such as letter
names in an acronym, join only when none of the alternatives match separately.

An exact kanji spelling with a unique one-edit reading discrepancy (substitution,
insertion, deletion, or adjacent transposition, with readings at least four
characters long) can count with a **Reading warning**. It remains in **Needs
review** with the dictionary reading shown. The app never fuzzy-matches headwords
or chooses between equally close homographs. Unrelated reading discrepancies
remain unresolved. **Partial** matches credit only the resolved reading alternatives.

Matching also compares the mapped English definition with each compatible
dictionary entry's glosses. A uniquely supported definition can resolve homophones:
`はい` plus “yes” identifies the yes entry rather than 灰 or 肺. Matching uses
normalized phrases and limited short phrase containment, not arbitrary substring
overlap or guessed synonyms. Ties and generic definitions remain unresolved.
A unique Japanese match with unverified English wording retains coverage with a
**Definition warning**, so different paraphrases are not treated as proven errors.

Spellings and readings are alternatives: `ありがとう` **or** `有難う` is enough for
the same entry, and `はい` alone is enough without also adding `へい` or `はーい`.
Reports lead with a kana lookup form and explicitly label other written/readings
as optional alternatives. One matching sense is enough; all listed meanings are
not required. Coverage still counts each dictionary entry once.

Inspect matches using the search/status filters. **Review selected match** lets
you select an ambiguous candidate, enter another verified JMdict ID, exclude a
note, or reset its correction. For explicitly studied alternatives, verified
entry IDs can be separated with commas. Preview the entries, then confirm the match.
Corrections persist per collection and note. Changes to the extracted expression
or reading, or removal of a saved dictionary ID, require review. Anki is unchanged.

The stem-family section reports conservative grouping for confirmed combined
entries. The initial curated rule covers 報う／報いる／報われる; other entries stay
separate unless assigned the same family label in match review. Counts are partial
while matches and relationships remain unresolved. Family labels group entries
within the collection and do not alter common/JLPT coverage.

**Reference sources and rules** shows the snapshot and definitions. Reinstall
uses the same pinned snapshot; a future app update can introduce newer versions.
Failed downloads preserve the prior reference data and personal corrections.
Reference data is licensed under CC BY-SA 4.0; see [third-party notices](THIRD-PARTY-NOTICES.md).

### Create missing vocabulary notes

Install [AnkiConnect](https://ankiweb.net/shared/info/2055492159) in Anki and restart
Anki once. Refresh Anki Stats with Anki closed, then open Anki with that same
profile. In Word Coverage, enter a positive whole number under **Add missing
words to Anki**, select **Preview new notes**, review the batch, and select
**Create … new notes**. Preview and Cancel never add anything.

Selection is random without replacement within **N5 → N4 → N3 → N2 → N1 → Common**.
It spills into the next tier, counts overlapping dictionary IDs once, and excludes
confirmed database entries and unresolved possible matches. Before preview and
again before creation, the app checks live vocabulary across the entire running
collection, including decks outside the imported selection. If fewer eligible
words remain, the preview offers the remainder; if none remain, it explains that
the eligible sets are exhausted.

New notes always use the existing **Japanese** note type in **日本語::勉強してる**.
Front uses JMdict's common-form and usually-kana preferences with a compatible
reading (`食べる[たべる]`, but `ありがとう`). These are dictionary preferences,
not a corpus frequency ranking. English contains dictionary glosses; Sentence,
Media, and other fields are blank. Anki's note templates determine the number of
cards. Daily limits and scheduling settings stay unchanged. The first preview
may update the pinned reference snapshot to retain preferred-form metadata.

The app verifies the running profile, note type, fields, and destination deck.
AnkiConnect uses only `127.0.0.1:8765`; an optional API key can be entered in the
panel and is kept in memory only. No collection contents are sent to internet
services. Isolated/synthetic sessions cannot create notes.

AnkiConnect must stay running inside Anki throughout preview and creation. A
connection error naming `127.0.0.1:8765` means the app could not reach that local
service; check that Anki is open and the add-on is enabled. HTTP rejection and
timeout errors are reported separately. The desktop client sends buffered JSON
with Content-Length for compatibility with AnkiConnect's HTTP parser; no browser
CORS configuration change is needed for this client.

New notes have no Anki tags. Dictionary attribution and entry identity remain
in an HTML comment in English. A local creation journal
records dictionary identity and uncertain write attempts before sending each
note. If a response is lost, the app stops and excludes pending entries from
later batches. With Anki open, use **Check pending additions** to acknowledge
identified notes or release entries that were not created. This check never creates
or deletes notes. Successfully added entries remain excluded from future batches.
After creation, close Anki and **Refresh** to update coverage from the saved
collection. Edit or delete unwanted notes manually in Anki.
Older app versions added `ankistats::generated` and `ankistats::jmdict::<ID>` tags.
Those tags can be removed in Anki; existing notes already contain the hidden
attribution identity used for recovery. Legacy tags are still recognized if retained.

## Data safety

The importer opens the source collection only with an ordinary read-only file handle and denies concurrent file access during copying. SQLite analyzes a temporary private copy. Locks, active journals, or unsupported schemas produce a recovery message. Refresh, analysis, exports, and match corrections do not modify Anki. Only the explicit Create action adds new notes through the running AnkiConnect API; the app never writes source SQLite directly, changes existing notes, adjusts schedules, or runs card HTML or JavaScript. Newly added notes can sync normally through Anki.

Settings, the latest imported data, reference data, matching decisions, creation journals, and insight rotation history stay in the per-user local application-data `AnkiStats` folder, accessible from Settings. There is no telemetry. Reference installation downloads public data; note creation communicates with local AnkiConnect; analysis works offline. Source files, personal settings, caches, and exports must not be committed.

The current importer supports Anki collection schemas 11 and 18. Other schemas require an adapter update. Moving or renaming note-type fields requires revisiting setup and previewing the mappings.

## Build and validate

Requires Windows and a .NET 10 SDK. An optional project-local SDK can be placed under `.tools/dotnet`; otherwise use an installed SDK.

```powershell
dotnet build src/AnkiStats.App -c Release
dotnet run --project tests/AnkiStats.Checks -c Release
.\scripts\publish.ps1
```

Publish creates a self-contained x64 executable in the ignored `artifacts/AnkiStats` folder, a shortcut alongside that folder, and the development shortcut in the repository root. The root shortcut always rebuilds the canonical `artifacts/AnkiStats` output regardless of any custom publish folder. Keep the packaged license/notice files with the executable. Build dependencies are restored from NuGet; see [third-party notices](THIRD-PARTY-NOTICES.md).

The focused checks use synthetic collections and cover vocabulary deduplication, Japanese entry distinctions, event scopes, time boundaries and daylight saving, CSV escaping, daily rotation, cache isolation, locked/WAL sources, schema rejection, and source integrity. Optional local importer validation accepts a profile folder through the check runner's `--inspect` argument; it prints only verification status and timing, and removes its temporary private copies.

For isolated UI validation, `ANKISTATS_DATA_DIR` can point to a temporary app-data folder. This always displays a test-session warning. The synthetic fixture generator also marks its source collection. Do not leave a test instance open as the user's working app; close it and launch without the override after checking it. No test data is bundled in the published executable.
