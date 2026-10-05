# AGENTS.md - Lightshot Development Rules

This document specifies mandatory rules and policies for AI agents working in this repository.

## 1. Architectural Purity Rules

The project enforces strict assembly boundaries verified by `scripts/check-core.ps1` and `Lightshot.Architecture.Tests`:

### `Lightshot.Core`
- Target framework: `net10.0`
- Zero `PackageReference`
- Zero `ProjectReference`
- Pure BCL only: forbidden namespaces include `System.Windows.*`, `Windows.*`, `Microsoft.Win32.*`, `System.Drawing.*`
- No P/Invoke or native interop (`DllImport`, `LibraryImport`)

### `Lightshot.Rendering`
- Target framework: `net10.0`
- Project reference: `Lightshot.Core` only
- Package references: `SkiaSharp` and `HarfBuzzSharp` only
- No Windows API references

### Purity Enforcement
Every build executes `scripts/check-core.ps1`. Any violation immediately breaks the build with error.

## 2. Golden Image Policy

Visual regression tests rely on golden images.
- Updating goldens is only permitted via `scripts/test.ps1 -Tier Render -UpdateGoldens`.
- Never commit updated golden images without visually inspecting git diffs.
- Golden updates must be isolated to dedicated `test(goldens): ...` commits.

## 3. PowerShell Scripts Policy

All maintenance and execution scripts in `scripts/`:
- Must target Windows PowerShell 5.1 compatibility.
- Must be strictly ASCII-encoded (no UTF-8 BOM, no non-ASCII Unicode characters).
- Must avoid PowerShell 7+ syntax: no `&&`, no `||`, no ternary `? :`, no pipeline chain operators.
- Must return an explicit exit code (`exit $code`).
- Must pass `scripts/check-scripts.ps1` validation during `scripts/build.ps1`.

## 4. Execution from WSL & Windows

- All Windows commands (powershell, dotnet, winget) must execute on the NTFS path `D:\WORKSPACES\PERSONAL\lightshot`, never `\\wsl$`.
- From WSL, invoke via:
  ```bash
  /mnt/c/Windows/System32/WindowsPowerShell/v1.0/powershell.exe -NoProfile -File 'D:\WORKSPACES\PERSONAL\lightshot\scripts\<script>.ps1' </dev/null 2>&1 | tr -d '\r'
  ```
- In `bash -lc "..."`, always escape `$LASTEXITCODE` as `\$LASTEXITCODE`.
- Do not trust `powershell.exe` exit code alone; verify build and test outputs on disk.

## 5. Git & Commit Policy

- Commit messages follow Conventional Commits format: `<type>(<scope>): <summary>` (e.g., `feat(scaffold): ...`, `build(scripts): ...`, `test(scaffold): ...`, `docs: ...`).
- Stage files by explicit path only. Never use `git add -A` or `git add .`.
- Never use `git stash`.
- Never add AI attribution trailers, session links, or co-author lines.
- Never run `git config user.*`.
- Never force push.
