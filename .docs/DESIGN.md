# Anki Stats — Current Design

Status: Phase 1A and Phase 1B are implemented. This document describes the current
application and its rules. [FUTURE-PLANS.md](FUTURE-PLANS.md) contains unimplemented
work and ideas; [README.md](../README.md) explains setup and use.

For step-by-step operation, see the [user's guide](USER-GUIDE.md). For implementation
walkthroughs, see [how the program works](program-guide.md),
[dictionary and coverage](dictionary-and-coverage.md), and
[AnkiConnect integration](anki-connect.md).

## 1. Product and completed scope

Anki Stats is a local Windows desktop application for exploring a Japanese Anki
collection. Anki remains the source of truth. The app measures vocabulary
inventory, dictionary coverage, and recorded study activity, and can explicitly
create new notes for missing vocabulary.

| Area | Implemented behavior |
| --- | --- |
| Desktop | Native dark WPF interface, Japanese text, tables, charts, and a development shortcut that builds the current checkout |
| Setup and import | Remembered Anki profile, editable deck selections and field mappings, safe snapshots with Anki open or closed, cached recovery after failure |
| Deck profiles | Named selections, Japanese/Kanji/combined presets when available, shared mappings and time zone, synchronized active-profile selectors |
| Vocabulary | Distinct expression/reading inventory, search, sorting, supporting note/card IDs, CSV export |
| Word Coverage | JMdict common-word and corrected OpenJLPT N5–N1 coverage, cumulative unions, Markdown missing-word reports, match corrections, conservative stem families |
| Note creation | Previewed batches from missing reference entries, fixed study deck, live duplicate checks, tag-free notes, interrupted-attempt recovery through AnkiConnect |
| Reviews | Daily/weekly answer volume, Again rates and recorded response times by time bucket, card rankings |
| Calendar | Current/historical review streaks and heatmaps using a fixed 4 AM study-day cutoff |
| Time Analysis | Recorded hours, daily/weekly charts, calendar-day averages, mean/median answer time, weekday/hour tables |
| Learning Patterns | History-backed difficulty candidates, recovery groups, cross-deck comparisons, inspectable reading/form/meaning associations |
| Insights | A small library of daily discoveries, stable daily selection, seven-day repeat protection, explicit scope and calculation |

Phase 1A delivered the desktop/import/inventory/review foundation. Phase 1B added
reference coverage, matching and reports, and the explicitly requested new-note
workflow. Calendar, deck profiles, time analysis, and learning patterns are also
implemented. This does not imply that the broader roadmap is complete.

There is no standalone dictionary browser, sentence tokenizer, media library,
media-script ingestion, grammar-pattern inventory, or general kanji-knowledge
score in the current app. Existing comparisons involving kanji cards do not
constitute those broader analyses.

## 2. Collection selection and user experience

Setup selects a local Anki profile folder containing `collection.anki2`, inspects
its metadata, maps note types as Vocabulary, Kanji, Grammar, or Ignored, and maps
expression, reading, and meaning fields. Preview precedes saving and refresh.
New, learning, review, suspended, and buried cards are included when their decks
are selected. Grammar/Kanji notes can contribute review activity without inflating
vocabulary inventory.

Selecting a parent deck includes descendants unless a more specific rule excludes
them. New descendants inherit those rules. Unrelated deck trees are not silently
included. Named deck profiles have stable IDs, names, enabled states, and their
own include/exclude rules. Field mappings and review time zone are shared.

The importer acquires the union of saved profiles once, including disabled
profiles. Disabled profiles are absent from analysis pickers and overview charts.
Switching the active profile filters the cached import without rereading Anki.
Overlapping membership never duplicates a record within one selection. Japanese,
Kanji, and combined presets are created when the corresponding trees exist;
previous selection rules are retained. Settings changes take effect with a
successful import, and a failed refresh retains the previous cache's scope.

Navigation contains Overview, Vocabulary, Word Coverage, Reviews, Insights,
Calendar, Time Analysis, Learning Patterns, and Settings. Overview links to
details and displays a review-rhythm chart for each enabled deck profile. The
header and Calendar profile selectors stay synchronized. Daily discovery history
is separate per collection and deck-profile ID.

Startup shows cached results while attempting refresh. Inspection, settings
preview/save, and refresh support Anki open or closed. Failures display recovery information and retain the latest successful
scope and timestamp. The source profile is visible; isolated or synthetic sessions
have a prominent warning and distinct window title. Refresh reads local data and
does not synchronize AnkiWeb.

## 3. Vocabulary inventory and reference coverage

### Counting units

Cards, notes, normalized vocabulary forms, dictionary entries, and stem families
are different units. A vocabulary note needs at least one included card and a
mapped nonempty expression. Multiple sibling cards do not multiply vocabulary.
Repeated notes with the same normalized expression and reading form one inventory
entry with supporting IDs. Normalization removes presentation markup, surrounding
whitespace, and conservative Unicode differences; it does not silently merge
kana/kanji variants or different dictionary entries in the inventory.

Dictionary coverage uses unique JMdict entry IDs. Accepted spelling/reading
variants can support the same entry once. Reference coverage is separate from
inventory totals and from the limited stem-family grouping.

The **All entries** coverage row uses every entry in the installed English JMdict
snapshot as its denominator. Its Known/Familiar/Combined/In database numerators
reuse existing distinct matched-ID sets for the active profile, with no extra
Anki reads or matching pass. Overview repeats the In database fraction. Entries
outside Common/JLPT membership remain part of this overall denominator.

| Cohort | Current definition |
| --- | --- |
| Known | Supported by vocabulary cards in 日本語::永遠 and descendants |
| Familiar | Supported by vocabulary cards in 日本語::勉強してる and descendants |
| Combined | Union of Known and Familiar, deduplicated by dictionary entry |
| In database | Matched vocabulary in the active imported selection, including 日本語::Suspended and other database-only decks |

These cohorts follow deck membership regardless of queue state. Known and
Familiar can overlap; Suspended establishes neither. Excluded decks are outside
coverage statistics. The separate card/note database totals include unresolved
vocabulary too. Presence in Anki and entry coverage are not proof of mastery of
every sense, comprehension, or fluency.

### Reference sources

The app downloads pinned, checksum-verified full English JMdict data through
jmdict-simplified and corrected OpenJLPT vocabulary memberships. Common status
uses JMdict priority markers `news1`, `ichi1`, `spec1`, `spec2`, and `gai1`.
OpenJLPT's corrected classifications were explicitly chosen over exact live
Jisho tags where they differ. Exact Jisho parity is not claimed. Source versions,
attribution, rules, and unlinked source-row counts are inspectable.

Each Common/N5–N1 denominator counts unique available linked dictionary entries.
An entry may have multiple memberships. Cumulative rows use unions: Through N3
means N5 ∪ N4 ∪ N3. Unlinked JLPT rows are disclosed and excluded rather than
assigned guessed identities. Reinstall reuses pinned snapshots; newer pins
require an app update. Failed downloads preserve the previous reference.

### Matching and correction

Vocabulary cards use dictionary forms. The parser supports whole-word and segment
furigana, whitespace before annotations, numeric headwords, and explicit alternative
readings such as `昨夜[ゆうべ・さくや]`. Written script is preserved before kana-folded
fallback. Dictionary spelling/reading restrictions apply. Separated phonetic
segments join only when no alternative matches separately and a unique exact-form
dictionary reading supports the joined result. Incidental words in example
sentences do not contribute vocabulary coverage.

Any one compatible spelling/reading and one supported sense suffice. `ありがとう`
and `有難う` are alternatives, as are the readings of an entry such as `はい`;
all permutations and all glosses are not required. Explicit alternatives can
support multiple entries; repeated readings of one entry count once.

English-definition evidence resolves Japanese-compatible homophones using
normalized phrases and limited short-phrase containment. It preserves negation,
rejects generic or tied evidence, and never matches a Japanese-incompatible entry
on English alone. A unique Japanese match with unverified English wording retains
coverage with a Definition warning. Paraphrases are not presumed incorrect.

A unique exact kanji spelling can retain coverage with a Reading warning for one
reading edit when readings have at least four characters. Supported edits are
substitution, insertion, deletion, and adjacent transposition. Headwords are never
fuzzy-matched, and equally close candidates remain unresolved. Partial matches
credit only resolved reading alternatives. Warnings remain inspectable in Needs
review; ambiguity does not silently increase coverage.

Match review supports choosing candidates, entering verified JMdict IDs,
explicitly linking multiple studied alternatives, excluding a note, and resetting
a correction. Decisions are collection/note-specific and app-owned. Current
fingerprints include expression, readings, and English definition; changes or
removed dictionary IDs require review. Older fingerprints remain readable.
Created notes retain their recorded dictionary identity with the same staleness
checks. Corrections do not modify Anki.

Stem families cover confirmed Combined entries. 報う／報いる／報われる form one
curated family while remaining separate dictionary/inventory entries. Other
entries stay separate unless explicitly assigned a shared family label. No broad
family inference is made from shared kanji, glosses, or morphology. Family labels
never alter reference coverage, and unresolved relationships limit family totals.

Markdown reports are generated on request to a chosen file. They distinguish
Not covered by Known/Familiar from Absent from imported database, and possible
matches from entries with no supporting vocabulary. Reports include lookup forms,
optional spelling/reading alternatives, glosses, IDs, source versions, and scope.
Unresolved candidates are not presented as proven absent.

## 4. Explicit new-note creation

A positive requested number selects random unique missing entries in this order:
N5 → N4 → N3 → N2 → N1 → Common. Selection spills into subsequent tiers and
deduplicates overlapping memberships. Confirmed database entries, unresolved
possible matches, live duplicates, and pending/acknowledged creation identities
are excluded. A smaller remainder is offered when necessary; no eligible entries
produces an explicit exhaustion error.

Preview displays the concrete batch before an explicit Create action. New notes
always use the existing Japanese note type in 日本語::勉強してる. Front uses a
JMdict common written form with a compatible reading, or kana for usually-kana
entries/common kana alternatives: `食べる[たべる]`, but `ありがとう`. These are
dictionary preferences, not a measured frequency ranking. English contains
condensed glosses and a hidden attribution/entry-identity comment. Other fields,
including Sentence and Media, are blank. New notes have no tags. Anki's templates
determine the number of generated cards; daily limits and schedules are unchanged.

Anki must be open on the imported profile with AnkiConnect enabled. Requests use
API v6 at `127.0.0.1:8765` with buffered JSON and Content-Length. An optional API
key stays in memory. The app checks profile, imported Japanese model ID, fields,
and destination deck, reads all live notes for mapped-vocabulary duplicate
analysis across the entire collection, and rechecks before creation. This live
scope is broader than the active profile's coverage statistics.

Each attempt is journaled locally before sending. Acknowledged IDs and matching
fingerprints persist. Uncertain responses stop the batch without automatic write
retries. Check pending additions reads live identities to acknowledge existing
notes or release absent attempts; it creates/deletes nothing. Hidden attribution
supports recovery without tags; legacy identity tags remain readable and may be
removed from old notes. Refresh recovers acknowledged matching identities from
the journal if the app closed before saving them separately.

Creation automatically refreshes the import after a completed or partial batch,
with Anki still open. Refresh failure preserves the creation outcome and offers
manual Refresh recovery; coverage comes from imported records. Existing notes, tags, and scheduling settings are not
edited. Isolated/synthetic sessions cannot create notes. The first preview can
upgrade the pinned reference data to include preferred study forms.

## 5. Review and study-time measurements

All scoped review metrics join historical events to included, currently existing
cards. Deck attribution reflects current membership, not historical deck placement.
Unresolvable deleted-card events are excluded from scoped results; collection-wide
orphan counts are disclosed. Learning, ordinary review, relearning, filtered/cram,
and administrative records remain distinct. Unknown, invalid, and future records
do not silently enter eligible measures.

| Measure | Definition |
| --- | --- |
| Overview total answers | All eligible rated-answer kinds, with learning/review/relearning/filtered breakdown |
| Overview Again rate | Again ÷ ordinary-review answers |
| Review volume | Daily/weekly counts in the selected rated-answer scope and date range |
| Review time buckets | Again rate and median positive recorded answer duration for the selected scope, with sample counts |
| Card rankings | Ordinary-review-event totals and Again answers, separately from Anki's current lapse counter |

Reviews offers All history, Last 30 days, and Last 90 days. Local time buckets
are 00:00–05:59, 06:00–11:59, 12:00–17:59, and 18:00–23:59. The configured time
zone handles daylight saving; historical travel time zones are unavailable.
Fewer than 30 eligible answers is labeled limited data. Empty denominators show
No data. Differences are descriptive and do not establish causal study advice.

Calendar uses consecutive rated-answer study days with a fixed 4 AM cutoff in the
selected time zone. A complete empty day breaks a streak; the current day remains
open until its next cutoff. Historical streak ranges span years and scroll
horizontally with seven weekday rows. Heat colors use five shades within blue
(1–499), purple (500–999), and gold (1,000+) bands; gold scales to the selected
streak's maximum. Custom Anki cutoffs are not imported. Reviews and existing daily
insights retain midnight-based calendar dates.

Time Analysis sums positive recorded durations in 64-bit milliseconds across all
eligible rated kinds. Ranges are All history, Last 30 days, Last 90 days, and Last
6 months; six months starts on the current study date minus six calendar months,
inclusive. Daily/weekly charts include averages and exact tables. Weekdays use
4 AM study dates; hour buckets use local clock hours and combine repeated DST
hours. Weeks run Monday–Sunday. Mean/median use positive-duration answers only;
missing durations retain their activity counts.

Calendar-day averages include zero-review days from the later of the selected
start or first available answer through today. Weekly averages equal seven times
the daily average. Today and edge weeks can be partial. No positive durations
means No data. Durations are Anki's recorded, potentially capped answer times,
not uncapped wall-clock study time.

Learning Patterns flags at least three historical Again answers during ordinary
reviews. It distinguishes reconstructed all-history/six-month counts from current
Anki lapse counters. 永遠 is known context and excluded from difficulty rankings;
Seen in Immersion is ignored. 日本語::Suspended and 漢字::Relearn are recovery
cohorts even without enough history; 漢字::Unused and Grammar are excluded.
Recovery placement alone is not evidence of repeated failure.

Cross-deck analysis uses imported Japanese and Kanji trees regardless of the
active card-list scope. It compares explicit readings, reliably aligned
single-kanji furigana, one-character written-form differences, and normalized
English gloss overlap. Generic glosses and kana endings are suppressed. Findings
require distinct history-flagged words, with up to 150 ranked groups per type.
Below-threshold meaning cards need at least ten ordinary answers for a
vocabulary-only comparison. Ambiguous compound readings remain unresolved.
Supporting IDs, reasons, counters, and recent answers are inspectable. These
associations cannot identify which component caused a failed answer.

## 6. Daily discoveries

The implemented small insight library uses available vocabulary/review facts,
including card rankings, busiest dates and weekdays, midnight-based streaks,
time-bucket shares, consistency, and repeated vocabulary notes. Each observation
explains its calculation, scope, date range, time zone, and answer kinds.
Consistency includes inactive dates in its stated first-to-last-answer span.

A topic stays stable for the local calendar date while refresh updates its values.
Persisted history prevents repetition within seven calendar days, separately per
collection/deck-profile ID. A timer checks day changes. Insufficient data or an
exhausted eligible pool produces an explanation instead of fabricated findings.
The larger insight library and frequency-backed rarity findings remain roadmap
items rather than completed features.

## 7. Import safety, persistence, and networking

The file importer copies idle collections under an exclusive read-only handle
and refuses nonempty journals. SQLite opens only the private copy, never the
original collection or source sidecars. When Anki holds the file or has an active
journal, CollectionReader falls back to read-only AnkiConnect enumeration. Live
imports verify the running profile, preserve home-deck membership, and reject
detected record changes before replacing the cache. They reuse mapping validation
and extraction through an app-owned in-memory schema. API reads span multiple
requests, so they are not a database transaction. Orphaned review counts are
unavailable via that API and explicitly labeled. Isolated/synthetic sessions
cannot fall back to the user's Anki. Supported file collection
schemas are 11 and 18. The compatibility adapter handles required metadata and
collations without launching Anki's collection API. Successful imports replace
the app cache transactionally; failures retain previous results. Temporary copies
and sidecars are cleaned up. Source files are never repaired, migrated,
checkpointed, or written directly.

Settings, the latest imported data, reference bundles, personal matching/family
decisions, creation journals, and insight history live in per-user AnkiStats app
data. Historical knowledge snapshots are not stored. User paths, note content,
collections, settings, exports, and generated analysis do not belong in this
public repository. Tests use marked synthetic data; real-data audit output stays
local and private.

CSV exports use current filters, UTF-8, quoting, and spreadsheet-formula protection.
Markdown exports carry reference attribution. Neither path executes note HTML or
JavaScript, imports media, or follows remote content in fields.

There is no telemetry or cloud account. Internet requests download explicit public
reference snapshots without collection content; live importing and note creation
talk to loopback AnkiConnect. Cached analysis and matching work offline after reference installation.
Reference data and derived lists retain source/version and CC BY-SA attribution;
see [THIRD-PARTY-NOTICES.md](../THIRD-PARTY-NOTICES.md). Newly created notes can sync
normally when Anki itself synchronizes.

## 8. Implementation and validation

The implementation uses C#, .NET 10, WPF/XAML, and Microsoft.Data.Sqlite. WPF draws
charts directly. Code-behind partials coordinate the desktop UI; the Core library
contains import, analysis, matching, persistence, and AnkiConnect behavior. The
check runner exercises Core without requiring real Anki. There is no web backend
or morphology runtime. Reference data is a compressed local JSON index, while
app-owned settings and cached import data use SQLite.

Long operations run off the UI thread and tables use virtualization. The normal
development shortcut rebuilds/publishes the current checkout before opening;
standalone packaged builds are snapshots. Self-contained Windows builds include
the runtime and accompanying notices. Performance must be measured before making
startup/refresh timing promises; no benchmark guarantee is claimed.

Validation covers metric scopes, dictionary variants and definitions, corrections,
preferred forms, tier spillover, duplicate prevention, interrupted writes,
HTTP request framing, source integrity, locking/WAL failures, schema rejection,
time boundaries, exports, and cache isolation. UI checks use isolated sessions.
Live AnkiConnect preview was verified, and the user tested actual creation.
The last application validation passed 339 checks and a Release build; future
changes must rerun relevant validation rather than treating that count as a promise.

Follow the [development workflow](development-workflow.md) for branch,
validation, commit, and PR steps. Full installation infrastructure, hosted CI,
and automated releases are not configured. See the [repository structure](repository-structure.md)
and [tools guide](tools-used.md) for code responsibilities and commands.
