# Third-party components

The Windows build uses the following components. No Anki application code or Japanese dictionary data is bundled.

- .NET runtime and Windows Presentation Foundation, Microsoft and contributors, MIT license. [Runtime license](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT), [WPF license](https://github.com/dotnet/wpf/blob/main/LICENSE.TXT).
- Microsoft.Data.Sqlite, Microsoft and contributors, MIT license. [License](https://github.com/dotnet/efcore/blob/main/LICENSE.txt).
- SQLitePCLRaw, Eric Sink and contributors, Apache License 2.0. [License](https://github.com/ericsink/SQLitePCL.raw/blob/master/LICENSE.TXT).
- SQLite, public domain. [Copyright and public-domain dedication](https://sqlite.org/copyright.html).

Exact package versions are pinned in the project files. License texts and .NET's additional third-party notices are included in the accompanying `LICENSES` folder. Keep that folder with the executable and this summary.

## Optional local vocabulary reference data

The app's explicit reference installation downloads these public datasets. No
Anki content is sent. Reference data is separate from the application code.

- JMdict, © the Electronic Dictionary Research and Development Group (EDRDG),
  started by Jim Breen; used under [EDRDG's licence](https://www.edrdg.org/edrdg/licence.html),
  CC BY-SA 4.0. [JMdict project](https://www.edrdg.org/jmdict/j_jmdict.html).
- [jmdict-simplified](https://github.com/scriptin/jmdict-simplified), scriptin and
  contributors; English release `3.6.2+20261026175559`, preserving JMdict's license.
- [OpenJLPT](https://github.com/evanclan/OpenJLPT), contributors, revision
  `0d1d3410bec90bd4098a7c72de820543cb4f707c`, CC BY-SA 4.0, using
  [Jonathan Waller's JLPT Resources](https://www.tanos.co.uk/jlpt/) (CC BY) and
  JMdict for vocabulary classifications and links. Upstream provenance is retained
  in [OpenJLPT-NOTICE](LICENSES/OpenJLPT-NOTICE.md). The app uses vocabulary
  memberships only; it does not redistribute example sentences or kanji datasets.

The local index condenses English glosses, retains spelling/reading restrictions,
and unions level memberships by dictionary ID. Derived reference word lists,
including the reference portions of Markdown reports, use
[CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0/); the
[license text](LICENSES/CC-BY-SA-4.0.txt) accompanies the app. Reports include source
attribution. Personal collection facts and the separately licensed application
code are not represented as source dictionary data.

Updates are pinned in `CoverageReferenceStore.cs` with content checksums; the
reinstall command reuses that snapshot. Maintainers should periodically review
upstream changes and publish an updated pin, validate mapping changes, and retain
attribution and license materials. Do not substitute a software library's license
for its dataset's license. OpenJLPT's corrected labels can differ from Jisho and
are unofficial study references, not official JLPT exam inventories.
