# Claude Usage

A small Windows tray widget that shows your Claude plan usage (session, week, and the per-model week, e.g. "Fable week") as reported by Claude Code's `/usage` command. The tray icon shows the highest percentage, coloured green / amber / red (under 70 / under 90 / 90 and over). If a poll fails, the icon keeps the last known number with a red dot in the corner and the tooltip starts with "⚠ last poll failed"; it shows "!" only if there has never been any data.

![Claude Usage widget](docs/screenshot.png)

- Borderless, draggable window; closing it hides it to the tray.
- Optional "stay on top"; window position and that setting are remembered.
- A 7-day chart in the widget. The expand button opens a resizable history window over everything stored (up to 90 days): 1D / 7D / 30D / All presets, zoom (buttons, mouse wheel at the cursor, + / −), scrolling (scroll bar, drag, Shift+wheel, arrow keys), a "Now" button that follows new readings, and a hover read-out of the values at any point.
- Tray menu: current stats, Show UI, Refresh now, Open data folder, Start with Windows, Quit.
- Start with Windows adds `"<path>\ClaudeUsage.exe" --hidden` to `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, so it starts in the tray without opening the window. If the exe moves, the menu item shows unchecked; tick it again to update the entry.

## Requirements

- Windows 10/11
- [.NET 10 runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (desktop)
- [Claude Code](https://claude.com/claude-code) installed and logged in with a subscription (Pro/Max)

## How it gets data

Once a minute the app runs `claude -p "/usage" --output-format json --no-session-persistence`. This is a local command: it does not call the model and uses no tokens. The output text is parsed into the three usage buckets; if the format changes, the window shows the raw text.

`claude` is looked up in this order: `claudePath` in `settings.json`, `claude.exe` on `PATH`, `%USERPROFILE%\.local\bin\claude.exe` (native installer), then an npm `claude.cmd` shim on `PATH` or in `%APPDATA%\npm`. A `.cmd` shim is run through `cmd.exe /d /s /c`. The child process does not inherit the variables of a Claude Code session the widget may have been started from (`CLAUDECODE`, `CLAUDE_CODE_ENTRYPOINT`, session ids), and runs with `DISABLE_AUTOUPDATER=1`.

> **Do not test `/usage` from Git Bash.** MSYS rewrites `/usage` into a path, which turns it into a real, billed prompt. Use PowerShell or cmd.

## Where data lives

`%LOCALAPPDATA%\ClaudeUsage\`

| File | Contents |
|---|---|
| `history.jsonl` | one sample per poll, kept for 90 days; used for the charts |
| `settings.json` | stay-on-top, window position, optional `claude` path override |
| `log.txt` | errors, unparsable output, crashes, and usage lines that disappeared from the output |

All files are UTF-8 without BOM.

## Build, run, test, publish

```powershell
dotnet build ClaudeUsage.sln
dotnet run --project src/ClaudeUsage
dotnet test
dotnet publish src/ClaudeUsage -p:PublishProfile=win-x64   # -> publish/ClaudeUsage.exe (framework-dependent, single file)
```

A running instance locks its `.exe`; stop it before building.

Command-line switches:

- `--hidden` starts in the tray without showing the window (used by Start with Windows).
- `--snapshot out.png` renders the window with synthetic data to a PNG and exits (debug aid).
- `--snapshot-history out.png` does the same for the history window, with 30 days of synthetic data.
