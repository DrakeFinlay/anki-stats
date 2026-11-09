# Nakama — Future Plans

This is the backlog after the completed Phase 1A/1B work. These are unimplemented
ideas, with no delivery dates or selected integration providers. [DESIGN.md](DESIGN.md)
describes the current app; [README.md](../README.md) explains setup and use.

## Incremental imports and startup performance

Replace full live imports on every refresh with a persistent baseline and
incremental reads where the source API provides reliable change evidence.
Keep automatic startup refresh: the user normally wants fresh data immediately
after opening, so replacing it with manual refresh is not the requested solution.
Avoid presenting a cached timestamp as a fresh Anki sync.

Handle added, edited, and deleted notes/cards; deck moves and filtered home-deck
membership; changed field schemas/mappings; and added, edited, deleted, or undone
reviews. Card/note modification times alone are insufficient for review-history
changes and deletions, and their second resolution needs consideration. Detect
profile/reference/scope changes, invalidate affected derived results, and fall
back to a full import when continuity cannot be verified. Retain the last good
cache if an incremental update fails, and keep note creation's live duplicate
checks independent of cached statistics.

Benchmark cache loading, reference indexing, matching, charts, HTTP enumeration,
and review-history validation separately before selecting an implementation.
Measure unchanged startup and one-note/one-review updates against a full import,
including deletion and undo regression cases. Consider caching derived analysis
with reference/rule/settings version keys as well as raw imported records. Do not
claim a speed improvement without measurements on a representative collection.

### Observed near-complete startup counter

The reported `Reading live cards and review history… 16,000 / 16,168` message
is emitted before each 200-card batch in `LiveCollectionImporter.Import`.
After the last 168-card batch, the same message remains while the importer reads
note modification times and note fields, repeats deck/profile inventory, checks
card modification times, rereads review history to reject concurrent changes,
and projects the snapshot through the shared SQLite importer. These stages do
not currently have separate progress messages. A nearly complete counter can
therefore look frozen while work continues; the screenshot alone cannot identify
a slow request or establish a deadlock.

Media adds another cost: after the normal profile import and analysis,
`RefreshInternal` calls `CompareMedia`, which discovers metadata again and performs
another full all-deck import, including review history, although media presence
only needs vocabulary notes/cards. The normal header can still show the preceding
counter during this pass. The HTTP client permits up to 90 seconds per request;
this is not a measured explanation of the user's individual delay.

For the later performance pass, add phase timings/progress for the final card
batch, notes, consistency validation, projection, dictionary matching, rendering
and the second media read. Reuse compatible imported snapshots and cache derived
matched-ID sets with collection/mapping/reference/rule versions. Evaluate an
all-deck vocabulary-only snapshot and safe incremental validation for media so
it does not require repeated review-history reads. Keep current source-safety
checks until an equally reliable replacement is validated. No startup caching
optimization or benchmark claim is included in the cover-layout changes.

## 1. Remaining mining opportunities in current media

Initial whole-title media support is implemented; see [Media](media.md) for
online/local acquisition, agreed mining criteria, metrics and current limits.
The proposals below retain longer-term context; unread-position tracking and
broader media candidate sources remain unimplemented. The initial corpus is F/SN;
Common ∪ JLPT is eligible, including particles/auxiliaries, and presence in any
mapped Anki deck counts as mined. Names are not categorically excluded.

Show how many vocabulary candidates still meet the user's mining criteria in a
selected title, initially **F/SN**, the current reading project. Provide an
inspectable list behind the count and recompute it against updated Anki data.

Treat “remaining cards” as unique eligible vocabulary candidates; separately
explain how many Anki cards the resulting notes might generate. Repeated word
occurrences and sibling cards must not inflate the candidate count.

Further configurable criteria could include title-specific frequency and optional
name exclusions. The implemented default is Common ∪ JLPT without categorical
POS/name exclusion, and Suspended counts as present. Ambiguous source analyses
and possible unresolved Anki matches remain separate from remaining candidates.

Distinguish whole-title opportunities from words in the unread portion. The
latter needs reading progress and ordered script segments or another reliable
position mapping. Without that information, report whole-title coverage rather
than assuming what remains unread. Manual versus integrated progress tracking
remains an open decision for unread-portion analysis.

### Media Import: implemented scope and follow-ups

The Import tab now aggregates missing resolved entries from Completed corpora,
with Common/JLPT and outside-both pools. Rank by number of completed titles, then
total occurrences, with an adjustable minimum title count. Prepare selected
rows, the first N, or all filtered rows through the existing Anki preview/writer.
Genuine locally analyzed source sentences are optional; missing ones stay blank
and are disclosed in preview. See [Media](media.md) for current behavior.
An individual title's Word list also supports selected missing entries from any
shelf, including both Common/JLPT and other words, without a title-count minimum.

Future additions include aggregating a chosen subset of source titles (including
current or planned works), alternative ordering controls, reviewing multiple sentence
occurrences, acquiring additional permitted scripts, and configurable target
decks/note types. Preserve unique-entry counting, inspectable source provenance,
live duplicate checks and interrupted-write recovery. Ordinary refresh and media
acquisition must continue to avoid silently creating notes.

## 2. Discover missing vocabulary in existing Anki sentences

Analyze sentences already stored in Anki to find words without dedicated
vocabulary notes. Cross-reference candidates with JMdict common words and
corrected OpenJLPT memberships, then bring eligible words into the new-note flow.

Proposed pipeline:

1. Map sentence fields by note type and extend the importer/cache to retain them.
   Current mappings retain intended vocabulary, reading, and meaning, not an
   analyzable sentence corpus.
2. Clean presentation markup/furigana without executing field content. Retain
   source note IDs and sentence contexts for inspection.
3. Perform morphological analysis: segment Japanese text, recover dictionary
   forms, and retain surface forms, readings, parts of speech, and positions.
4. Resolve results to dictionary identities, treating compounds, names,
   inflections, and ambiguous kana conservatively.
5. Compare with dedicated vocabulary notes, separating Known, Familiar,
   database-only, missing, and uncertain candidates. Deduplicate by dictionary ID.
6. Attach Common/N5–N1 memberships and show supporting sentence occurrences.
7. Automatically prepare eligible new-word batches. Reuse preview, explicit
   Create, preferred forms, live duplicate checks, and interrupted-write recovery.
   A normal stats refresh should not silently add notes.

Automatic discovery and batch preparation are the intended direction. Whether
to copy a supporting sentence into the generated note, which occurrence to pick,
and how to order candidates remain open. Current reference-list creation leaves
Sentence blank; sentence-driven creation would extend that behavior.

Incidental sentence vocabulary must not count as having a dedicated card or
knowing a word. Keep sentence results separate from current Word Coverage.
This needs a morphological analyzer and its analysis dictionary in addition to
JMdict; the dictionary-form matcher alone is insufficient. Evaluate compatible
local options when implementation starts. Having AI develop the feature does
not by itself imply a cloud AI dependency or uploading private sentences.

## 3. Sentence statistics

Add an overview of sentence material in the selected Anki scope:

- Notes with a nonempty sentence field, empty fields, extracted sentence count,
  and distinct normalized sentences. A field may contain several sentences;
  duplicate notes and sibling cards must not multiply distinct-text totals.
- Total analyzed word occurrences (tokens) and unique vocabulary entries (types).
  Define handling of punctuation, particles, auxiliary verbs, numbers, names,
  and unknown tokens so “total words” has a reproducible meaning.
- Sentence-length distributions, average/median length, repeated sentences,
  analysis failures, and unresolved vocabulary.
- Known/Familiar/database coverage, missing words, and Common/JLPT breakdowns.
  Separate unique-entry coverage from occurrence-weighted text coverage and
  display both denominators.

Support inspection by note type, deck profile, and eventually title where reliable
media provenance exists. Define sentence splitting and normalization first;
record analyzer/dictionary versions so reanalysis can explain changes. Text
coverage must not be presented as a comprehension score.

## 4. A library of consumed media

The initial Currently reading / Completed / Add shelves and vocabulary summaries
are implemented in [Media](media.md). Historical baselines and richer coverage
breakdowns below remain future work.

Track previously read or consumed works, including **Biman 4** and **Cafe Sourire**,
with game/anime icons or cover images and vocabulary coverage summaries. Allow
planned, in-progress, and completed status, with associated scripts, subtitles,
vocabulary lists, or existing coverage records.

Show covered/total unique vocabulary and percentages for Known, Familiar,
Combined, and In database. Offer separately labeled token-weighted coverage when
occurrence counts exist. An imported percentage without its word list and
denominator is a historical reported value, not an independently recomputed metric.

Recompute against current Anki data when the word list is available. Distinguish
coverage now from coverage when the work was consumed; the latter requires a
saved baseline or historical snapshot. Show unresolved counts and source versions.

Support user-supplied artwork and a text fallback. Investigate artwork/metadata
providers, acquisition, formats, and permissions when integrating each source.
Identifying a work should not depend on its icon being available.

## 5. Analyze prospective media

The first supported integration is Jiten vocabulary acquisition, with local
text/Sudachi analysis as a fallback. Initial media types are VNs, anime and books.
See [Media](media.md) for implemented behavior and limitations; broader sources,
media types and source-sentence integration below remain future proposals.

Let the user select future games, anime, novels, manga, or other works. Obtain
available scripts/subtitles/text through supported integrations, perform
morphological analysis, build unique vocabulary lists, and compare them against
the app whenever analysis runs with refreshed Anki data.

Integrated acquisition and analysis is the preferred experience. User-selected
local scripts, subtitles, or vocabulary lists are a fallback. No provider, script
collection, extraction tool, or format has been selected; establish availability
and permitted use per integration when work begins.

Cache versioned text-analysis results locally to avoid downloading and
retokenizing everything on each comparison. Record title/edition, routes or
episodes included, source revision, analyzer configuration, and dictionary
versions. Partial scripts must not appear to represent complete works.

Show unique-entry coverage, remaining eligible mining candidates, Common/JLPT
breakdowns, and occurrence-weighted coverage where available. Compare titles to
help choose future material, with missing lists and sample contexts. Reuse the
sentence-analysis cohort definitions and ambiguity rules.

## 6. Other unimplemented product directions

These carry forward the earlier design's longer-term ideas.

| Area | Remaining work and boundaries |
| --- | --- |
| Reference alignment | Further Jisho comparisons when usable permitted data exists; retain the explicit choice of corrected OpenJLPT classifications rather than silently reverting to Jisho tags |
| Stem families | Expand limited curated/manual relationships with explainable evidence; shared kanji or glosses alone are insufficient |
| Grammar | Count distinct explicitly mapped patterns such as passive, causative, and conditional; conjugation practice alone does not establish exhaustive knowledge |
| Kanji | Separate vocabulary-derived kanji from dedicated study; distinguish supported recognition, reading, and writing claims; evaluate data such as KANJIDIC2 when needed |
| Knowledge filters | Add young/mature or history-based filters alongside named-deck cohorts without silently redefining existing statistics |
| Goals and forecasts | User-defined vocabulary/other metric targets and forecasts with visible assumptions; historical progress may need snapshots |
| Review analysis | Broader review views, configurable time buckets and study-day preferences where supported, and interpretation grounded in recorded data |
| Insight library | Grow toward the original approximately 80–150 distinct useful templates; avoid cosmetic duplicates and retain scope/sample explanations and repeat protection |
| Rarity insights | Establish a documented frequency source before ranking rare words; JMdict common status is not a rarity ranking |
| Knowledge snapshots | Optional aggregate baselines with reference/scope changes explained for meaningful historical comparisons |
| Broader knowledge profile | Explore a wider profile or fluency-related estimate only with defined evidence and limits; list/text coverage alone is insufficient |
| Language and packaging | Consider bilingual interface labels and a fuller installer/release process; current deployment is a Windows app folder and shortcut |

A standalone local dictionary app remains a separate future project. Bulk
classification/tag updates on existing Anki notes remain outside the implemented
new-note workflow and need their own explicit scope.

## 7. Suggested sequence and decisions to settle

Start with sentence-field import and reusable local text analysis, then sentence
statistics and missing-word inspection. Add candidate batch preparation using
existing creation safeguards. Reuse that foundation for a first F/SN corpus and
remaining-mining count, followed by consumed/future media libraries and
integrations. This is a dependency-based proposal, not an agreed release plan.

Before building, settle mining criteria, sentence fields/splitting, the first
text source/format, analyzer and dictionary, ambiguity handling, reading-progress
input, and source-sentence copying into new notes. Define unique-entry and token
denominators before publishing percentages. Private scripts, sentences, paths,
artwork, and derived personal statistics stay outside the public repository.
