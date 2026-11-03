# Dictionary matching and Word Coverage

Word Coverage matches intended vocabulary fields to dictionary identities. It is
not a standalone dictionary browser or sentence tokenizer. See [the user's guide](../USER-GUIDE.md#use-word-coverage)
for controls and [DESIGN.md](../DESIGN.md) for metric definitions.

## Source acquisition and storage

[CoverageReferenceStore.cs](../src/AnkiStats.Core/CoverageReferenceStore.cs) pins
jmdict-simplified English release 3.6.2+20261026175559 and OpenJLPT revision
0d1d3410bec90bd4098a7c72de820543cb4f707c. Download retrieves fixed public URLs and
verifies SHA-256 for the dictionary archive and five level lists. Requests contain
no note text, personal paths, or collection statistics.

Build retains JMdict IDs, spellings, readings with spelling restrictions,
distinct English glosses condensed into a string, common flags, linked JLPT
memberships, and preferred study forms. It is not a full sense-by-sense semantic
model. Common status follows the selected news1/ichi1/spec1/spec2/gai1 priority
rule. OpenJLPT links by JMdict ID; missing/unavailable links are counted and
excluded rather than guessed. Corrected classifications intentionally take
precedence over exact live Jisho tags.

Save validates IDs, readings, and levels, writes compressed JSON to a temporary
file, then replaces reference/coverage-reference-v1.json.gz under app data. A
failed download preserves the prior bundle and personal corrections. Reinstall
reuses pinned sources. New snapshots require updating pins/hashes and reviewing
classification/matching changes. Metadata retains source versions, dictionary
date, excluded source-row counts, attribution, rules, and preferred-form version.
See [third-party notices](../THIRD-PARTY-NOTICES.md) for derived-data license terms.

## Parsing and Japanese candidate selection

[VocabularyExpression.cs](../src/AnkiStats.Core/VocabularyExpression.cs) parses
mapped expression/reading fields and preserved source expression text. It supports
word[reading], segment furigana, whitespace before annotations, numeric headwords,
and alternative readings such as 昨夜[ゆうべ・さくや]. It does not tokenize Sentence.

[VocabularyMatcher](../src/AnkiStats.Core/VocabularyCoverage.cs) builds an in-memory
reference-form index. Lookup preserves written script before kana-folded fallback,
so a katakana loanword does not automatically become a hiragana homophone.
Dictionary restrictions determine whether a reading applies to a written form.
Explicit alternatives are resolved separately: several readings may support one
entry once or different entries independently. Phonetic segments join only when
none match separately and a unique exact-form dictionary reading verifies the join.

## English evidence and uncertainty

[DefinitionMatcher.cs](../src/AnkiStats.Core/DefinitionMatcher.cs) compares normalized
English phrases: exact phrases outrank limited containment of short phrases,
negation is retained, and generic/tied evidence is suppressed. This is local
matching logic, with no model/API or guessed synonyms. Meaning cannot select a
Japanese-incompatible entry. はい with “yes” can identify the yes entry without
crediting 灰 or 肺.

A unique Japanese match with unverified English wording retains coverage with a
Definition warning. An exact kanji spelling can retain coverage with a Reading
warning when one uniquely supported reading differs by one edit and readings
have at least four characters. Edits include insertion, deletion, substitution,
and adjacent transposition. This is not fuzzy headword matching; ties and larger
discrepancies remain unresolved.

| Result | Effect |
| --- | --- |
| Matched / Corrected / Generated | Confirmed dictionary IDs contribute to their supported cohorts |
| Reading / Definition warning | Confirmed portions can contribute while remaining visible for review |
| Partial | Only confirmed alternatives contribute |
| Ambiguous / Unmatched / Stale decision | Unresolved portions receive no invented coverage |
| Excluded | A personal decision suppresses coverage for the note |

Any accepted form and supported sense is enough. ありがとう and 有難う are
alternatives, not required separate cards. All glosses need not match. Reports
lead with a kana lookup and label other forms as optional alternatives.

## Cohorts, counting, and reports

Analyze groups included cards by note. 日本語::永遠 establishes Known and
日本語::勉強してる establishes Familiar, including descendants. Combined is their
union. In database includes all matched vocabulary in the active selection;
Suspended/other selected decks are database-only. Queue state does not redefine
these cohorts. Excluded decks remain outside statistical coverage.

Each metric uses sets of confirmed IDs: intersect a cohort with the reference
list for the numerator and count unique linked entries for the denominator.
Cumulative levels use unions. Sibling cards, duplicate notes, alternative forms,
and list overlap never multiply an entry. Inventory expression/reading totals
and raw card/note counts remain different units.

CoverageReport creates requested Markdown files for entries outside Combined
or outside Database. Possible-match IDs are separated from entries without
support. Missing confirmation is not proof of absence. Reports retain scope,
versions, alternatives, glosses, IDs, and source attribution. These source-defined
study-list percentages are neither an official JLPT syllabus nor text comprehension.

## Decisions and families

MatchDecision objects live by collection/note in app-owned SQLite. They can link
one or several verified IDs or exclude a note. Current fingerprints include
parsed expression, readings, and normalized definition. Target changes or removed
IDs make a decision stale. Older fingerprints remain readable. Corrections never
edit Anki fields or tags. Generated notes use the same fingerprint validation.

FamilyDecision records are separate. Confirmed Combined entries form conservative
families: 報う／報いる／報われる is the initial curated group; other entries stay
separate unless manually grouped. Shared kanji/glosses do not justify inferred
relationships. Families never alter Common/JLPT entry coverage.

## Preferred study forms and verification

Build uses the first sense's usually-kana marker, common kana/written flags, and
reading restrictions to choose StudyForm. Usually-kana entries use preferred
kana; other entries prefer a common written form with compatible reading, with a
kana fallback when needed. This yields ありがとう and 食べる[たべる], not a
corpus-based guarantee of the most frequent form. FormSelectionVersion identifies
old bundles: coverage can read them, but creation requires preferred-form metadata.
The [AnkiConnect guide](anki-connect.md) explains how creation reuses this engine.

CoverageChecks, CoverageMatchingChecks, and DefinitionChecks exercise identity,
restrictions, alternatives, warnings, homophones, definitions, decisions, families,
and report semantics using synthetic entries. Creation tests separately cover
preferred forms and duplicate exclusions. Real-data audits remain private.
There is no runtime AI, morphology runtime, incidental sentence credit, exhaustive
sense knowledge, or automatic broad stem inference; those extensions belong to
[future work](../FUTURE-PLANS.md).
