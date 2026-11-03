# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A small Windows desktop widget that shows Claude plan usage, as reported by Claude Code's `/usage` command.

- Lives in the system tray; tray icon menu shows/hides the window and exits the app.
- The window is small, borderless, and draggable by its body (no title bar).
- User can toggle "stay on top" (`Topmost`); persist that setting and the window position between runs.
- Closing the window hides it to the tray; it does not exit the app.

## Git / Shell Conventions

### Git commits on Windows

Never pass multi-line or quoted commit messages inline through PowerShell 5.1. It splits the message at embedded quotes. Always write the message to a temp file and run `git commit -F <file>`.

### Line endings

This repo uses CRLF. Do not use `sed -i` or ad-hoc Python/PowerShell rewrite scripts on tracked files. They strip CRs and mangle encoding. Use the Edit/Write tools instead. If you must script edits, preserve CRLF and UTF-8, then check `git diff` for whole-file line-ending changes before committing.

## Tooling Environment

### Worktree guard and sandbox

The path-isolation guard misreads inline commands that contain `D:/Git-Tom` paths, and it refuses compound commands and heredocs. For anything non-trivial, write a script file and run that instead. When launching subagents for parallel work, use worktree isolation (`isolation: worktree`). Do not hand-create worktrees for agents, because they cannot write to them.

## Stack

- C# / .NET 10, WPF, Windows-only (`net10.0-windows`). No cross-platform abstractions needed.
- Tray icon: WinForms `NotifyIcon` (`<UseWindowsForms>true</UseWindowsForms>`) or a small tray library — WPF has no built-in tray support. Watch for `Application`/`MessageBox` type ambiguities between WPF and WinForms namespaces.

## Usage data

- Source is Claude Code's `/usage` output. **How the app obtains it (invoking the CLI, parsing output, or something else) is not decided yet** — ask before committing to an approach; don't invent an API.
- Keep fetching/parsing behind its own class so the source can be swapped, and poll on a timer rather than per UI event.

## Gotchas

- A running instance locks its `.exe`, so `dotnet build` fails while the widget is open. Use `/run-widget`, or kill the process before building.

## Workflow

- GitHub, PR-based: work on a feature branch, never commit directly to `main`.

### Merge to main checklist

1. Build and run all tests on the branch. Never merge unbuilt code.
2. Run `git worktree list` and check which branch is checked out in the main folder before updating local main.
3. Merge, then push main unless told otherwise.
4. Report the merge commit hash and say explicitly whether it was pushed.
5. Remove the merged worktree and branch.
