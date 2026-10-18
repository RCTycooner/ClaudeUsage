---
name: run-widget
description: Stop any running instance of the usage widget, rebuild it, and launch it to check a change in the real app. Use after UI or tray changes, or when a build fails because the .exe is locked.
---

1. Find the app project: the `.csproj` with `<OutputType>WinExe</OutputType>`. Its assembly name (`<AssemblyName>`, else the project file name) is the process name.
2. Stop any running instance (PowerShell):
   `Get-Process -Name <AssemblyName> -ErrorAction SilentlyContinue | Stop-Process -Force`
3. Build: `dotnet build <path-to-csproj>`. If it still reports a locked file, find the locking process and report it rather than retrying in a loop.
4. Launch the built exe directly, detached so the session isn't blocked:
   `Start-Process '<project-dir>\bin\Debug\net10.0-windows\<AssemblyName>.exe'`
   Don't use `dotnet run`: `dotnet.exe` is a console program, so it opens a console window next to the widget.
5. Confirm the process is running (`Get-Process -Name <AssemblyName>`), then tell the user what to check by hand (tray icon, dragging, stay-on-top toggle) — you can't see the screen unless computer-use tools are available.
