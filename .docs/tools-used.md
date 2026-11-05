# Tools used

This guide describes the tools used by the checked-in implementation. Versions below are those declared in the repository, rather than recommendations for unrelated projects.

## Application and development stack

| Tool | Role in this project |
| --- | --- |
| C# | Application behavior, importing, calculations, persistence, and validation. |
| .NET 10 SDK | Compiles the projects, restores dependencies, runs checks, and publishes the application. `global.json` selects this SDK generation. |
| WPF | Windows desktop UI, controls, data binding, and chart drawing. |
| XAML | Declarative layout and styling for WPF windows and controls. |
| SQLite | Reads private Anki collection copies and stores app-owned settings and cached data. |
| Microsoft.Data.Sqlite `10.0.14` | C# interface for SQLite connections, commands, and transactions. |
| SQLitePCLRaw.bundle_e_sqlite3 `3.0.5` | Supplies the SQLite native engine and supporting bindings. |
| JMdict via jmdict-simplified | Downloaded English dictionary identities, forms/readings, glosses, and common/preferred-form metadata; separate from application dependencies. |
| OpenJLPT | Downloaded corrected N5–N1 memberships linked to JMdict IDs; separate from application dependencies. |
| Jiten | Optional selected-title online vocabulary lists and metadata; no Anki data uploaded. |
| Python / SudachiPy / SudachiDict-core | Separately installed optional local text morphology; versions recorded per corpus. |
| AnkiConnect API v6 | Local HTTP bridge for live collection imports while Anki is open, explicitly requested note creation, and live checks. Closed-file imports and cached analysis work without it. |
| NuGet | Downloads the project dependencies during restore/build. |
| PowerShell | Runs the Windows publishing script and creates a normal application shortcut through Windows scripting support. |
| Git | Tracks source and documentation changes. Generated and personal files are excluded through `.gitignore`. |
| Codex | Used to vibe code the initial implementation: translating requirements into code and documentation, running checks, and iterating on the result with human feedback. It is a development tool, not a runtime dependency. |

The app also uses standard .NET libraries for JSON serialization, gzip compression, Unicode normalization, time-zone conversion, CSV file writing, and file-integrity checks. It draws charts with WPF directly, so no separate chart library is installed.

## What users need

A self-contained published build includes the required .NET runtime. Open the executable or shortcut on Windows; no SDK, terminal, or browser is needed. Live imports and optional note creation require separately installed AnkiConnect, with Anki running on the matching profile. Closed-file imports and cached analysis need no add-on.

Analysis works locally without a network connection. Word Coverage installs pinned,
checksum-verified JMdict and OpenJLPT reference data through an explicit download;
subsequent matching and reporting work offline. No Anki content is sent to internet services.
Live imports and explicit note creation use standard .NET HTTP against AnkiConnect API v6 at
`127.0.0.1:8765`. It requires no additional application library or web backend.
The reference engine uses standard .NET JSON, HTTP, compression, and SHA-256
libraries. Optional local media morphology runs a separately installed Python /
Sudachi environment. Internet access may also be needed for SDK and NuGet
dependencies when building from source. [Media](media.md) integrates Jiten for
selected-title vocabulary and artwork; cached comparisons work offline.
The dictionary and JLPT pins/checksums are in CoverageReferenceStore.cs; their
versions and licenses are documented in [third-party notices](../THIRD-PARTY-NOTICES.md).
See [dictionary and coverage](dictionary-and-coverage.md) and [AnkiConnect integration](anki-connect.md)
for the data and protocol details. Source changes are built with Codex's assistance;
the running app does not call an AI model.

## What developers need

Use Windows and a .NET 10 SDK. The publishing script prefers an optional SDK under `.tools/dotnet`; otherwise it uses the installed `dotnet` command. An editor such as Visual Studio or Visual Studio Code is optional. The repository does not depend on editor-specific project settings.

Run these commands from the repository root with a suitable SDK available:

```powershell
# Compile the desktop application.
dotnet build src/AnkiStats.App -c Release

# Run the focused synthetic checks.
dotnet run --project tests/AnkiStats.Checks -c Release

# Produce a self-contained executable and shortcut.
.\scripts\publish.ps1
```

The default publish output is `artifacts/AnkiStats`. Keep the executable, notices, and accompanying `LICENSES` folder together. Close a running copy before replacing its executable, or publish to another output folder with the script's `-OutputDirectory` parameter.

## Validation tools and approach

The C# check runner uses temporary SQLite collections and assertions. It checks behavior that can materially affect results or source safety, including distinguishing total answers from ordinary reviews and leaving source bytes unchanged.
Dictionary fixtures cover identities, variants, definitions, denominators, reports,
and corrections. AnkiConnect fixtures simulate live inventory/writes without
creating real notes; a temporary loopback HTTP listener checks Content-Length and
request framing. No external test framework or running Anki instance is required
for the ordinary suite.

The synthetic UI fixture supports desktop visual inspection without displaying personal collection contents. Set `ANKISTATS_DATA_DIR` to an isolated app-data folder for that workflow; the UI labels the session as a test. Synthetic sources also have a marker file. These are development facilities, not the normal user setup process.

Optional real-data audits are local, explicitly invoked checks. They should emit only the metadata or aggregate counts needed for diagnosis and keep private copies outside the repository. Automated checks do not replace reviewing the rendered desktop interface or comparing a real import with Anki's statistics.

## Dependency boundaries

- The app never migrates or repairs the original collection. Live reads use Anki's own connection through AnkiConnect.
- Python, Node.js, a web backend, and a cloud account are not runtime dependencies.
- AnkiConnect is required for imports while Anki holds an exclusive database lock and for new-note creation. File imports and cached analysis work without it.
- No telemetry, hosted CI, or automatic release pipeline is configured.
- Dictionary downloads, Common/JLPT coverage and initial Jiten/local-text media integration are implemented; see [Media](media.md) for limits.

For redistribution details, consult [third-party notices](../THIRD-PARTY-NOTICES.md) and the included license texts. For the code layout, see [repository structure](repository-structure.md).
