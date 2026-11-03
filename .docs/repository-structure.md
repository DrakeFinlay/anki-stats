# Repository structure

This repository contains a Windows desktop application, a reusable analysis library, a small validation program, and documentation. User collections and generated build files are not source code and must stay out of version control.

```text
anki-stats/
├── .docs/
│   ├── development-workflow.md
│   ├── repository-structure.md
│   ├── tools-used.md
│   ├── program-guide.md
│   ├── dictionary-and-coverage.md
│   └── anki-connect.md
├── src/
│   ├── AnkiStats.App/
│   └── AnkiStats.Core/
├── tests/
│   └── AnkiStats.Checks/
├── scripts/
│   ├── publish.ps1
│   ├── launch.ps1
│   └── create-shortcut.ps1
├── LICENSES/
├── .gitignore
├── AGENTS.md
├── DESIGN.md
├── FUTURE-PLANS.md
├── Directory.Build.props
├── global.json
├── README.md
├── USER-GUIDE.md
└── THIRD-PARTY-NOTICES.md
```

## Application: `src/AnkiStats.App`

This project produces the executable. It references the core library and uses Windows Presentation Foundation (WPF) for the interface.

| File | Responsibility |
| --- | --- |
| `AnkiStats.App.csproj` | Defines the Windows desktop project and its core-library reference. |
| `App.xaml` | Shared colors, control styles, and application startup window. |
| `App.xaml.cs` | English formatting and a last-resort UI exception handler. |
| `MainWindow.xaml` | Layout for Overview, Vocabulary, Word Coverage, Reviews, Insights, Calendar, Time Analysis, Learning Patterns, and Settings. |
| `MainWindow.xaml.cs` | Connects controls to setup, importing, analysis, searching, daily discoveries, and exports. Contains the editable deck and field-mapping choices. |
| `MainWindow.Profiles.cs` | Coordinates saved deck-profile editing, active selection, scoped results, and overview comparison charts. |
| `MainWindow.Time.cs` | Connects recorded hours, time-range filters, comparison metrics, time charts, and weekday/hour tables to the active profile. |
| `MainWindow.Learning.cs` | Filters difficulty cohorts, cross-deck comparisons, shared patterns, and supporting review evidence. |
| `MainWindow.Coverage.cs` | Connects local reference installation, common/JLPT coverage, missing-word reports, and persistent match/family corrections to the active profile. |
| `MainWindow.Creation.cs` | Previews missing-word batches, requires explicit creation, journals writes, and reconciles pending additions through local AnkiConnect. |
| `TimeChart.cs` | Draws recorded minutes by day or week with all-history and six-month average reference lines. |
| `StreakCalendar.cs` | Draws the streak heatmap on one surface with day selection, hover text, and keyboard navigation. |
| `ReviewChart.cs` | Draws review-volume bars directly with WPF; groups dates for daily or weekly views. |
| `app.manifest` | Declares normal user privileges and Windows compatibility. |

The current application uses code-behind: event handlers associated with the window coordinate its behavior. It is not a separate web frontend or a full application framework with a server.

## Analysis: `src/AnkiStats.Core`

This library contains the work that does not require a window. Keeping it separate allows the validation program to exercise importing and calculations without launching the GUI.

| File | Responsibility |
| --- | --- |
| `Models.cs` | Data types for settings, deck rules, field mappings, imported records, and calculated results. |
| `DeckProfiles.cs` | Migrates and validates named deck profiles and filters imported data to a profile without duplicating records. |
| `Streaks.cs` | Defines 4 AM study-day boundaries, daily answer aggregation, and current/historical consecutive streak ranges. |
| `TimeAnalysis.cs` | Calculates recorded time, mean/median answer duration, six-month comparisons, and study-day/week/hour aggregates. |
| `LearningPatterns.cs` | Applies deck-role rules and three-lapse history criteria; produces cross-deck and explicit reading/written-form/gloss candidates. |
| `CollectionImporter.cs` | Safely acquires a private collection copy, reads supported schemas, applies selections, and extracts notes, cards, and review events. |
| `Analysis.cs` | Cleans field text, groups vocabulary entries, calculates review statistics, generates discovery candidates, and writes CSV exports. |
| `VocabularyCoverage.cs` | Matches expression/reading pairs to restricted dictionary forms, aggregates named-deck cohorts and entry coverage, groups conservative families, and produces Markdown missing-word reports. |
| `VocabularyExpression.cs` | Extracts written forms and explicit reading alternatives, including segment furigana, spacing, numeric headwords, and dictionary-verifiable phonetic joins. |
| `DefinitionMatcher.cs` | Provides normalized English gloss evidence for homophone resolution, preserving negation and rejecting generic or tied evidence. |
| `CoverageReferenceStore.cs` | Downloads checksum-verified reference snapshots, builds a local dictionary index, and atomically replaces compressed reference data independently of personal corrections. |
| `AnkiConnectClient.cs` | Calls the fixed loopback API, verifies the profile/model/deck, and reads live vocabulary for duplicate checks. |
| `NoteCreation.cs` | Selects random missing entries in tier order, builds preferred-form notes, journals explicit writes, and reconciles uncertain attempts without retries. |
| `LocalStore.cs` | Stores settings, the gzip-JSON imported cache, daily discovery history, matching/family decisions, and creation journals in app-owned SQLite. Reference bundles remain separate compressed files. |
| `AnkiStats.Core.csproj` | Defines the library and its SQLite dependencies. |

The data flow is:

```text
Selected Anki profile
    → protected read-only file copy
    → private SQLite queries and field extraction
    → selected notes, cards, and review events
    → analysis results
    → desktop views and CSV exports
```

The original collection is never opened by the app's SQLite connection.

Explicit new-note creation follows a separate path: local reference and cached
coverage → live AnkiConnect duplicate checks → batch preview → Create button →
persisted attempt → AnkiConnect addNote → local identity receipt. Anki owns all
source writes; normal importing and analysis remain read-only.

## Validation: `tests/AnkiStats.Checks`

This is an executable check runner, rather than a project using a separate unit-test framework.

- `Program.cs` checks calculations, Japanese entry distinctions, event scopes, date boundaries, daylight saving, CSV behavior, discovery rotation, cache isolation, and importer safety. It also provides optional local inspection commands.
- `UiFixture.cs` generates a marked synthetic collection for isolated interface checks.
- `LocalAudit.cs` provides explicitly invoked local audit/connection helpers. It reports aggregate counts and can save a selection to normal per-user app storage; it does not modify Anki.
- `CoverageChecks.cs` validates dictionary identities, homographs, furigana, named-deck cohorts, persistent corrections, reference replacement, family rules, and Markdown reports. Its optional audit reads an existing app cache without opening Anki or printing personal text.
- `CoverageUiFixture.cs` creates a small, marked synthetic collection and reference bundle for isolated coverage UI verification.
- `CoverageMatchingChecks.cs` and `DefinitionChecks.cs` exercise alternative readings, script preference, bounded reading errors, English evidence, and stale corrections.
- `ProfileChecks.cs`, `StreakChecks.cs`, `TimeChecks.cs`, and `LearningChecks.cs` verify named scopes, study-day boundaries, recorded durations, and difficulty evidence. `LearningUiFixture.cs` supports isolated Learning Patterns checks.
- `NoteCreationChecks.cs` uses an in-memory HTTP handler to verify tier spillover, preferred forms, live duplicates, profile checks, write payloads, and lost-response recovery without opening or modifying real Anki.
- `AnkiTransportChecks.cs` exercises HTTP framing against a temporary loopback listener, verifying fixed-length JSON requests and actionable protocol errors without contacting real Anki.

Ordinary checks use temporary synthetic collections and clean up afterward. Real collections must never become committed test fixtures.

## Build and project files

- `AGENTS.md` directs coding agents to read `.docs/development-workflow.md` before each task; the workflow defines branching, validation, pull requests, and authorized merges.
- `scripts/publish.ps1` publishes a self-contained Windows x64 build, copies third-party notices, and creates packaged and root development shortcuts.
- `scripts/launch.ps1` rebuilds the current checkout into `artifacts/AnkiStats`, then opens it with normal saved settings. Build failures show a message and write `.tools/launch.log`; inherited test-data overrides are removed from the app's environment.
- `scripts/create-shortcut.ps1` creates the ignored root `Anki Stats.lnk` pointing to the launcher. Run it again after moving the repository.
- `global.json` selects a stable .NET 10 SDK, allowing roll-forward within that SDK generation.
- `Directory.Build.props` applies shared settings, including nullable-reference checks, deterministic builds, and treating compiler warnings as errors.
- `DESIGN.md` records current product behavior, metric definitions, architecture, and safety boundaries.
- `FUTURE-PLANS.md` separates unimplemented sentence/media and other product ideas from current functionality.
- `USER-GUIDE.md` covers installation, first setup, daily use, and troubleshooting. `.docs/program-guide.md` explains internals, with deeper dictionary and AnkiConnect guides alongside it.
- `LICENSES/` and `THIRD-PARTY-NOTICES.md` contain notices for bundled dependencies. They do not grant a license to the project's original code.

## Generated and private files

Ignored folders can appear after development:

| Location | Contents |
| --- | --- |
| `.tools/` | Optional local SDK, package downloads, and isolated development tools. |
| `artifacts/` | Published executables, packaged notices, and shortcuts. |
| Each project's `bin/` and `obj/` | Compiled assemblies and intermediate build files. |

Normal user settings and imported data belong in the per-user local application-data `AnkiStats` folder, accessible through Settings. Keep real collections, exports, personal paths, and collection statistics out of repository files, including documentation.

For usage, read the [user's guide](../USER-GUIDE.md). For internals, read [how the
program works](program-guide.md), [dictionary and coverage](dictionary-and-coverage.md),
and [AnkiConnect integration](anki-connect.md). For dependencies, read [tools used](tools-used.md).
