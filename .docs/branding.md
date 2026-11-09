# Nakama branding

Nakama takes its name from **仲間（なかま）**, companion. The app's tagline is
“Your Japanese learning companion.” It was previously called Anki Stats.

The identity follows Gengo's kanji tile approach: bold cream lettering, a rounded
square, and a solid color. Nakama uses the app's existing blue, `#3D659B`, with
cream `#FFF8E8`. The full wordmark places **仲間** above **Nakama**. The Windows
icon uses **仲** so its strokes stay legible at small sizes. The full wordmark is
intended for dark backgrounds, including the sidebar's `#20232B`.

## Assets

All assets live in [`src/AnkiStats.App/Assets`](../src/AnkiStats.App/Assets/):

| File | Purpose |
| --- | --- |
| `Nakama.svg` | Outlined, scalable kanji tile |
| `Nakama.png` | Transparent 512 × 512 tile preview |
| `Nakama.ico` | Windows icon with 16, 20, 24, 32, 40, 48, 64, 128, and 256 pixel frames |
| `Nakama-wordmark.svg` | Outlined, scalable 仲間 / Nakama wordmark |
| `Nakama-wordmark.png` | Transparent 640 × 452 wordmark used in the sidebar |

The vector source uses outlines from Yu Gothic UI Bold and Segoe UI Bold, rather
than font references. The committed images do not require those fonts at runtime.
Regenerate on Windows with the fonts installed:

```powershell
powershell -NoProfile -STA -ExecutionPolicy Bypass -File scripts/create-brand-assets.ps1
```

These assets are generated locally with WPF typography and drawing APIs.

## Names and compatibility

Window titles, dialogs, executable metadata, the executable (`Nakama.exe`),
packaged output (`artifacts/Nakama`), and shortcuts (`Nakama.lnk`) use the new name.
Run `scripts/create-shortcut.ps1` to recreate the development shortcut; publishing
also recreates it. Older ignored `Anki Stats.lnk` shortcuts and published folders
may remain on an existing checkout. The new canonical launcher is `Nakama.lnk`.

The repository folder and GitHub remote, project paths, C# namespaces, and core
assembly still use `AnkiStats`. Saved app data stays under
`%LOCALAPPDATA%\AnkiStats`, and `ANKISTATS_DATA_DIR` continues to select an isolated
test session. Existing settings, imports, mapping decisions, and creation journals
therefore remain available without migration. Legacy Anki attribution and tag
identifiers remain readable for note recovery and duplicate prevention.
