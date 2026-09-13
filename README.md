# GLOBAL INVARIANT: Code Navigation, AST Inspection & Tool Priority

## 1. EXCLUSIVE LANGUAGE AST TOOLS FIRST (EXACTLY 1 CALL PER TOOL)
For ALL code inspection, discovery, and navigation across C#, JS, and mixed codebases:

- **FOR C# (.cs)**: Use `codenav` (or `C:\Users\charl\.local\bin\codenav.exe`):
  - `codenav index [path]` -> Directory overview with lines and summaries
  - `codenav skeleton <file.cs>` -> Classes, methods, records, properties with line spans
  - `codenav symbol <file.cs> <symbol>` -> Exact method / class implementation
  - `codenav slice "<file.cs>:<start>-<end>"` -> Surgical line block with line numbers

- **FOR JAVASCRIPT / TYPESCRIPT (.js, .ts, .jsx, .tsx)**: Use `codenav-js` (or `node c:\prj\codenav\js\bin\codenav-js.cjs`):
  - `codenav-js index [path]` -> JS/TS directory file list with header descriptions
  - `codenav-js skeleton <file.js>` -> Babel AST extracted classes, methods, functions, arrow fns
  - `codenav-js symbol <file.js> <symbol>` -> Exact function / class AST implementation
  - `codenav-js slice "<file.js>:<start>-<end>"` -> Precise line slice

- **STRICT PROHIBITIONS**:
  - **NEVER** use `view_file` (100% duplicative and prohibited).
  - **NEVER** use speculative `rg` searches to find or read files.
  - **RIPGREP (`rg`) IS PERMITTED ONLY IF AST TOOLS FAIL** or after an AST tool has identified an exact symbol and you need to find all external call sites / references across the codebase (`rg -n -i "symbolName"`).
  - **HARD BUDGET**: Maximum 4 focused tool calls per turn. Always terminate background processes.

---

## 2. TOOL INSTALLATION LOCATION
Both tools are installed in `C:\Users\charl\.local\bin`:
- `C:\Users\charl\.local\bin\codenav.exe` (C# Roslyn CLI)
- `C:\Users\charl\.local\bin\codenav-js.cmd` (JS Babel AST CLI)

---

## 3. HOW TO CHECK OUT, FIX BUGS, TEST, DEPLOY, AND CHECK IN

### GitHub Repository:
`https://github.com/LarryC42/CodeNav` (Local clone: `c:\prj\codenav`)

### Workflow:
1. **Checkout / Pull latest**:
   ```powershell
   git -C c:\prj\codenav pull origin master
   ```

2. **Fix Code & Add Tests**:
   - For C# fixes: Edit `c:\prj\codenav\src\CodeNav\Program.cs` and add test in `c:\prj\codenav\tests\CodeNav.Tests`.
   - For JS fixes: Edit `c:\prj\codenav\js\bin\codenav-js.cjs` and add test in `c:\prj\codenav\js\tests\test.cjs`.

3. **Run Unit Tests**:
   - C# Tests: `dotnet test c:\prj\codenav\tests\CodeNav.Tests`
   - JS Tests: `npm --prefix c:\prj\codenav\js test`

4. **Deploy Globally to `C:\Users\charl\.local\bin`**:
   ```powershell
   # Deploy C# tool:
   dotnet publish c:\prj\codenav\src\CodeNav\EventHorizon.CodeNav.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o "C:\Users\charl\.local\bin"
   Copy-Item "C:\Users\charl\.local\bin\EventHorizon.CodeNav.exe" "C:\Users\charl\.local\bin\codenav.exe" -Force

   # Deploy JS tool wrapper:
   @'
   @echo off
   node "c:\prj\codenav\js\bin\codenav-js.cjs" %*
   '@ | Set-Content -Path "C:\Users\charl\.local\bin\codenav-js.cmd" -Force
   ```

5. **Commit & Push to GitHub**:
   ```powershell
   git -C c:\prj\codenav add .
   git -C c:\prj\codenav commit -m "fix/feat: description of fix"
   git -C c:\prj\codenav push origin master
   ```
