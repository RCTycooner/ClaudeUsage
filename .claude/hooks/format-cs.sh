#!/usr/bin/env bash
# PostToolUse hook: whitespace-format the edited .cs file. Reads the hook payload (JSON) on stdin.
f=$(node -e "let s='';process.stdin.on('data',d=>s+=d).on('end',()=>{const j=JSON.parse(s);process.stdout.write((j.tool_input&&j.tool_input.file_path)||'')})")
case "$f" in
  *.cs) dotnet format whitespace "$(dirname "$f")" --folder --include "$(basename "$f")" ;;
esac
