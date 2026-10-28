<p align="center"><img src="assets/logo.png" width="128" alt="BlocPlus logo"></p>

# BlocPlus

<p align="center"><a href="https://apps.microsoft.com/detail/9PBMQDG1JZV9"><img src="https://get.microsoft.com/images/en-us%20dark.svg" width="200" alt="Get it from Microsoft"></a></p>

A lightweight text editor for Windows that replaces Notepad. It is a single C# file with no dependencies, and it compiles with the compiler that already ships with Windows. You don't need Visual Studio, the .NET SDK or any extra download.

## Features

- **Tabs**: a **+** button, close buttons, drag to reorder, middle-click to close, Ctrl+Tab to switch tabs and Ctrl+Shift+T to reopen the last closed tab.
- **Sessions like modern Notepad**: closing the editor doesn't ask you to save. Open tabs and unsaved text come back the next time you start it, and a copy is saved every 30 seconds in case of a crash. You can turn this off in Settings.
- **Syntax highlighting** chosen from the file extension. Supported languages: C#, C/C++, Java/Kotlin/Swift, JavaScript/TypeScript, Python, Go, Rust, PHP, Ruby, PowerShell, Bash, Batch, SQL, Lua, HTML/XML, CSS, JSON, YAML, INI/TOML, Markdown, Dockerfile and Diff.
- **Light, dark or system theme.** The system theme follows the Windows setting as it changes.
- **Interface in 33 languages.** It follows the Windows display language, or pick one in Settings.
- **Line numbers**, word wrap, zoom and font selection.
- **Unlimited undo and redo.**
- **Find and replace** with case matching, whole words and regular expressions.
- **Multiple cursors**, like VS Code: Ctrl+click adds a cursor and Ctrl+drag adds a selection; typing, deleting, moving, copy and paste act on all of them. Escape goes back to one cursor.
- **Line editing**: duplicate, delete, move up or down, sort, trim trailing whitespace, change case, and indent or outdent a whole block with Tab and Shift+Tab.
- **Encodings**: detects and keeps UTF-8 (with or without BOM), UTF-16 LE/BE and ANSI, and keeps the original line endings (CRLF, LF or CR). You can convert both from the *Document* menu.
- **Safe saving**: the editor writes to a temporary file first, so a failed save never corrupts the original.
- **Detects external changes**: if another program changes an open file, the editor reloads it when you switch back to the window.
- **Live preview** for HTML, Markdown, reStructuredText, SVG and CSV/TSV: the editor on the left and the rendered result on the right, updated as you type and scrolled along with the editor (**View > Preview**, Ctrl+Shift+V).
- **LRC lyrics player**: the preview of an `.lrc` file plays its song or video (MP3, M4A/AAC or H.264 MP4) and highlights the line being sung; videos show the lyrics as subtitles. A file with the same name next to the `.lrc` loads automatically.
- **Find bar** above the text (Ctrl+F, Ctrl+H) that highlights every match on screen and counts them ("3 of 12"). From its **⋯** button you can also search or replace in all open tabs or in a folder and its subfolders; results appear in a panel, and a double click jumps to each one. Replacing in a folder never writes to disk: the files open in tabs with the changes made, ready to review and keep with *Save all*.
- **Matching brackets** and **other occurrences of the selected word** are outlined as you move around.
- **Command palette** (Ctrl+Shift+P): every menu command in one list, filtered as you type.
- **Folding**: fold the lines indented under a line from the arrow next to its number, or with Alt+← and Alt+→.
- **Show spaces and tabs**, **auto-closing brackets and quotes** (optional), and **format or minify JSON and XML** without reordering keys or touching numbers or comments.
- **Compare two tabs**: the differences open in a new tab, shown in color in the preview.
- **Pinned tabs** and a tab menu with *Close others*, *Close to the right*, *Copy path* and *Open folder*.
- **Printing** (Ctrl+P).
- The status bar's language, encoding and line ending can be clicked to change them.
- **One window**: opening a file while BlocPlus is running adds a tab to it. `file.txt:42` or `file.txt:42:7` opens at that line and column.
- **Installer and automatic updates**: see below.

## Installation

The easiest way is to **[get it from the Microsoft Store](https://apps.microsoft.com/detail/9PBMQDG1JZV9)**: Windows installs it and keeps it up to date. You can also install it from GitHub:

Download **`BlocPlus-Setup.exe`** from the [latest release](https://github.com/u0f/BlocPlus/releases/latest) and run it. Requirements: Windows 10 or 11.

- It installs for your user only, in `%LOCALAPPDATA%\Programs\BlocPlus`, so it doesn't need administrator rights.
- It adds BlocPlus to the Start menu, to the **Open with** list and to the right-click menu (**Editar con BlocPlus**).
- **To make it your default editor**, accept the offer at the end of the installation, or later choose **Help > Use as default editor**. Windows doesn't allow programs to set themselves as the default, so this opens *Settings > Default apps*, where you pick BlocPlus.
- **Updates**: every time it starts, BlocPlus checks GitHub for a new release. When there is one, a notice appears in the status bar. You can also check by hand with **Help > Check for updates**. Updating downloads the new version, checks that its SHA-256 matches the one GitHub publishes for it, and restarts the editor, keeping your tabs. You can turn off the automatic check in Settings.
- **To uninstall**, use *Settings > Apps > Installed apps > BlocPlus*. The uninstaller asks whether to also delete your data in `%APPDATA%\BlocPlus` (settings, recent files and the saved session) or keep it for a future install.

If you'd rather not install anything, download **`BlocPlus.exe`** and run it from anywhere. That is the portable version.

## Building

Requirements: Windows 10 or 11. The .NET Framework 4.x that comes with Windows is enough.

```bat
build.cmd
```

This creates `dist\BlocPlus.exe` and `dist\BlocPlus-Setup.exe`. Both are the same program: when the file is named `BlocPlus-Setup*.exe`, it installs itself.

`test.cmd` builds and runs the tests in `tests\`, and `tools\check-lang.ps1` lists the interface strings missing from any `lang\*.txt` (with `-Update` it also refreshes `lang\_source.txt`). GitHub Actions runs all three on every push.

To publish a version: bump `AssemblyVersion` in `src\BlocPlus.cs`, add the entry to `CHANGELOG.md`, build, and create a GitHub release tagged `vX.Y.Z` with both files attached. The updater looks for the `BlocPlus.exe` asset in the latest release and installs it only if its SHA-256 matches: GitHub publishes it for every uploaded asset, and a `BlocPlus.exe.sha256` asset works too.

## Keyboard shortcuts

| Action | Shortcut |
|---|---|
| New tab / Open / Save / Save as | Ctrl+T (or Ctrl+N) / Ctrl+O / Ctrl+S / Ctrl+Shift+S |
| Save all / Close tab | Ctrl+Alt+S / Ctrl+W |
| Reopen closed tab | Ctrl+Shift+T |
| Switch tab | Ctrl+Tab / Ctrl+Shift+Tab |
| Find / Replace / Go to line | Ctrl+F / Ctrl+H / Ctrl+G |
| Find next / previous | F3 / Shift+F3 |
| Find in all tabs | Ctrl+Shift+F |
| Command palette | Ctrl+Shift+P |
| Print | Ctrl+P |
| Format JSON or XML | Ctrl+Alt+F |
| Fold / Unfold block | Alt+← / Alt+→ |
| Duplicate / Delete line | Ctrl+D / Ctrl+Shift+K |
| Move line | Alt+↑ / Alt+↓ |
| Upper case / lower case | Ctrl+Shift+U / Ctrl+U |
| Indent / Outdent block | Tab / Shift+Tab |
| Word wrap | Alt+Z |
| Live preview | Ctrl+Shift+V |
| Zoom | Ctrl++ / Ctrl+- / Ctrl+0 / Ctrl+mouse wheel |
| Insert date and time | F5 |
| Settings | Ctrl+, |

## Where data is stored

Settings, recent files and the session (including unsaved text) are stored in `%APPDATA%\BlocPlus`.

## Limitations

- Very large files (tens of MB) take a while to open, and above 50 MB the editor asks first. Syntax highlighting, folding and match highlighting turn off above 3 MB.

## Contributing

BlocPlus is free and open source, and contributions are welcome: bug fixes, new features, more languages for syntax highlighting, translations or improvements to this documentation.

1. Fork the repository and create a branch for your change.
2. Make the change in `src\*.cs` and check that `build.cmd` still compiles without errors or warnings and that `test.cmd` passes. New interface strings go through `Tr.S("...")` in Spanish; run `tools\check-lang.ps1 -Update`.
3. Test it by hand in both the light and dark themes.
4. Add a line describing the change to [CHANGELOG.md](CHANGELOG.md).
5. Open a pull request explaining what it changes and why. Include a screenshot if it touches the interface.

Guidelines:

- No external dependencies: the project should keep building with the `csc.exe` that ships with Windows (C# 5).
- Keep it a text editor, not an IDE: small, fast and obvious features that don't get in the way of plain text.
- Follow the style of the existing code and keep changes small and focused. If you want to make a big change, open an issue first so we can discuss it.
- If you find a bug but don't want to fix it yourself, open an issue with the steps to reproduce it.

By submitting a pull request you agree that your contribution is licensed under the same GPL-3.0 license as the project.

## Changes

See [CHANGELOG.md](CHANGELOG.md).

## License

BlocPlus is free software released under the [GNU General Public License v3.0](LICENSE). You may use, study, modify and share it. If you distribute it, or any modified version, you must do so under the same license and include the source code.
