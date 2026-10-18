# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A small Windows desktop widget that shows Claude plan usage, as reported by Claude Code's `/usage` command.

- Lives in the system tray; tray icon menu shows/hides the window and exits the app.
- The window is small, borderless, and draggable by its body (no title bar).
- User can toggle "stay on top" (`Topmost`); persist that setting and the window position between runs.
- Closing the window hides it to the tray; it does not exit the app.

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
