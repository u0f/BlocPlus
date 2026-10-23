# BlocPlus

A lightweight text editor for Windows that replaces Notepad. It is a single C# file with no dependencies, and it compiles with the compiler that already ships with Windows. You don't need Visual Studio, the .NET SDK or any extra download.

## Features

- **Tabs**: a **+** button, close buttons, drag to reorder, middle-click to close, Ctrl+Tab to switch tabs and Ctrl+Shift+T to reopen the last closed tab.
- **Sessions like modern Notepad**: closing the editor doesn't ask you to save. Open tabs and unsaved text come back the next time you start it, and a copy is saved every 30 seconds in case of a crash. You can turn this off in Settings.
- **Syntax highlighting** chosen from the file extension. Supported languages: C#, C/C++, Java/Kotlin/Swift, JavaScript/TypeScript, Python, Go, Rust, PHP, Ruby, PowerShell, Bash, Batch, SQL, Lua, HTML/XML, CSS, JSON, YAML, INI/TOML, Markdown, Dockerfile and Diff.
- **Light, dark or system theme.** The system theme follows the Windows setting as it changes.
- **Line numbers**, word wrap, zoom and font selection.
- **Unlimited undo and redo.**
- **Find and replace** with case matching, whole words and regular expressions.
- **Line editing**: duplicate, delete, move up or down, sort, trim trailing whitespace, change case, and indent or outdent a whole block with Tab and Shift+Tab.
- **Encodings**: detects and keeps UTF-8 (with or without BOM), UTF-16 LE/BE and ANSI, and keeps the original line endings (CRLF, LF or CR). You can convert both from the *Document* menu.
- **Safe saving**: the editor writes to a temporary file first, so a failed save never corrupts the original.
- **Detects external changes**: if another program changes an open file, the editor reloads it when you switch back to the window.

## Building

Requirements: Windows 10 or 11. The .NET Framework 4.x that comes with Windows is enough.

```bat
build.cmd
```

This creates `BlocPlus.exe` next to the source code.

To use it as your default editor, right-click a `.txt` file and choose **Open with > Choose another app > BlocPlus.exe**.

## Keyboard shortcuts

| Action | Shortcut |
|---|---|
| New tab / Open / Save / Save as | Ctrl+T (or Ctrl+N) / Ctrl+O / Ctrl+S / Ctrl+Shift+S |
| Save all / Close tab | Ctrl+Alt+S / Ctrl+W |
| Reopen closed tab | Ctrl+Shift+T |
| Switch tab | Ctrl+Tab / Ctrl+Shift+Tab |
| Find / Replace / Go to line | Ctrl+F / Ctrl+H / Ctrl+G |
| Find next / previous | F3 / Shift+F3 |
| Duplicate / Delete line | Ctrl+D / Ctrl+Shift+K |
| Move line | Alt+↑ / Alt+↓ |
| Upper case / lower case | Ctrl+Shift+U / Ctrl+U |
| Indent / Outdent block | Tab / Shift+Tab |
| Word wrap | Alt+Z |
| Zoom | Ctrl++ / Ctrl+- / Ctrl+0 / Ctrl+mouse wheel |
| Insert date and time | F5 |
| Settings | Ctrl+, |

## Where data is stored

Settings, recent files and the session (including unsaved text) are stored in `%APPDATA%\BlocPlus`.

## Limitations

- Very large files (tens of MB) take a while to open. Syntax highlighting turns off above 3 MB.
- Printing is not supported.

## Contributing

BlocPlus is free and open source, and contributions are welcome: bug fixes, new features, more languages for syntax highlighting, translations or improvements to this documentation.

1. Fork the repository and create a branch for your change.
2. Make the change in `BlocPlus.cs` and check that `build.cmd` still compiles without errors or warnings.
3. Test it by hand in both the light and dark themes.
4. Add a line describing the change to [CHANGELOG.md](CHANGELOG.md).
5. Open a pull request explaining what it changes and why. Include a screenshot if it touches the interface.

Guidelines:

- Keep it to a single file with no external dependencies. The project should keep building with the `csc.exe` that ships with Windows.
- Follow the style of the existing code and keep changes small and focused. If you want to make a big change, open an issue first so we can discuss it.
- If you find a bug but don't want to fix it yourself, open an issue with the steps to reproduce it.

By submitting a pull request you agree that your contribution is licensed under the same GPL-3.0 license as the project.

## Changes

See [CHANGELOG.md](CHANGELOG.md).

## License

BlocPlus is free software released under the [GNU General Public License v3.0](LICENSE). You may use, study, modify and share it. If you distribute it, or any modified version, you must do so under the same license and include the source code.
