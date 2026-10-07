# Changelog

All notable changes to PhraseFlow are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and versions follow [Semantic Versioning](https://semver.org/). The release workflow publishes the section matching each tag as the release notes.

## [1.0.0] - 2026-10-07

First public release.

### Added

- Keyword expansion in any Windows application through a global keyboard hook: expand after a trigger key (space, Tab, Enter, punctuation), immediately, or only from the picker.
- Case propagation (`btw` → by the way, `Btw` → By the way, `BTW` → BY THE WAY), whole-word matching and Backspace right after an expansion to undo it.
- 39 placeholders for dynamic content: dates and times with offsets and business days, the clipboard, cursor position, fill-in forms (text, drop-downs, checkboxes, date pickers), nested snippets, text transforms, counters, calculations, key presses, delays and opt-in scripts.
- 1,822 built-in snippets in 13 groups: autocorrect, contractions, abbreviations, word shortcuts, email and business phrases, dates, symbols, LaTeX, emoji, kaomoji, developer and Markdown snippets, plus a personal group to fill in.
- Windows 11-style manager (Fluent theme, light and dark) with groups, per-app rules, live preview and a playground; a search picker (<kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>Space</kbd>), tray icon and pause shortcut (<kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>Shift</kbd>+<kbd>P</kbd>).
- Import from PhraseFlow/Beeftext JSON, CSV/TSV (TextExpander), AutoHotkey hotstrings and espanso YAML; export to JSON, CSV and AutoHotkey.
- Smart insertion: short text is typed, long text is pasted and the clipboard restored; keys typed while an expansion is inserted are replayed in order.
- Automatic backups, portable mode and optional start at sign-in.

[1.0.0]: https://github.com/az-pz/phraseflow/releases/tag/v1.0.0
