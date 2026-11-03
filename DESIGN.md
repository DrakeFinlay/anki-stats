# Anki Stats — Design and Scope

Status: Phase 1A complete. Phase 1B dictionary coverage approved with the decisions below.

## 1. Product intent

A Windows desktop application for exploring a Japanese Anki collection. The user opens an ordinary application shortcut, the app refreshes from a remembered local profile, and an overview presents useful vocabulary and review information.

The central long-term questions are:

- How much vocabulary is represented in the selected decks?
- How many distinct vocabulary forms and underlying words are represented?
- What percentage of Jisho-style common-word and JLPT lists is represented?
- What do review history and individual cards reveal about study habits?
- What interesting observation can the app show today?

The app is a personal analysis tool. Anki remains the source of truth. Collection contents, paths, profile names, settings, caches, and exports belong outside this public repository.

## 2. Delivery boundaries

### Phase 1A — the initial build

This is the first implementation scope. It must provide a useful app before introducing dictionary matching or linguistic analysis.

| Area | Required behavior |
| --- | --- |
| Desktop experience | Windows application with a shortcut or installer; no user-facing CLI or separately launched server |
| Setup | Select a local Anki profile, decks, and note-type field mappings; remember choices locally |
| Refresh | Refresh automatically at launch and through a manual button; work with Anki closed |
| Overview | Show vocabulary inventory, review summaries, and a featured daily insight |
| Vocabulary | Count distinct vocabulary forms; provide searchable, sortable drilldown tables |
| Reviews | Show volume over time, Again rate by time of day, median answer time by time of day, and most-reviewed/most-lapsed cards |
| Insights | Rotate a small initial set of useful observations without repeating a template within seven calendar days |
| Export | Export the current vocabulary and review tables as CSV |
| Resilience | Preserve the latest successful results when refresh fails; explain how to recover |
| Appearance | Dark interface, English labels, readable Japanese text, overview and drilldowns |

Phase 1A vocabulary counts are inventory measurements. They are not common-word coverage or stem counts. Those labels must not appear as completed metrics until Phase 1B implements them.

### Phase 1B — dictionary coverage and underlying words

Required follow-up work, outside the initial build:

- Local dictionary matching, with Jisho alignment as the preferred reference behavior.
- Common-word coverage as both a percentage and a numerator/denominator.
- A separate stem-family count alongside the vocabulary-entry count, including related dictionary entries rather than only inflectional lemmas.
- Inspectable matched, ambiguous, and unmatched vocabulary.
- Source/version information and a precise definition of each denominator.

#### Phase 1B decisions (November 2, 2026)

- No standalone dictionary search or dictionary-browsing interface. Local reference
  data exists to cross-reference Anki vocabulary; a dictionary app is a separate future project.
- Known vocabulary is supported by cards in `日本語::永遠` and descendants.
  Familiar vocabulary is supported by cards in `日本語::勉強してる` and descendants.
  Combined coverage uses their union. `日本語::Suspended` and descendants cannot
  establish either cohort, but contribute to the separate imported-database metric.
  Other selected vocabulary is also database-only. These rules use named deck
  membership, including cards in any queue state, rather than inferred mastery.
- Coverage follows the active deck profile, the existing Vocabulary category, and
  the successfully imported selection. “In database” does not claim to include
  excluded decks. Counts use unique JMdict entry IDs, deduplicating notes, sibling
  cards, spellings, and overlap between cohorts. Known and Familiar may overlap.
- Common and N5–N1 coverage are included now. Display numerator, denominator,
  percentage, and cumulative unions (Through N3 = N5 ∪ N4 ∪ N3).
- Use full English JMdict via jmdict-simplified, with its common-entry rule
  (`news1`, `ichi1`, `spec1`, `spec2`, `gai1`). Use OpenJLPT's corrected JMdict links
  and classifications. After representative live Jisho comparisons revealed
  differences, the user chose corrected OpenJLPT classifications. Exact Jisho
  alignment remains a preference, not a claim of current parity.
- JLPT denominators count unique available JMdict entries linked by the reference
  source, rather than source rows or spellings. Report unlinked rows explicitly;
  do not guess their identities. An entry may belong to multiple levels.
- Notes use dictionary-form vocabulary, including `word[reading]` and segment
  furigana. Parse expression and reading without changing the inventory. Respect
  dictionary reading/spelling restrictions. Do not tokenize sentences or credit
  incidental vocabulary. Morphology dependencies are unnecessary for this input.
- Matching v2 preserves written script before kana folding, supports explicit
  alternative readings (including separate dictionary entries), whitespace before
  annotations, and numeric headwords. Duplicate readings of one dictionary entry
  count once. Separated phonetic segments join only if no alternative is compatible
  and a unique exact-form dictionary reading supports the joined reading.
- Exact kanji spellings with one uniquely supported reading edit may count with a
  visible reading warning. Require readings of at least four characters; allow
  one substitution, insertion, deletion, or adjacent transposition. Never fuzzy-match
  headwords or select between equally close homographs. Keep these confirmed
  matches in Needs review. Partial multi-reading matches credit only resolved
  alternatives. New correction fingerprints distinguish written scripts; legacy
  saved decisions remain readable.
- Matching v3 adds English-definition evidence among Japanese-compatible entries.
  Prefer an exact normalized gloss phrase over limited short phrase containment;
  require a unique best candidate, retain negation, and suppress generic-word or
  tied evidence. Meaning alone cannot match an incompatible Japanese form. A
  unique Japanese match whose English wording is unverified retains coverage with
  a definition warning, rather than assuming arbitrary paraphrases are wrong.
  New correction fingerprints include the definition; older decisions remain readable.
- Any one accepted spelling/reading and one supported sense suffice for entry
  coverage. Missing-word reports lead with a kana lookup form and show all other
  forms as optional alternatives, rather than presenting a rare kanji spelling as
  the required target. Different forms of the same entry never inflate counts.
- Matched, ambiguous, unmatched, excluded, and stale-decision notes are inspectable.
  Only a unique match or saved manual correction receives coverage credit.
  Corrections are app-owned, collection/note-specific, and persist across imports.
  Changed expression/reading or removed reference IDs require another review.
- Missing-word Markdown reports are generated on request to a user-selected file,
  rather than during builds. Distinguish “not covered by known/familiar” from
  “absent from imported database.” Include expression, reading, gloss, JMdict ID,
  database status, scope, versions, and unresolved-match caveats.
  Separate possible matches needing review from entries with no supporting
  vocabulary. A lack of a confirmed match must not imply proven absence.
- Stem-family grouping starts conservatively: spelling variants share dictionary
  identity; the approved 報う／報いる／報われる group is explicitly curated; other
  entries remain separate unless given the same manual family label. Display the
  rules and limited relationship coverage. Do not infer families from shared kanji,
  glosses, generic cross-references, or transitive/intransitive morphology. Family
  grouping never changes dictionary-entry coverage numerators or denominators.
- Reference installation is an explicit one-time public-data download, followed
  by offline use. Pin source revisions and SHA-256 checksums. Keep reference data
  separate from personal decisions; failed downloads preserve the prior snapshot.
  Reinstall uses the pinned snapshot; new snapshots are introduced by app updates.
  No card contents, paths, or statistics are transmitted. Include attribution,
  CC BY-SA license material, and source/version details in the app and reports.

#### Explicit note creation extension

The user approved adding missing vocabulary notes within Word Coverage. A positive
requested count selects random unique entries in N5, N4, N3, N2, N1, then Common
order, spilling into later tiers. Only absent entries qualify; confirmed entries,
possible matches, live duplicates, and pending/acknowledged creation identities
are excluded. Live checks cover all mapped vocabulary in the running collection,
including decks outside the active imported profile. Offer a smaller remainder
when necessary and an exhaustion error when no eligible entries remain.

Preview the concrete batch before an explicit Create action. All new notes use
the existing Japanese model and 日本語::勉強してる deck. Front uses a common
written form with a compatible reading, or kana when JMdict marks usually-kana
usage or only a common kana form. English contains dictionary glosses and hidden
source attribution; remaining fields are blank. These preferences do not claim
an exact frequency ranking. Anki generates cards from existing templates. Leave
limits, schedules, existing notes, and existing tags unchanged.

Use AnkiConnect API v6 over loopback with Anki open on the imported profile.
Verify the profile, model ID, fields, and destination; recheck live duplicates
before writing. Journal each attempt before its request and retain dictionary
identity in app-owned matching decisions and the hidden attribution comment in
English. New notes have no tags; older identity tags remain readable. Stop on uncertain
responses without automatic write retries. An explicit read-only reconciliation
acknowledges identified notes or releases absent pending entries. Isolated/synthetic
sessions cannot create notes. Refresh remains a separate read-only workflow with
Anki closed. This narrowly scoped extension supersedes the earlier blanket ban
on Anki modifications; classification/tag updates to existing notes remain deferred.

### Subsequent required capabilities

- Further refinement of Jisho alignment when a usable, permitted source is available.
- Grammar analysis based on explicitly mapped grammar/conjugation cards.
- Kanji analysis from dedicated kanji cards and from vocabulary, kept separate.
- Flexible knowledge filters, including young/mature card criteria.
- Goals and progress forecasts for vocabulary and other supported metrics.
- Broader review analysis and a larger insight library.

These remain part of the product direction; they are not Phase 1A acceptance requirements.

### Optional future capabilities

- Media vocabulary coverage and discovery for shows, manga, novels, visual novels, and similar material.
- A broader knowledge profile or fluency-related estimate.
- Bilingual interface labels.
- Historical snapshots of aggregate knowledge metrics.

### Separate project

Writing dictionary classifications or other tags onto existing Anki notes remains
outside this app's scope. A standalone local dictionary app remains a separate
future project. The explicit new-note workflow above is approved for Phase 1B.

## 3. User experience

### First launch

1. Choose an Anki profile folder using a file picker. Do not require typing a path.
2. Choose included decks and their descendants. The user's intended Japanese deck tree is selected through setup, never hard-coded.
3. Assign note types to vocabulary, kanji, grammar, or ignored categories. Map the vocabulary expression field and, where available, reading and meaning fields.
4. Preview mapping results and counts before saving. Suggest likely fields, but allow correction.
5. Refresh and display the overview.

Initial inclusion includes new, learning, review, suspended, and buried cards in the selected scope. Grammar and kanji cards can contribute to relevant review summaries without inflating vocabulary totals. Excluded decks and ignored note types do not contribute.

Deck selections must be editable. Selecting a parent includes its descendants; users can exclude a child subtree. Newly created descendant decks follow that rule. Unrelated new decks are not silently added. Mapping changes invalidate affected analysis and trigger a refresh.

### Subsequent launches

Saved deck profiles are a follow-up capability. Each profile has a stable identifier, name, enabled state, and independent deck rules. The app imports their union in one protected acquisition, then filters the imported cards, notes, and reviews per profile. Overlapping profiles do not duplicate records within a combined profile. Disabled profiles remain in the import but have no overview chart or active analysis option. A header selector scopes summary cards, drilldowns, insights, exports, and Calendar; the Calendar selector is synchronized with it. Overview shows one Review rhythm chart per enabled profile. The combined profile treats a rated answer in either constituent deck as activity on that study day. Profiles share field mappings and time zone. Existing single selections migrate without losing their rules; the requested Japanese, Kanji, and combined presets are suggested when those deck trees exist. Profile settings are saved with the successful cache, which retains its own profile definitions for failed-refresh recovery. Active-profile changes persist without reimporting. Daily discovery rotation is separate per profile.

Open a responsive application window, show refresh progress, and then present the overview. If cached results exist, they may remain visible during refresh with their timestamp and a refreshing indicator. Replace results together only after successful analysis.

Provide manual Refresh, last successful refresh time, source/selection settings, and visible error recovery. No background polling, system tray process, or desktop notifications are required initially.

"Refresh" reads local collection data. It does not contact AnkiWeb or synchronize other devices. Reviews become visible after they reach the selected local collection.

### Navigation and appearance

Navigation: Overview, Vocabulary, Reviews, Insights, Calendar, Time Analysis, Learning Patterns, and Settings. Calendar is a follow-up feature: it shows current and historical consecutive rated-answer streaks, selected by date range, with seven weekday rows and horizontal scrolling across years. It uses a fixed 4 AM study-day cutoff in the selected time zone; existing review charts and insights retain midnight-based dates. A study day remains open until its next cutoff, so yesterday's streak stays active while today's reviews are pending. Calendar uses five light-to-dark shades per density band: blue for 1–499 answers, purple for 500–999, and gold for 1,000+. Blue and purple use fixed thresholds across streaks; gold scales to the busiest day in the selected streak with no upper cap. Custom Anki cutoff preferences are not imported. Later features gain navigation when implemented; avoid empty placeholder pages.

The overview contains a distinct-entry total, total rated-answer count with learning/review/relearning/filtered breakdown, ordinary-review Again rate, review-volume chart, and today's featured insight. The review total includes every rated-answer kind, matching the meaning of Anki's overall review total; the Again-rate denominator is explicitly narrower. Each summary links to the relevant detail view. Counts carry their scope and date range. The actual source profile is visible, and isolated/synthetic test sessions must be unmistakably labeled.

Use charcoal backgrounds, slightly lighter panels, restrained accent colors, and readable contrast inspired by Anki, Visual Studio, and Codex dark interfaces. Use a Japanese-capable system font and scalable layout. Support keyboard navigation and text explanations alongside chart colors. Exact colors and visual polish can be reviewed during implementation.

## 4. Metric definitions

### Vocabulary inventory

Anki cards, notes, vocabulary forms, and underlying words are different entities. Keep their totals separate.

- An eligible vocabulary note has at least one included card and a mapped, nonempty vocabulary expression.
- Multiple cards generated from the same note do not multiply the vocabulary count.
- Multiple notes with the same normalized expression and reading produce one distinct vocabulary entry, with links to all supporting notes/cards.
- In Phase 1A, normalization removes presentation markup and surrounding whitespace and performs conservative Unicode normalization. It must not silently merge kana/kanji spellings, inflections, or different readings.
- Furigana and other field conventions require mapping-aware handling; never strip meaningful text using a blanket rule. Preview the extracted expression. Unparseable or empty expressions are counted as skipped and remain inspectable.
- The Phase 1A label is **Vocabulary forms in selected decks**. If "known" appears, explain that it currently means present in scope, including unstudied cards.

The collection drills dictionary forms, not separate cards for ordinary conjugations such as 食べた and 食べます. The relevant example is 報う, 報いる, and 報われる: these are three separate vocabulary entries for inventory and reference matching, while the later stem analysis should group them into one shared stem family. This grouping is broader than strict dictionary lemmatization; Phase 1B must define and expose its family rules and ambiguous cases. Do not collapse these entries in Phase 1A or common-word coverage merely because they share a stem.

Common-word coverage counts each item in a defined reference set at most once. Several forms matching the same item cannot increase the numerator multiple times. The reference set's entry/form identity must be resolved in Phase 1B before fixing the denominator. Raw vocabulary-form totals are not a coverage numerator by themselves.

### Review scope and interpretation

Learning Patterns is a follow-up local analysis based on the user's deck workflow. Difficulty history is three or more Again answers during ordinary reviews across all available history; Hard, learning/relearning repetitions, filtered answers, administrative/invalid records, and future events do not create lapse episodes. Current card lapse counters are retained separately from reconstructed history. Recent evidence uses six calendar months and 4 AM study dates. Active-profile card lists and word groups distinguish history-backed difficulty from recovery/suspended placement, including new recovery cards with no review evidence. 日本語::永遠 overrides historical difficulty as known context; 日本語::Seen in Immersion and 漢字::Unused are excluded. 日本語::Suspended and 漢字::Relearn are recovery cohorts. Grammar is out of scope.

Cross-deck comparisons explicitly use the imported Japanese and Kanji trees, regardless of active-profile card-list scope. A standalone meaning card is compared with vocabulary containing that kanji and with known vocabulary as supporting context. Below-threshold meaning cards require at least ten ordinary answers before receiving a vocabulary-only comparison; missing history is not mastery. Pattern candidates use explicit whole-word readings, reliably aligned single-kanji furigana, one-character written-form differences, and exact normalized English gloss overlap. Broad ambiguous English glosses and generic kana-only endings are suppressed. Groups require at least two distinct history-flagged words, with up to 150 ranked groups per type from the imported scope. Multiple cards for the same word do not inflate distinct-word counts. Character readings are unresolved when compound furigana cannot be safely divided; no dictionary is inferred. These findings never establish which component caused an answer failure. Supporting cards expose IDs, reasons, counters, and recent ordinary answers. Cleaned source expression text is retained alongside the inventory expression so optional bracket stripping does not erase analysis evidence. The importer remains read-only.

Time Analysis is a follow-up view scoped to the active deck profile. Overview displays total recorded hours across all available rated-answer history. Time Analysis offers all history, last 30 days, last 90 days, and last six calendar months (current study date minus six months, inclusive, through today). It sums positive recorded durations across all rated answer kinds, excluding administrative, unknown, invalid-rating, and future events. Totals accumulate in 64-bit milliseconds. Mean and median seconds use individual positive-duration answers; missing durations retain their activity counts but are excluded from timing denominators. The page compares all-history and six-month totals, calendar-day and seven-day averages, and mean/median seconds per answer. Daily and Monday–Sunday weekly time charts include average reference lines and exact totals. Weekdays use the 4 AM study date; hourly buckets use local wall-clock hours, combining repeated DST hours. Calendar-day averages include zero-review dates from the later of the selected start or the first available answer through today. Seven-day averages equal daily averages × 7; edge weeks and today may be partial. If no positive durations exist, timing metrics show No data. These metrics represent Anki's recorded answer durations and may be capped, rather than uncapped elapsed study time. Existing review volume views retain their midnight-based definitions.

Join review events to included, currently existing cards. Deleted cards may have historical events without recoverable vocabulary/deck metadata; exclude unresolvable events from scoped metrics and disclose that limitation. Deck attribution reflects current membership, not a reconstruction of historical deck membership.

Decode event kinds according to the supported Anki schema. Learning, ordinary review, relearning, filtered/cram, and administrative scheduling events must not be treated as interchangeable. Unknown event kinds are reported, not silently interpreted. Do not infer past ratings from current card state.

| View | Phase 1A definition |
| --- | --- |
| Volume over time | Count recorded answer events per local calendar day, with daily/weekly display; exclude administrative events |
| Again rate by time of day | Again answers divided by eligible rated answer events in each bucket; ordinary review is the default, with learning/relearning distinguished |
| Answer time by time of day | Median recorded positive answer duration for the same eligible event scope; show sample size and do not describe it as uncapped actual study time |
| Most-reviewed cards | Rank included cards by ordinary review-event count within the selected date range |
| Most-lapsed cards | Rank included cards by Again answers during ordinary reviews within the selected date range; label this separately from Anki's current lifetime lapse counter |

Provide All history, Last 30 days, and Last 90 days presets. Apply filters consistently across charts and tables. Empty denominators display "No data," never 0%.

Default time buckets: overnight 00:00–05:59, morning 06:00–11:59, afternoon 12:00–17:59, evening 18:00–23:59. Use the configured local time zone with daylight-saving handling. Explain that past travel time zones cannot be recovered from timestamps alone. Bucket customization can follow later.

Show the number of eligible reviews next to accuracy and timing results. Mark buckets with fewer than 30 eligible events as limited data; this is a presentation threshold, not a statistical guarantee. Differences across buckets are descriptive and do not establish that time of day caused better performance. Recorded durations may reflect Anki limits and interruptions.

## 5. Daily insight behavior

The long-term target is approximately 80–150 distinct insight templates. Creating that library is not an initial-build requirement. Proposed Phase 1A target: 8–12 templates backed by already implemented metrics, subject to meaningful data availability.

Initial candidates include the most-reviewed card, card with the most review Again answers, busiest review day, longest active-day streak, day of week with most reviews, most-used review time bucket, proportion of reviews completed in the evening, and the best-supported low-Again-rate time bucket. Avoid presenting several cosmetic variants of one metric as different insights. Every card must explain its calculation, date range, selected deck scope, time zone, and eligible answer kinds. Consistency observations show active dates divided by all calendar dates between the first and last recorded answer, with a percentage; vague labels such as "momentum" are insufficient. Midnight-based calendar dates are explicitly distinguished from Anki study days.

Select one template for the local calendar date and persist that selection. Reopening the app on the same day retains the template while refresh updates its values. Persist the previous seven dates' selections and exclude those template IDs from selection. This history is separate from deferred knowledge snapshots.

Skip templates without enough data. If no eligible unseen template remains, show an explanation or a neutral summary rather than violating the no-repeat rule or inventing a result. Keep insight history across restarts and refreshes. History is specific to the collection and saved deck-profile identifier; renaming a profile or changing its rules recomputes values without resetting repetition protection.

Rarest-word insights require a documented frequency source and are deferred until dictionary/frequency support exists. Common status alone is not an adequate ranking of rarity. A leech tag, a lapse count, and repeated Again answers are distinct concepts and must be labeled accurately.

## 6. Local collection access and data safety

Analysis requires neither AnkiConnect nor an Anki add-on. Refresh uses Anki closed;
the app analyzes its own private imported data. Optional explicit note creation
requires AnkiConnect and Anki open on the matching profile.

The importer must never modify, migrate, repair, checkpoint, or tag the source
collection, or launch Anki's collection API against it. Source SQLite is never
written directly. Only the approved Create action asks an already-running Anki
instance to add new notes through AnkiConnect. Do not import media, execute note
HTML/JavaScript, or follow remote content from card fields.

Proposed refresh sequence:

1. Validate the remembered profile and collection file.
2. Establish that a safe closed-file acquisition is possible. Use a read-only file handle that denies concurrent writers while acquiring a private copy. Treat active journals or a nonempty WAL as a reason to stop and request a clean Anki close; do not discard them.
3. Copy through the protected read handle into an app-owned temporary location outside the repository. Do not copy a live collection opportunistically or bypass locks with SQLite immutable mode.
4. Analyze only the private copy. Validate schema compatibility before reading metrics. Handle Anki-specific collations and any required serialized metadata through a dedicated compatibility adapter.
5. Build a complete new result set, validate it, and atomically replace the latest successful app-owned cache.
6. Dispose of temporary copies and release resources on success or failure.

The exact file-locking/acquisition strategy must be validated on Windows before accepting the importer. If safe acquisition cannot be established, fail clearly and retain the previous cache. Reading the source must not create SQLite sidecar files in the Anki profile.

Supported failures include missing/moved profile, access denied, locked collection, unsupported schema, corrupt/incomplete copy, insufficient disk space, and invalid mappings. Each produces an actionable message rather than a crash. On first launch with no cache, remain in setup/recovery. Later failures show the last successful timestamp and stale-data status.

## 7. Recommended technical design

Use **C# with a supported .NET LTS release, WPF, and SQLite**. Confirm exact supported dependency versions at implementation time.

WPF fits the Windows-only requirement and supports native windows, data binding, styling, and Japanese text without requiring a browser/server launch. Package a self-contained Windows build so the user does not install a development runtime. A launchable app folder with a shortcut is sufficient initially; a full installer can follow if useful.

Keep four modest boundaries within one desktop application:

- **Presentation:** WPF views/view models, dark styles, tables, charts, progress, and recovery messages.
- **Collection import:** safe file acquisition, schema adapter, field/deck mapping, and normalized records.
- **Analysis:** vocabulary inventory, review aggregates, insight templates, and later dictionary matching.
- **Local persistence:** settings, latest successful imported/derived data, and insight rotation history in app-owned storage.

Use Microsoft.Data.Sqlite for app-owned SQLite data and evaluate it against the imported schema's collation requirements. Chart-library selection is an implementation decision based on WPF compatibility, maintained status, and license. No web backend, cloud account, plugin architecture, or cross-platform layer is required.

Local records should distinguish collection identity, deck hierarchy, note types/field mappings, notes, cards, review events, extracted vocabulary forms, analysis run metadata, and insight history. Future dictionary matches must reference stable dictionary IDs and retain matching provenance. Do not design speculative media or fluency subsystems now.

Do background work off the UI thread, use bounded/batched database operations, and virtualize long tables. Optimize for collections on the order of tens of thousands of cards and hundreds of thousands to roughly a million review events. As a provisional usability target, show the window within two seconds and complete a full refresh within 30 seconds on a typical local SSD; measure before making a release promise. The interface must remain responsive throughout.

## 8. Storage, exports, and repository hygiene

Store all user-specific files under a per-user application-data location. Do not store collection files, private note text, personal paths, machine details, or derived personal statistics in the repository. Do not create documentation containing inspection output from a real collection. Any examples or test fixtures must be synthetic.

The app retains the latest successful results, not historical knowledge snapshots. Review-history charts use events already present in Anki; they do not require snapshots. Replacing a cache must also remove data that is no longer included. Old results must never be presented as belonging to a newly selected collection.

CSV exports use the current filters, UTF-8 with Japanese text preserved, appropriate quoting, and spreadsheet-safe handling of formula-like text. Include useful metric/filter metadata without exporting personal source paths by default. Save only to a user-selected location.

Phase 1A needs no network service or telemetry. Future dictionary downloads require explicit source attribution, version/date metadata, license files, and a documented update procedure. No card contents or collection statistics should be sent to dictionary providers.

## 9. Dictionary and future analysis constraints

Jisho's vocabulary categorization is the desired comparison target. JLPT labels mean coverage of the selected Jisho-aligned lists, not official exam conformity or a certification estimate. Exact Jisho parity is not yet verified and must not be promised.

JMdict is a candidate for downloadable local vocabulary data; KANJIDIC2 is a candidate for kanji information. A local SQLite index can be built from permitted source distributions rather than querying a website for every card. Neither a public SQL dump nor permission to bulk scrape Jisho should be assumed.

Before Phase 1B, settle reference identity, common-word rules, variant handling, readings/homographs, confidence levels, unmatched handling, source licensing, and update behavior. Keep app-derived classifications separate from Anki tags. Ambiguous matches must remain visible and must not silently inflate coverage.

Grammar analysis will count distinct patterns such as passive, causative, and conditional rather than every verb instance. Explicit field/category mapping is acceptable. Conjugation exercises alone do not establish exhaustive knowledge of grammar.

Kanji from known vocabulary and explicitly studied kanji are separate datasets. Recognition, reading, and writing claims require supporting card structure. A vocabulary-derived kanji count is not proof of handwriting ability.

Media analysis should prioritize unique-word coverage and show both count and percentage. Manual vocabulary/subtitle import is an acceptable fallback but not the preferred experience. External availability, licenses, and integration options must be researched when that work starts.

## 10. Implementation milestones and acceptance

| Milestone | Reviewable outcome |
| --- | --- |
| A1: Desktop and safe import | Launchable dark desktop shell; profile/deck/mapping setup; safe private acquisition; remembered settings; progress and recovery |
| A2: Vocabulary inventory | Distinct-form total and inspectable table with duplicates, skipped expressions, and category exclusions handled |
| A3: Reviews and insights | Four agreed review views, daily featured insight, persisted seven-day repetition protection |
| A4: Finish Phase 1A | CSV export, usable shortcut/app packaging, privacy check, and representative validation |

Phase 1A is useful and complete when:

- The app opens from a normal Windows shortcut without a CLI and refreshes automatically from saved settings when the source is safely available.
- It reliably presents at least three useful requested data points: vocabulary-form inventory, review-history analysis, and a rotating interesting observation. It also implements all four agreed review views.
- Deck and mapping choices persist, suspended/new cards count by default, and unrelated material does not inflate vocabulary totals.
- Metrics can be inspected through tables; synthetic cases verify duplicate handling, event selection, zero denominators, time boundaries, and CSV output.
- Failed refreshes do not crash the app, damage the source, or replace valid cached results. Successful refreshes update all visible results consistently.
- Source safety is verified, including no source-content changes or source sidecar creation by the app. Lock/conflict and unsupported-schema cases are exercised.
- The daily template survives restarts, refreshes its values, and does not repeat within seven calendar days; low-data behavior is explicit.
- A public-repository check finds no real collection files, private paths, settings, exported records, or real-data fixtures.

Use focused validation for metric correctness and importer safety, plus manual end-to-end checks with local data. Hosted CI, a release automation pipeline, public-distribution infrastructure, and a contributor guide are not required for this personal project. Real-data validation output stays local and outside the repository.

## 11. Decisions and unresolved implementation checks

Phase 1A is complete. Phase 1B follows the decisions in section 2; subsequent capabilities remain outside that implementation scope.

Remaining technical checks do not require more product discovery before implementation: validate safe Windows acquisition; establish supported Anki schemas and custom-collation handling; confirm field mappings through the setup preview; select compatible chart and packaging dependencies; measure refresh performance. These checks may change implementation details, but not metric meanings or read-only guarantees.

The initial insight count and performance targets above are implementation targets. The user has approved Phase 1A and the Phase 1B decisions above. Later phases require a separate instruction to build.

## 12. Primary references

- [Anki: managing files](https://docs.ankiweb.net/files.html) — collection location/contents and the warning against copying an open collection.
- [Microsoft: WPF overview](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/overview/) — Windows desktop framework capabilities.
- [JMdict project](https://www.edrdg.org/jmdict/j_jmdict.html) — downloadable dictionary project and source documentation.
- [EDRDG dictionary license](https://www.edrdg.org/edrdg/licence.html) — attribution, share-alike terms for dictionary data, and update requirements. Recheck terms when distributing data.
- [Official JLPT FAQ](https://jlpt.jp/sp/e/faq/index.html) — context for why current JLPT labels are not an official published vocabulary syllabus. The product target remains the chosen Jisho-aligned lists.
