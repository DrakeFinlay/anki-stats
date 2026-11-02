# Repository structure

This repository contains a Windows desktop application, a reusable analysis library, a small validation program, and documentation. User collections and generated build files are not source code and must stay out of version control.

```text
anki-stats/
├── .docs/
│   ├── development-workflow.md
│   ├── repository-structure.md
│   ├── tools-used.md
│   └── program-guide.md
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
├── Directory.Build.props
├── global.json
├── README.md
└── THIRD-PARTY-NOTICES.md
```

## Application: `src/AnkiStats.App`

This project produces the executable. It references the core library and uses Windows Presentation Foundation (WPF) for the interface.

| File | Responsibility |
| --- | --- |
| `AnkiStats.App.csproj` | Defines the Windows desktop project and its core-library reference. |
| `App.xaml` | Shared colors, control styles, and application startup window. |
| `App.xaml.cs` | English formatting and a last-resort UI exception handler. |
| `MainWindow.xaml` | Layout for Overview, Vocabulary, Reviews, Insights, and Settings. |
| `MainWindow.xaml.cs` | Connects controls to setup, importing, analysis, searching, daily discoveries, and exports. Contains the editable deck and field-mapping choices. |
| `ReviewChart.cs` | Draws review-volume bars directly with WPF; groups dates for daily or weekly views. |
| `app.manifest` | Declares normal user privileges and Windows compatibility. |

The current application uses code-behind: event handlers associated with the window coordinate its behavior. It is not a separate web frontend or a full application framework with a server.

## Analysis: `src/AnkiStats.Core`

This library contains the work that does not require a window. Keeping it separate allows the validation program to exercise importing and calculations without launching the GUI.

| File | Responsibility |
| --- | --- |
| `Models.cs` | Data types for settings, deck rules, field mappings, imported records, and calculated results. |
| `CollectionImporter.cs` | Safely acquires a private collection copy, reads supported schemas, applies selections, and extracts notes, cards, and review events. |
| `Analysis.cs` | Cleans field text, groups vocabulary entries, calculates review statistics, generates discovery candidates, and writes CSV exports. |
| `LocalStore.cs` | Stores settings, the latest imported result, and daily discovery history in an app-owned SQLite database. The imported result is serialized as compressed JSON inside that database. |
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

## Validation: `tests/AnkiStats.Checks`

This is an executable check runner, rather than a project using a separate unit-test framework.

- `Program.cs` checks calculations, Japanese entry distinctions, event scopes, date boundaries, daylight saving, CSV behavior, discovery rotation, cache isolation, and importer safety. It also provides optional local inspection commands.
- `UiFixture.cs` generates a marked synthetic collection for isolated interface checks.
- `LocalAudit.cs` provides explicitly invoked local audit/connection helpers. It reports aggregate counts and can save a selection to normal per-user app storage; it does not modify Anki.

Ordinary checks use temporary synthetic collections and clean up afterward. Real collections must never become committed test fixtures.

## Build and project files

- `AGENTS.md` directs coding agents to read `.docs/development-workflow.md` before each task; the workflow defines branching, validation, pull requests, and authorized merges.
- `scripts/publish.ps1` publishes a self-contained Windows x64 build, copies third-party notices, and creates packaged and root development shortcuts.
- `scripts/launch.ps1` rebuilds the current checkout into `artifacts/AnkiStats`, then opens it with normal saved settings. Build failures show a message and write `.tools/launch.log`; inherited test-data overrides are removed from the app's environment.
- `scripts/create-shortcut.ps1` creates the ignored root `Anki Stats.lnk` pointing to the launcher. Run it again after moving the repository.
- `global.json` selects a stable .NET 10 SDK, allowing roll-forward within that SDK generation.
- `Directory.Build.props` applies shared settings, including nullable-reference checks, deterministic builds, and treating compiler warnings as errors.
- `DESIGN.md` records product intent, scope, metric definitions, and acceptance criteria.
- `LICENSES/` and `THIRD-PARTY-NOTICES.md` contain notices for bundled dependencies. They do not grant a license to the project's original code.

## Generated and private files

Ignored folders can appear after development:

| Location | Contents |
| --- | --- |
| `.tools/` | Optional local SDK, package downloads, and isolated development tools. |
| `artifacts/` | Published executables, packaged notices, and shortcuts. |
| Each project's `bin/` and `obj/` | Compiled assemblies and intermediate build files. |

Normal user settings and imported data belong in the per-user local application-data `AnkiStats` folder, accessible through Settings. Keep real collections, exports, personal paths, and collection statistics out of repository files, including documentation.

For usage, read the [program guide](program-guide.md). For the development stack, read [tools used](tools-used.md).
