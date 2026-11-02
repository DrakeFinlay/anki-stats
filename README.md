# Anki Stats

A local, read-only Windows desktop app for exploring Japanese vocabulary and Anki review history. Phase 1A provides vocabulary inventory, review analysis, daily discoveries, and CSV exports. Dictionary coverage and stem-family analysis are planned for Phase 1B.

This project was **vibe coded with Codex**, with human direction and review guiding its design and development.

## Documentation

- [Repository structure](.docs/repository-structure.md) — where the code, checks, build scripts, and documentation live.
- [Tools used](.docs/tools-used.md) — languages, libraries, development tools, and build commands.
- [Program guide](.docs/program-guide.md) — a beginner-to-intermediate explanation of what the app does, how it was written, and how to use it.

## Run

Open `AnkiStats.exe` in a published build, or use its Anki Stats shortcut. No terminal, browser, Anki add-on, or separately installed .NET runtime is required for a self-contained build.

On first launch:

1. Close Anki completely.
2. In Settings, browse to the Anki profile folder containing `collection.anki2` and select **Inspect profile**.
3. Include your Japanese deck tree. Child decks inherit parent rules; exclude any subtrees you do not want counted.
4. Confirm each note type's category and fields. Only Vocabulary types contribute to the vocabulary inventory; Grammar and Kanji types can contribute review events.
5. Select **Preview selection**, check the extracted expressions, then **Save and refresh**.

Subsequent launches refresh the saved selection automatically. Close Anki before refreshing. A failed refresh leaves the latest successful import available with its original scope and timestamp.

## Measurements

- Vocabulary entries are distinct normalized expression/reading pairs. Repeated notes and reverse cards do not multiply an entry; all card states count by default.
- 報う, 報いる, and 報われる stay separate inventory entries. Grouping their shared stem family is future work.
- Reviews show daily/weekly volume, Again rate and median recorded answer time by period of day, plus card rankings. Ordinary review, learning, relearning, and filtered/cram events are distinct.
- The overview's **Total review answers** includes learning, ordinary review, relearning, and filtered answers, with a visible breakdown. Its **Again rate** uses ordinary reviews only, as labeled. Administrative scheduling records are excluded from both.
- The source profile is visible in the header. Isolated UI test sessions carry a prominent test banner and a different window title; marked synthetic sources retain that warning even outside an isolated session.
- Every daily discovery explains its calculation and scope on the card. Consistency counts midnight-based active dates, shows the denominator and date range, and is not claimed to match Anki's configured study-day rollover.
- Daily discoveries use all available rated-answer history and the vocabulary inventory; card rankings use ordinary reviews. The featured topic stays stable during a day and does not repeat within seven calendar days. A small-data collection may exhaust eligible topics and show a neutral fallback.
- Click vocabulary column headers to sort and double-click an entry for supporting IDs. Search filters the vocabulary export. Review exports identify daily rows, time buckets, card rankings, and their scope.

The inventory includes unstudied cards and is not a fluency score. Review charts use current deck membership and available card-linked history. Recorded response time may be capped by Anki. See [DESIGN.md](DESIGN.md) for definitions and scope.

## Data safety

The app opens the source collection only with an ordinary read-only file handle and denies concurrent file access during copying. SQLite analyzes a temporary private copy. Locks, active journals, or unsupported schemas produce a recovery message. The app never changes cards, tags, schedules, or AnkiWeb data and never runs card HTML or JavaScript.

Settings, the latest imported data, and insight rotation history stay in the per-user local application-data `AnkiStats` folder, accessible from Settings. There is no telemetry or network dependency in the running app. Source files, personal settings, caches, and exports must not be committed.

The current importer supports Anki collection schemas 11 and 18. Other schemas require an adapter update. Moving or renaming note-type fields requires revisiting setup and previewing the mappings.

## Build and validate

Requires Windows and a .NET 10 SDK. An optional project-local SDK can be placed under `.tools/dotnet`; otherwise use an installed SDK.

```powershell
dotnet build src/AnkiStats.App -c Release
dotnet run --project tests/AnkiStats.Checks -c Release
.\scripts\publish.ps1
```

Publish creates a self-contained x64 executable in the ignored `artifacts/AnkiStats` folder and a shortcut alongside that folder. Keep the packaged license/notice files with the executable. Build dependencies are restored from NuGet; see [third-party notices](THIRD-PARTY-NOTICES.md).

The focused checks use synthetic collections and cover vocabulary deduplication, Japanese entry distinctions, event scopes, time boundaries and daylight saving, CSV escaping, daily rotation, cache isolation, locked/WAL sources, schema rejection, and source integrity. Optional local importer validation accepts a profile folder through the check runner's `--inspect` argument; it prints only verification status and timing, and removes its temporary private copies.

For isolated UI validation, `ANKISTATS_DATA_DIR` can point to a temporary app-data folder. This always displays a test-session warning. The synthetic fixture generator also marks its source collection. Do not leave a test instance open as the user's working app; close it and launch without the override after checking it. No test data is bundled in the published executable.
