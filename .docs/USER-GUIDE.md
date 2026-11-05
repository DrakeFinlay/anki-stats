# Anki Stats — User's Guide

## Welcome

Anki Stats helps you explore your Japanese vocabulary and study history. It shows
what is in your collection, how it overlaps common-word and JLPT lists, and how
you have been reviewing. You can also preview and create notes for missing words.
The app is for Windows and stores its analysis locally.

It measures recorded activity and vocabulary coverage, not fluency. Sentence
analysis is in [future plans](FUTURE-PLANS.md). The initial [Media library](media.md)
supports online vocabulary lists and optional local text analysis.

## Install and open the app

If you have a packaged build, put the complete app folder somewhere convenient
and open `AnkiStats.exe`. Keep its accompanying files, third-party notices, and
`LICENSES` folder together. There is no separate installer or automatic updater.
A self-contained build includes its .NET runtime; no terminal or SDK is needed
for everyday use. Close the app before replacing it with a newer packaged build.
Your saved settings live separately from the executable.

If you have the source repository rather than a packaged app, a developer can
build it on Windows with a .NET 10 SDK by running `scripts/publish.ps1` from
PowerShell at the repository root. This produces `artifacts/AnkiStats` and
shortcuts. See [build instructions](../README.md#build-and-validate). During ongoing
development, the root **Anki Stats.lnk** rebuilds the current checkout before it
opens, so it can take longer than launching a packaged executable.

You need an existing local Anki collection for analysis. Install AnkiConnect to
read it while Anki is running and to create notes. Closed-file imports and cached
statistics work without the add-on.

## Get started

1. Anki may be open or closed during setup and refresh.
2. Open Anki Stats and go to **Settings**.
3. Use **Browse…** to select your Anki profile folder containing `collection.anki2`,
   then select **Inspect profile**. On a standard Windows installation, profiles
   are under `%APPDATA%\Anki2`; choose your profile's subfolder. Custom Anki
   installations may use another location. See [Anki's file-location guide](https://docs.ankiweb.net/manual/files).
4. Include the deck trees you want to analyze. Selecting a parent includes its
   children; exclude a child tree if you do not want it counted.
5. Check each note type's category and field mappings. Use **Vocabulary** for
   word notes and map the expression, reading if separate, and English meaning.
   Kanji/Grammar notes can contribute review activity without adding vocabulary
   inventory. Confirmed Grammar dictionary links can count in Word Coverage;
   see [Mappings](mappings.md).
   **Ignored** types are excluded.
6. Check the review time zone. Use **Preview selection** and inspect the extracted
   examples. The bracket-reading option affects how inventory expressions are
   displayed; confirm that the preview matches your field format.
7. Select **Save and refresh**.

The app remembers your settings and tries to refresh at startup. The source
profile and last successful refresh time are shown near the top. A failed refresh
keeps the previous successful results. A **TEST SESSION** banner means you are
looking at isolated development data rather than the normal connected collection.

## The everyday workflow

| What you want to do | Should Anki be open? |
| --- | --- |
| Inspect your profile, save import settings, or Refresh | Either — keep AnkiConnect enabled for live access while Anki is open |
| Explore already imported stats, switch deck profiles, or export | Either — these use cached data |
| Preview/Create new notes or Check pending additions | Yes — open the same profile with AnkiConnect enabled |

Refresh reads your local collection. If you review on another device, synchronize
through Anki first, then refresh Anki Stats with Anki still open. Refresh itself does
not synchronize AnkiWeb.

Choose **Active profile** to switch between named deck selections, such as
日本語, 漢字, and 日本語 + 漢字. These are selections within one connected Anki
collection. Manage their names, enabled states, and include/exclude rules in
Settings. Switching saved selections uses cached data; changing rules requires
previewing and saving a refreshed import.

## Find your way around

| Page | What to use it for |
| --- | --- |
| Overview | Vocabulary inventory, total answers, ordinary-review Again rate, recorded hours, coverage summary, daily discovery, and review-rhythm charts |
| Vocabulary | Search/sort entries, inspect supporting note/card IDs by double-clicking, inspect skipped expressions, and export the current table |
| Word Coverage | Common/JLPT coverage, missing-word reports, match review, stem families, and creation of new vocabulary notes |
| Media | Reading/completed covers, Jiten title search, local text analysis, and remaining Common/JLPT mining opportunities; see [Media guide](media.md) |
| Mappings | Review uncertain Anki/media matches, confirm grammar links, configure label aliases, and manage manual cleanup; see [Mappings guide](mappings.md) |
| Reviews | Daily/weekly volume, time-of-day breakdowns, Again rates, response times, and ordinary-review card rankings |
| Insights | Explore the supported discoveries and their explanations |
| Calendar | Select a current/historical streak, inspect daily answer counts, and scroll across years |
| Time Analysis | Explore recorded study hours, ranges, daily/weekly charts, averages, and weekday/hour breakdowns |
| Learning Patterns | Inspect difficulty candidates, recovery groups, shared patterns, and Japanese/Kanji cross-deck evidence |
| Settings | Connect the source, edit mappings/profiles/time zone, and open the app-data folder |

Overview's total answers includes learning, review, relearning, and filtered
answers. Reviews defaults to ordinary reviews, so choose the appropriate event
filter before comparing totals. Recorded time can be capped by Anki and does not
include all elapsed time spent studying. Small samples are labeled limited data.

Calendar and Time Analysis use a fixed 4 AM study-day boundary. Reviews and daily
discoveries use midnight-based dates. Custom Anki cutoffs are not imported.
Learning Patterns findings are connections to inspect, not proof of why a card
was difficult; cross-deck comparisons use both imported Japanese/Kanji trees even
when another active selection scopes the card list.

## Use Word Coverage

Select **Download / reinstall reference data** once. This installs public
dictionary/classification data; matching and reports then work offline. **Reference
sources and rules** explains the versions and counting rules.

| Label | Meaning |
| --- | --- |
| Known | Matched vocabulary in 日本語::永遠 and its children |
| Familiar | Matched vocabulary in 日本語::勉強してる and its children |
| Combined | Known or Familiar, with overlap counted once |
| In database | Matched vocabulary anywhere in the active imported selection, including Suspended |

These labels follow deck placement. Unstudied/suspended queue states do not
override those deck rules. A reference row counts dictionary entries, not cards.
N5–N1 are independent lists; Through N3 is the union of N5, N4, and N3. JLPT labels
come from corrected OpenJLPT lists and can differ from Jisho.

**All entries** covers the entire installed English JMdict snapshot, including
entries outside Common and JLPT lists. Its **In database** column is the running
total of distinct confirmed dictionary entries in the active profile, including
Suspended. The denominator comes from that snapshot, not an estimated dictionary
size. Duplicate notes/cards and alternative spellings do not multiply entries;
unresolved possible matches do not count as confirmed. Overview repeats this
overall count. These totals reuse existing matching results, with no extra Anki
queries or matching pass. They update with ordinary refresh and profile changes.

Select a reference-list row, then choose **Export not covered (.md)** for words
outside Known/Familiar, or **Export absent from database (.md)** for words without
a confirmed match in the imported selection. Choose where to save the Markdown
file and open it in a text/Markdown editor. Possible matches appear separately
from entries with no supporting vocabulary.

If a word seems missing despite having a card, search **Inspect Anki matches**
and check its reason. Select it and use **Review selected match** to choose a
candidate, enter a verified JMdict ID, exclude it, or reset a correction. The
dialog previews entries before confirmation. Corrections stay in Anki Stats and
do not edit Anki. Changes to the note can make an old correction need review.

One valid spelling/reading is enough; you do not need every alternative. Reading
and Definition warnings can retain coverage while asking you to inspect the
evidence. Partial matches count only their confirmed portion. Stem families are
separate conservative groupings and do not change common/JLPT percentages.

## Create missing vocabulary notes

### One-time setup

Install [AnkiConnect](https://ankiweb.net/shared/info/2055492159) in Anki. Use
**Tools → Add-ons → Get Add-ons**, enter `2055492159`, and restart Anki. This follows
Anki's [add-on installation workflow](https://docs.ankiweb.net/manual/addons).
The app expects the local service at `127.0.0.1:8765`.

Creation currently requires an existing **Japanese** note type with **Front** and
**English** fields, mapped as vocabulary expression and meaning, and an existing
**日本語::勉強してる** deck. These creation choices are fixed even if your analysis
uses other note types or decks. Import the configuration with Anki open or closed.
If AnkiConnect has an API key configured, enter it in the optional panel in
**Settings**; otherwise leave that panel alone. The key is shared by live imports
and note creation. The app does not save it: after restarting, enter it again and
Refresh if live access requires it. Previous cached statistics remain available.

### Add a batch

1. Open Anki on the same profile that Anki Stats imported.
2. In **Word Coverage → Add missing words to Anki**, enter a positive whole number.
3. Select **Preview new notes** and review the proposed Front/English fields.
4. Select **Create … new notes**, or Cancel to add nothing.
5. Inspect the notes in Anki. Adjust your new-card limits manually if desired.
6. Statistics and coverage refresh automatically. If that fails, use **Refresh**
   to retry; Anki can stay open. Successful notes remain in Anki even if refresh fails.

Selection is random without repeats within N5 → N4 → N3 → N2 → N1 → Common.
For example, requesting ten with six eligible N5 words selects six N5 and four
N4 words. A smaller remainder is offered if necessary; an empty eligible pool
produces an exhaustion message. Possible matches and pending attempts are excluded.
Live checks cover mapped vocabulary throughout the running collection, including
decks outside the active statistics selection, so a preview may offer fewer words
than a missing-word report suggests.

Notes use preferred dictionary forms such as `ありがとう` or `食べる[たべる]`,
with English glosses. Sentence, Media, and other fields are blank. No tags are
added. Hidden attribution in English retains the dictionary identity. Anki's
templates control how many cards each note generates. The app does not change
daily limits, existing notes, or scheduling settings.

If creation stops with an uncertain response, keep/open Anki and use **Check
pending additions** before requesting another batch. It checks what was actually
created and does not add or delete notes. Successful additions stay in Anki even
if the rest of a batch fails. Edit/delete unwanted notes in Anki itself.

## Troubleshooting and your data

| Problem | What to check |
| --- | --- |
| Refresh cannot acquire a snapshot | Let Anki finish any busy operation, check profile access and disk space, then retry. If Anki needs recovery, let Anki recover the collection first |
| Missing or unexpected statistics | Check source, timestamp, active profile, deck rules, categories, and field mappings before comparing metrics |
| Cannot reach AnkiConnect | Keep Anki open with the add-on enabled on the correct profile; confirm its configured address/port |
| HTTP rejection, timeout, or API error | Read the specific error; confirm the add-on is compatible and Anki is responsive. Use Check pending additions if creation had started |
| Wrong profile/model/fields/deck | Fix the named configuration in Anki, then inspect, preview, save, and refresh the mappings with Anki still open |
| Missing words have possible matches | Review those notes before treating the words as absent or creating duplicates |
| App opens an older version | Use the root development shortcut for current source changes; packaged builds stay unchanged until replaced |

Settings, cached imports, dictionary data, corrections, and creation history live
under `%LOCALAPPDATA%\AnkiStats`, accessible with **Open app data folder**. Exports
go where you choose. There is no telemetry. Analysis never changes the source
collection; only your explicit Create action adds notes through AnkiConnect.
Keep ordinary Anki backups; the stats cache is not an Anki backup.

For implementation details, read the [program guide](program-guide.md).
