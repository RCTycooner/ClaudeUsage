# Implementation plan: Claude usage widget

Status: proposed (2026-10-18). Written after verifying the data source on this machine
(Claude Code 2.1.302, .NET SDK 10.0.401, Windows 11).

## 1. Decisions

### Data source (verified)

```
claude.exe -p "/usage" --output-format json --no-session-persistence --strict-mcp-config
```

- `/usage` is a **local command**: the JSON result has `"local_command":"usage"`, `num_turns: 0`,
  `total_cost_usd: 0`. No model call, no tokens burned. Safe to run every minute.
- Takes ~2 s, exit code 0, nothing on stderr. Works with stdin redirected and closed,
  `CreateNoWindow = true`, and an arbitrary working directory (use the app's data folder, not a
  project folder, so no project CLAUDE.md/hooks are picked up).
- `--no-session-persistence` stops it writing a session file under `~/.claude/projects/` per poll.
- `--strict-mcp-config` with no `--mcp-config` starts no MCP servers. Without it, `-p` mode starts the
  user's configured servers (stdio processes such as `node_repl` via cmd.exe, claude.ai connectors) on
  every poll, although `/usage` needs none of them.
- Do **not** use `--bare`: it disables OAuth, so `/usage` has nothing to report.
- The binary lives at `%USERPROFILE%\.local\bin\claude.exe` (also on PATH). Resolve via PATH first,
  fall back to that path, allow an override in settings.
- Pitfall: from Git Bash the argument `/usage` is rewritten to a Windows path and the CLI then
  **sends a real prompt to the model**. The app spawns the process directly, so this does not apply,
  but never test with Git Bash; use PowerShell.

The `result` string looks like this (captured 2026-10-18):

```
You are currently using your subscription to power your Claude Code usage

Current session: 3% used · resets Oct 19, 2am (Europe/Brussels)
Current week (all models): 17% used · resets Oct 20, 10am (Europe/Brussels)
Current week (Fable): 22% used · resets Oct 20, 10am (Europe/Brussels)

What's contributing to your limits usage?
... (per-skill breakdown, ignored)
```

Three lines are parsed; everything else is ignored. Percentages are integers. The reset time has no
year and names the machine's time zone.

### Storage

`%LOCALAPPDATA%\ClaudeUsage\` (inside the user profile; nothing important is lost if wiped):

- `history.jsonl`: one line per poll, e.g.
  `{"t":"2026-10-18T21:05:12+02:00","s":3,"w":17,"f":22,"sr":"2026-10-19T02:00:00+02:00","wr":"...","fr":"..."}`.
  Append-only; pruned to the last 8 days on load and once a day. Corrupt lines are skipped.
  Missing file = empty history, no error.
- `settings.json`: `{ "topmost": false, "left": 1200, "top": 80, "claudePath": null }`.
- `log.txt`: last errors (spawn failures, parse failures with the raw text), size-capped.

### Stack

- .NET 10, WPF, `net10.0-windows`, `UseWPF` + `UseWindowsForms` (for `NotifyIcon`).
- No chart library: a custom `FrameworkElement` with `OnRender` draws 3 polylines on a 0–100 % grid.
  That is ~120 lines and avoids a dependency for a 280×100 px plot.
- Tests: xunit project targeting the parser, history pruning and countdown formatting.

### Interpretation choices (change if wrong)

- "Tray shows stats when you click on it": left or right click opens one `ContextMenuStrip` whose
  first three (disabled) items are the stats with countdowns, then a separator, **Show UI**, **Quit**.
  Hovering shows the same three percentages in the tooltip (`NotifyIcon.Text`, 127-char limit).
- The graph plots all three series (session, week, Fable week) for the last 7 days. The session
  line is a 5-hour sawtooth; the week lines ramp and drop weekly.
- Gaps: if two consecutive samples are more than 5 minutes apart (machine asleep, app closed), the
  line is broken instead of drawing a slope through the gap.

## 2. Project layout

```
ClaudeUsage.sln
src/ClaudeUsage/
  ClaudeUsage.csproj              WinExe, net10.0-windows, UseWPF, UseWindowsForms, ApplicationIcon
  App.xaml / App.xaml.cs          Startup: single-instance mutex, composition root, owns TrayIcon
                                  and UsagePoller; ShutdownMode=OnExplicitShutdown
  Usage/UsageSnapshot.cs          record UsageSnapshot(DateTimeOffset Taken, UsageBucket Session,
                                  UsageBucket Week, UsageBucket FableWeek, string RawText)
                                  record UsageBucket(int Percent, DateTimeOffset? ResetsAt)
  Usage/IUsageSource.cs           Task<string> FetchRawAsync(CancellationToken)  (returns result text)
  Usage/ClaudeCliUsageSource.cs   Spawns claude.exe (args above), 30 s timeout, kills on timeout,
                                  parses the JSON envelope, throws UsageSourceException with detail
  Usage/UsageOutputParser.cs      static UsageSnapshot Parse(string text, DateTimeOffset now)
                                  Pure, regex-based, fully unit-tested
  Usage/UsagePoller.cs            PeriodicTimer(1 min) on a background task; first run immediately;
                                  never overlaps; raises Updated(UsageSnapshot) / Failed(string)
                                  marshalled to the dispatcher
  Storage/UsageHistoryStore.cs    Load(), Append(snapshot), Prune(TimeSpan)
  Storage/SettingsStore.cs        Load()/Save(AppSettings), debounced save
  Tray/TrayIcon.cs                NotifyIcon wrapper; Update(snapshot), ShowUiRequested, QuitRequested
  UI/MainWindow.xaml(.cs)         Borderless window, drag, header, stats, chart
  UI/MainViewModel.cs             INotifyPropertyChanged; exposes Buckets, History, LastUpdated, Error
  UI/UsageChart.cs                Custom element: 0–100 % y-axis, 7-day x-axis, 3 series
  UI/Converters.cs                Percent→brush (green <70, amber <90, red ≥90), countdown text
tests/ClaudeUsage.Tests/
  UsageOutputParserTests.cs       Real captured output; missing lines; year rollover; odd spacing
  UsageHistoryStoreTests.cs       Append/load round-trip, prune, corrupt line skipped
  CountdownFormatterTests.cs      "in 3h 12m", "in 1d 14h", "now"
  Fixtures/usage-2026-10-18.txt
```

## 3. Steps (each a PR off `main`)

### PR 1 — Skeleton: builds, runs, lives in the tray
1. Create solution, app project, test project; `Directory.Build.props` already sets nullable/analyzers.
2. `App.xaml.cs`: `ShutdownMode=OnExplicitShutdown`, named mutex `Local\ClaudeUsageWidget`
   (second instance exits after signalling the first to show its window via a named `EventWaitHandle`).
3. `TrayIcon` with a placeholder icon and a menu: **Show UI**, **Quit**. Dispose the icon on exit,
   otherwise a ghost icon stays in the tray.
4. `MainWindow`: `WindowStyle=None`, `ResizeMode=NoResize`, `AllowsTransparency=True`, rounded
   border, `ShowInTaskbar=False`. Body `MouseLeftButtonDown` → `DragMove()`. Header has a title,
   a pin `ToggleButton` bound to `Topmost`, and an × that hides the window. `Closing` → cancel + hide.
5. `SettingsStore`: persist `Topmost`, `Left`, `Top` on change (debounced 500 ms) and on quit.
   On load, if the saved rectangle is not inside `SystemParameters.VirtualScreen*`, centre instead.
6. Verify with `/run-widget`: tray icon, menu, drag, pin, close-to-tray, position restored.

### PR 2 — Data: source, parser, poller, history
1. `UsageOutputParser` with regexes anchored on the three line prefixes:
   `^Current session:`, `^Current week \(all models\):`, `^Current week \(Fable\):`.
   Each line: `(\d+)% used · resets (.+?)(?: \((.+)\))?$`.
   Reset parsing: `MMM d, h[:mm]tt` with the current year; if the result is more than 1 day in the
   past, add a year. Time zone: the name in parentheses via `TimeZoneInfo.FindSystemTimeZoneById`
   (IANA ids work on .NET 6+); on failure assume local time. Keep the raw text on the snapshot.
   Return partial results if one line is missing; throw only if none are found.
2. `ClaudeCliUsageSource`: resolve exe, `ProcessStartInfo` with
   `UseShellExecute=false`, all three streams redirected, `CreateNoWindow=true`,
   `WorkingDirectory` = data folder, close stdin immediately, read stdout async, 30 s timeout →
   `Kill(entireProcessTree: true)`. Deserialize the envelope; if `is_error` or `result` missing,
   throw with the first 500 chars of output.
3. `UsageHistoryStore` (JSONL) and `UsagePoller`. Startup order: load history → show last known
   snapshot immediately → start poller (first fetch at once, then every 60 s).
4. Tests for parser (fixture file), prune, round-trip. Add a `Fixtures/` capture of the real output.
5. Wire `App` to the poller: tray tooltip and menu items update on `Updated`; `Failed` sets an
   error state (tooltip "Claude usage: error — see window") and logs to `log.txt`.

### PR 3 — Window content
1. Three rows: label, `ProgressBar` (0–100, brush by threshold), `NN %`, `resets in 1d 14h`.
   Countdowns re-render every minute from the last snapshot even if a poll failed.
2. Footer: `updated 12 s ago` / `claude.exe not found` / `could not parse output (click to copy)`.
3. `UsageChart`: gridlines at 0/25/50/75/100, day ticks on the x-axis (last 7 × 24 h ending now),
   three series in the same colours as the bars, gap rule from §1, tiny legend. Redraw on
   `Updated` and on resize; data is at most 10 080 points per series, so plain `StreamGeometry`
   is fine.
4. Manual check via `/run-widget`; also seed a fake `history.jsonl` to eyeball a full 7-day plot.

### PR 4 — Polish (optional, pick what matters)
- Dynamic tray icon: render the highest of the three percentages as text onto a 16×16/32×32 bitmap,
  colour by threshold, so the number is visible without hovering.
- "Start with Windows" menu item (HKCU `...\Run` key).
- "Refresh now" menu item; "Open data folder" menu item.
- `dotnet publish -r win-x64 --self-contained false -p:PublishSingleFile=true` profile.
- README with screenshot.

## 4. Risks and how the plan handles them

| Risk | Handling |
|---|---|
| `/usage` text format is undocumented and may change between Claude Code releases | Parser is tolerant, returns partial data, keeps raw text; fixture test pins the current format; UI shows the raw text on parse failure. |
| Reset date has no year | Year inference rule in PR 2. |
| API-key users or logged-out state produce different output | Treated as a parse failure with a clear footer message; no crash. |
| `claude.exe` not on PATH for the GUI process (PATH differs from the shell) | Fallback to `%USERPROFILE%\.local\bin\claude.exe`, then `settings.json` override. |
| Each poll starts a 290 MB Node binary | ~2 s, idle otherwise; 30 s timeout and process-tree kill prevent pile-ups; polls never overlap. |
| Running instance locks the exe during builds | Already covered by `/run-widget`. |
| WPF/WinForms type ambiguities (`Application`, `MessageBox`, `Timer`) | Keep WinForms usage inside `Tray/TrayIcon.cs` only, with aliased usings. |

## 5. Out of scope for now

- Reading usage from any API endpoint or from `~/.claude` files directly.
- Notifications when a threshold is crossed (easy follow-up: `NotifyIcon.ShowBalloonTip`).
- Multiple accounts or machines.
