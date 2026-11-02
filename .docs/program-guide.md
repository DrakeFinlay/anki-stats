# Anki Stats: a practical guide

Anki Stats is a Windows desktop app that helps you explore the Japanese vocabulary and review history in a local Anki collection. It answers questions such as: how many different vocabulary entries are in the decks you selected, when you tend to review, which cards receive the most reviews, and which cards receive the most Again answers.

The current version is Phase 1A. It measures collection inventory and recorded study activity. Dictionary coverage, shared stem families, JLPT lists, grammar-pattern analysis, and media recommendations have not been implemented yet.

## A few Anki terms

| Term | Meaning |
| --- | --- |
| Profile | The local folder containing an Anki user's collection and related files. |
| Deck profile | An app-specific named deck selection, such as 日本語, 漢字, or 日本語 + 漢字. These all read the same connected Anki collection. |
| Collection | The database containing notes, cards, decks, and review history. The main file is `collection.anki2`. |
| Note | Stored information, such as a word and its meaning. |
| Card | A question generated from a note. A note can generate more than one card. |
| Note type | The definition of a note's fields and card templates. |
| Review event | A recorded answer. Answering the same card repeatedly creates multiple events. |
| Field mapping | Your choice of which note fields contain the expression, reading, and meaning. |

These distinctions matter. Ten notes do not necessarily mean ten different words, and ten thousand answers do not mean ten thousand different cards.

## Connect your collection

1. Close Anki completely. The app needs a safely available collection to refresh.
2. Open the published `AnkiStats.exe` or its shortcut.
3. In Settings, choose **Browse…** and select the profile folder containing `collection.anki2`.
4. Select **Inspect profile** to load the available decks and note types.
5. Include the deck tree you want to analyze. Descendants inherit a parent's rule. An explicit exclusion omits a subtree; unrelated decks remain excluded.
6. Assign note-type categories and confirm their fields. Vocabulary contributes inventory and reviews. Grammar and Kanji contribute reviews without becoming vocabulary entries. Ignored types contribute neither.
7. Check the review time zone.
8. Choose **Preview selection**. Inspect the counts and example expressions. If fields contain bracket readings such as 漢字[かんじ], use the explicit bracket-reading option when appropriate; it removes annotations from the expression and does not create a separate reading field.
9. Choose **Save and refresh**.

The app remembers this configuration. Later launches refresh automatically. A refresh reads what is already present in the local collection; it does not synchronize AnkiWeb or pull reviews directly from another device.

## Read the overview

**Vocabulary entries** counts distinct normalized expression/reading pairs in mapped vocabulary notes. Repeated notes with the same pair count once, even when they generate several cards. New, learning, review, suspended, and buried cards are included by default within the chosen scope.

This is an inventory count, not a claim that every entry is mastered. 報う, 報いる, and 報われる remain three entries. Grouping them into a shared stem family is planned for a later phase.

**Total review answers** includes rated learning, ordinary review, relearning, and filtered/cram events. The breakdown explains how the total was formed. Manual scheduling operations and other administrative records are excluded. Ordinary reviews alone are a smaller subset, so comparing that subset with Anki's overall answer total would be misleading.

**Again rate** on the overview uses ordinary review answers only. It is the number rated Again divided by the number of eligible ordinary review answers. It is not a general fluency or comprehension score.

**Today's discovery** shows one observation, with its calculation and scope on the card. For example, consistency states how many dates had answers, the number of dates between the first and last answer, and the resulting percentage. Calendar dates run from midnight to midnight in the displayed time zone. Anki can use a different study-day boundary, so day counts and streaks may differ even when answer totals agree.

The discovery topic stays the same for a day while refreshed values can change. A template cannot repeat within seven calendar days. Collections with too little data may show an explanation instead of a new observation.

Always check the source profile, included deck rules, and last successful refresh time. A test-session banner identifies isolated development data; those numbers must not be interpreted as your collection's statistics.

## Explore details and export data

Use **Active profile** in the header to choose the named deck selection for all summary cards, detail pages, discoveries, and exports. Overview keeps a separate Review rhythm chart for every enabled deck profile. Calendar has a synchronized profile selector and remembers the chosen streak range per profile while the app is open. A combined profile adds answers from both decks, and either deck can keep its streak active.

Manage deck profiles in Settings: select a profile row, edit its include/exclude rules below, and preview and save. Names and enabled states can be edited in the table; Add profile copies the selected rules as a starting point. Turning a profile off removes its chart and active-selection option. Switching between saved profiles uses cached data; Refresh is only needed to obtain new Anki activity. Field mappings and time zone apply to all profiles.

In **Vocabulary**, search by expression, reading, meaning, or deck. Click column headings to sort. Double-click an entry to inspect its supporting note and card IDs. The skipped-expression view identifies mapped vocabulary notes whose extracted expression is empty. Export CSV saves the current search results and sort order.

In **Reviews**, select a date range and event kind. The default event filter is ordinary review; choose all rated answers when comparing with the overview's total. The chart can display daily or weekly volume. Time-of-day rows show answer counts, Again rates, and median recorded answer durations. A median is the middle duration after sorting, which reduces the influence of unusually long answers. Zero or missing durations are excluded from timing calculations.

Small samples are labeled as limited data. Response durations may be capped by Anki, and differences between time buckets can reflect card difficulty or workload. They do not establish that studying at a particular hour causes better memory.

Card rankings always use ordinary reviews in the chosen date range. The Again count is separate from the current card's lifetime lapse counter. The review CSV identifies daily rows, time buckets, and rankings so their meanings remain distinguishable.

## How the program was written

The program is written in C#. WPF supplies the Windows interface, XAML describes its layout and styling, and SQLite supplies database access. The code is divided into a desktop project, a core library, and an executable check runner. The desktop project references the core library, so calculations can also run without opening a window.

### From a button click to a screen update

[MainWindow.xaml](../src/AnkiStats.App/MainWindow.xaml) defines the pages, buttons, tables, and named text controls. Its companion file, [MainWindow.xaml.cs](../src/AnkiStats.App/MainWindow.xaml.cs), contains event handlers: methods WPF calls when something happens in the window.

For example, the Refresh button calls `RefreshClick`, which runs `RefreshInternal` through a shared `Guard` method. `Guard` disables conflicting controls, shows progress, catches errors, and restores the controls afterward. Startup follows the same refresh path after loading settings and any cached import. Without saved settings, it opens the setup page.

The window uses **code-behind**, meaning its event-handling C# lives alongside its XAML. Table columns use **data binding**: a column names a property such as `Expression`, and WPF displays that property for each row object. Other controls are updated explicitly, for example:

```csharp
ReviewCount.Text = overview.TotalAnswers.ToString("N0");
```

Here, `overview` holds calculated results, `ReviewCount` is a named text control, and `N0` formats an integer with thousands separators.

### Reading and selecting Anki data

[CollectionImporter.cs](../src/AnkiStats.Core/CollectionImporter.cs) handles collection access. Its `WithPrivateCopy` method acquires a read-only file handle, checks for active journals, copies the collection to a temporary file, and opens SQLite against that copy. A `finally` block attempts to remove the temporary database and its sidecar files whether reading succeeds or fails.

`ReadMetadata` first checks the schema version. Schema 11 stores deck and note-type definitions as JSON; schema 18 uses separate tables. The importer translates both layouts into the same C# data types, so the analysis code does not need a separate implementation for each schema.

`Import` then reads the `cards`, `notes`, and `revlog` tables. Their IDs connect the records: a card refers to a note, and a review event refers to a card. The code uses dictionaries and hash sets to look up selected IDs efficiently rather than repeatedly searching every record.

Deck rules and field mappings determine what survives that selection. A parent deck rule applies to descendants unless a more specific rule overrides it. Ignored note types are omitted. For included notes, the importer splits Anki's stored field string and extracts the mapped expression, reading, and meaning. [Models.cs](../src/AnkiStats.Core/Models.cs) defines the resulting note, card, review, settings, and result objects.

### Turning records into statistics

[Analysis.cs](../src/AnkiStats.Core/Analysis.cs) contains three responsibilities: field-text cleanup, analysis, and CSV output.

`TextNormalizer.Clean` removes presentation markup and sound references, decodes HTML entities, and normalizes Unicode text. Removing bracket readings is an explicit mapping option. It does not use a dictionary or infer stem families.

`Analyzer.Analyze` groups vocabulary notes by expression and reading using LINQ, C#'s collection-query methods:

```csharp
.GroupBy(n => (n.Expression, n.Reading))
```

Each group becomes one vocabulary row. The row retains supporting note/card IDs, so the displayed total can be inspected. Different expressions remain separate even if they share a stem.

For reviews, the analyzer excludes administrative events and invalid ratings, converts timestamps to the selected time zone, and applies the date range. It then separates the chosen answer kinds from the ordinary-review subset. Grouping by date produces daily totals; grouping by local hour produces the four time-of-day buckets. Answer durations are sorted to calculate medians. Card IDs connect ordinary-review counts and Again counts to the ranking rows.

The method returns an `AnalysisResult` containing vocabulary rows, daily totals, time buckets, rankings, and discovery candidates. The window assigns those rows to tables. [ReviewChart.cs](../src/AnkiStats.App/ReviewChart.cs) draws the volume chart with WPF rectangles, lines, and labels; weekly grouping happens before drawing.

### Keeping the window responsive

Importing and analysis can take longer than a button click should block the interface. The window uses `Task.Run` to perform that work on a background thread and `await` to resume when it finishes:

```csharp
var next = await Task.Run(() =>
    new CollectionImporter(store.TemporaryRoot).Import(settings, progress));
```

While this runs, WPF can continue drawing the window and processing normal interface activity. The code computes the new results and saves the cache before replacing the window's current data. If importing fails, the error handler can keep showing the previous successful import.

### Saving results and choosing discoveries

[LocalStore.cs](../src/AnkiStats.Core/LocalStore.cs) maintains an app-owned SQLite database with three tables: `settings`, `cache`, and `insight_history`. Settings are serialized to JSON. Imported records are serialized to JSON, compressed with gzip, and saved as a binary value in the cache table. Loading reverses those steps and checks the collection identity before returning cached records.

`SaveCache` uses a transaction: the database commits the new result as one operation. When a new connection is saved, its settings are written in that same transaction. This prevents a partially saved connection from becoming the stored result.

`SelectInsight` checks whether today's date already has a chosen template. If so, it returns that template with freshly calculated values. Otherwise, it removes templates used in the preceding seven dates, chooses from the remaining eligible candidates using the date, and saves the selection. This is date-based selection with persisted history, rather than an unrestricted random choice. A timer checks for a new local date while the app remains open.

### Exporting and checking behavior

`CsvExport` writes UTF-8 text with quoted cells, escaped quotation marks, and protection against formula-like cell contents. The window supplies the filtered vocabulary rows or the labeled review sections selected for export.

The separate [check runner](../tests/AnkiStats.Checks/Program.cs) builds synthetic collections, calls the core methods directly, and asserts expected results. It tests calculations and failure paths without requiring clicks in the GUI. These checks include duplicate handling, answer-kind separation, time boundaries, discovery rotation, cache behavior, locked sources, and unchanged source-file bytes.

For a file-by-file map, read [repository structure](repository-structure.md). For dependencies and build commands, read [tools used](tools-used.md).

## What happens to your data

The app opens the source file with read-only file access and denies concurrent access while making a private copy. SQLite queries run against the copy, not the original. The importer refuses active journals or an unavailable source instead of attempting to repair or alter Anki's files. Temporary copies are removed after the operation.

The app never changes Anki tags, schedules, or cards and never executes HTML or JavaScript from note fields. The running app has no telemetry or network dependency.

Settings and imported data live in the per-user local application-data `AnkiStats` folder, available through **Open app data folder** in Settings. The cache holds the latest successful import; it is not a historical snapshot archive or a substitute for an Anki backup. CSV files go wherever you choose to save them.

## If the numbers look wrong

- Check the source profile and any test-session warning first.
- Confirm deck inclusion and exclusion rules, including inherited rules.
- Check categories and field mappings. An ignored type will not contribute, and a grammar type will not increase vocabulary inventory.
- Compare the same answer kinds, deck scope, date range, and time zone. Total answers and ordinary reviews are different measurements.
- Check the last successful refresh time. An error leaves previous results visible with their original scope and timestamp.
- Remember that history is attributed to a card's current deck. Events for deleted cards without recoverable card metadata are excluded; the footer reports collection-wide orphaned events, which are not necessarily part of the selected deck's history.

If refresh fails, close Anki, verify the profile location and folder access, then retry. Renamed or reordered mapped fields require inspecting the profile and previewing updated mappings. Unsupported collection schemas require an application update; do not change the database to make it fit.
