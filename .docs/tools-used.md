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
| NuGet | Downloads the project dependencies during restore/build. |
| PowerShell | Runs the Windows publishing script and creates a normal application shortcut through Windows scripting support. |
| Git | Tracks source and documentation changes. Generated and personal files are excluded through `.gitignore`. |
| Codex | Used to vibe code the initial implementation: translating requirements into code and documentation, running checks, and iterating on the result with human feedback. It is a development tool, not a runtime dependency. |

The app also uses standard .NET libraries for JSON serialization, gzip compression, Unicode normalization, time-zone conversion, CSV file writing, and file-integrity checks. It draws charts with WPF directly, so no separate chart library is installed.

## What users need

A self-contained published build includes the required .NET runtime. To use it, open the executable or shortcut on Windows; no SDK, terminal, browser, Anki add-on, or separately launched server is needed.

Analysis works locally without a network connection. Word Coverage installs pinned,
checksum-verified JMdict and OpenJLPT reference data through an explicit download;
subsequent matching and reporting work offline. No Anki content is transmitted.
The reference engine uses standard .NET JSON, HTTP, compression, and SHA-256
libraries, with no morphology runtime. Internet access may also be needed for SDK
and NuGet dependencies when building from source. No media service is integrated.

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

The synthetic UI fixture supports desktop visual inspection without displaying personal collection contents. Set `ANKISTATS_DATA_DIR` to an isolated app-data folder for that workflow; the UI labels the session as a test. Synthetic sources also have a marker file. These are development facilities, not the normal user setup process.

Optional real-data audits are local, explicitly invoked checks. They should emit only the metadata or aggregate counts needed for diagnosis and keep private copies outside the repository. Automated checks do not replace reviewing the rendered desktop interface or comparing a real import with Anki's statistics.

## Dependency boundaries

- No Anki application code or API is used to open or migrate the original collection.
- No AnkiConnect add-on, Python environment, Node.js runtime, web backend, or cloud account is required by the application.
- No telemetry, hosted CI, or automatic release pipeline is configured.
- Dictionary downloads, Jisho-aligned coverage, and media integrations are future work.

For redistribution details, consult [third-party notices](../THIRD-PARTY-NOTICES.md) and the included license texts. For the code layout, see [repository structure](repository-structure.md).
