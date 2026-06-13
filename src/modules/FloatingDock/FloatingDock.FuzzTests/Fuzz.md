# Floating Dock fuzz tests

Floating Dock accepts user-provided text drops/manual shortcut targets and reads JSON state from disk. These fuzz targets exercise those boundaries:

- `FuzzShortcutText`: parses arbitrary UTF-8 text as a shortcut target.
- `FuzzSettingsJson`: loads arbitrary bytes as `settings.json` and `dock.json` from an isolated temporary folder.

