// Roslyn Code Navigation CLI: AST Symbol Skeleton, Symbol Extractor, Block Slicer, Session Coverage Tracker, and Git-aware Indexer
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EventHorizon.CodeNav;

public class Program
{
    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintUsage();
            return 0;
        }

        string command = args[0].ToLowerInvariant();
        string[] cmdArgs = args.Skip(1).ToArray();

        try
        {
            return command switch
            {
                "reset" or "--reset" or "clear" => RunReset(cmdArgs),
                "status" or "coverage" => RunCoverage(cmdArgs),
                "skeleton" or "sk" => RunSkeleton(cmdArgs),
                "symbol" or "symbols" or "sym" => RunSymbols(cmdArgs),
                "slice" or "sl" => RunSlice(cmdArgs),
                "index" or "idx" => RunIndex(cmdArgs),
                _ => HandleDirectArgumentOrError(args)
            };
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"[Error] {ex.Message}");
            Console.ResetColor();
            return 1;
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine(@"
=== EventHorizon CodeNav (C# & Roslyn AST Inspector) ===

Usage:
  dotnet run --project src/EventHorizon.CodeNav -- <command> [arguments]

Commands:
  reset, --reset
      Resets the session coverage tracker so all code can be inspected fresh.

  status, coverage
      Displays which files and line spans have been returned/inspected during this session.

  skeleton, sk <file.cs> [...]
      Displays high-level AST symbol map (classes, records, interfaces, enums,
      structs, methods, constructors, properties) with exact line spans & doc comments.

  symbol, sym ""<file1.cs>: Sym1, Sym2; <file2.cs>: Sym3, Sym4"" [--all | --force] [...]
      Extracts full source code for specific methods, classes, properties, or records across multiple files.
      Automatically omits code that has already been returned this session unless --all is passed.
      If a symbol is not found, clearly lists all available symbols in the target file.

  slice, sl ""<file1.cs>:<start>-<end>; <file2.cs>:<start>-<end>"" [--all | --force] [...]
      Prints line-numbered slice of file content across specified line ranges across multiple files.
      Deduplicates against previously returned line ranges in this session.

  index, idx [directory]
      Generates compact file index with header descriptions for all C# / JS files.

Examples:
  dotnet run --project src/EventHorizon.CodeNav -- reset
  dotnet run --project src/EventHorizon.CodeNav -- skeleton src/EventHorizon.Infrastructure/Repositories/ProblemRepository.cs
  dotnet run --project src/EventHorizon.CodeNav -- symbol ""src/EventHorizon.Infrastructure/Configuration/AppConfig.cs: Load, Validate; src/EventHorizon.Infrastructure/AI/AiClients.cs: MockLlmClient""
  dotnet run --project src/EventHorizon.CodeNav -- slice ""src/EventHorizon.Api/Program.cs: 15-60; src/EventHorizon.Infrastructure/AI/AiClients.cs: 1-40""
  dotnet run --project src/EventHorizon.CodeNav -- index src/EventHorizon.Infrastructure
");
    }

    private static int HandleDirectArgumentOrError(string[] args)
    {
        string first = args[0];
        if (first.Equals("--reset", StringComparison.OrdinalIgnoreCase) || first.Equals("reset", StringComparison.OrdinalIgnoreCase))
        {
            return RunReset(Array.Empty<string>());
        }
        // If user passed a slice pattern like "file.cs:10-20" directly
        if (Regex.IsMatch(first, @":\d+-\d+$"))
        {
            return RunSlice(args);
        }
        // If user passed a symbol pattern like "file.cs: Symbol" directly
        if (first.Contains(':'))
        {
            return RunSymbols(args);
        }
        // If user passed a .cs or .js file directly, default to skeleton
        if (File.Exists(first) && (first.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || first.EndsWith(".js", StringComparison.OrdinalIgnoreCase)))
        {
            return RunSkeleton(args);
        }
        // If user passed a directory directly, default to index
        if (Directory.Exists(first))
        {
            return RunIndex(args);
        }

        Console.Error.WriteLine($"Unknown command or path: '{first}'. Run with --help for usage instructions.");
        return 1;
    }

    #region 0. SESSION TRACKING & DEDUPLICATION

    private static int RunReset(string[] args)
    {
        SessionTracker.Reset();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("[Session] Code navigation history and coverage tracker successfully reset.");
        Console.ResetColor();
        return 0;
    }

    private static int RunCoverage(string[] args)
    {
        var session = SessionTracker.Load();
        Console.WriteLine($"\n=== CODENAV SESSION COVERAGE ({session.Entries.Count} files tracked) ===");
        if (session.Entries.Count == 0)
        {
            Console.WriteLine("No files inspected yet in this session.");
            return 0;
        }

        foreach (var (file, ranges) in session.Entries.OrderBy(e => e.Key))
        {
            int totalLinesInspected = ranges.Sum(r => r.End - r.Start + 1);
            string rangeSummary = string.Join(", ", ranges.Select(r => $"{r.Start}-{r.End}"));
            Console.WriteLine($"{file} -> {totalLinesInspected} lines viewed: [{rangeSummary}]");
        }
        return 0;
    }

    #endregion

    #region 1. SKELETON (Roslyn AST Symbol Outline)

    private static int RunSkeleton(string[] files)
    {
        if (files.Length == 0)
        {
            Console.Error.WriteLine("Usage: skeleton <file1.cs> [file2.cs] ...");
            return 1;
        }

        foreach (var filePath in files)
        {
            string fullPath = Path.GetFullPath(filePath);
            if (!File.Exists(fullPath))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"[Warning] File not found: {filePath}");
                Console.ResetColor();
                continue;
            }

            if (fullPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                PrintCSharpSkeleton(fullPath);
            }
            else
            {
                // Generic JavaScript / text fallback
                PrintGenericSkeleton(fullPath);
            }
        }
        return 0;
    }

    private static void PrintCSharpSkeleton(string filePath)
    {
        string code = File.ReadAllText(filePath);
        var tree = CSharpSyntaxTree.ParseText(code);
        var root = tree.GetCompilationUnitRoot();
        var lines = code.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

        var symbols = GetCSharpSymbols(root);
        string relPath = Path.GetRelativePath(Directory.GetCurrentDirectory(), filePath).Replace('\\', '/');
        Console.WriteLine($"\n=== SKELETON: {Path.GetFileName(filePath)} ({symbols.Count} symbols, {lines.Length} lines) [{relPath}] ===");
        
        foreach (var s in symbols)
        {
            string range = $"L{s.StartLine.ToString().PadLeft(4)}-L{s.EndLine.ToString().PadRight(4)}";
            string doc = string.IsNullOrWhiteSpace(s.DocSummary) ? "" : $" // {s.DocSummary}";
            Console.WriteLine($"{range} | {s.Kind.PadRight(9)} | {s.Name}{doc}");
        }
    }

    private static List<AstSymbolInfo> GetCSharpSymbols(CompilationUnitSyntax root)
    {
        var symbols = new List<AstSymbolInfo>();
        foreach (var node in root.DescendantNodes())
        {
            AstSymbolInfo? info = node switch
            {
                ClassDeclarationSyntax c => new AstSymbolInfo("CLASS", c.Identifier.Text, GetSpan(c), GetDocSummary(c)),
                RecordDeclarationSyntax r => new AstSymbolInfo("RECORD", r.Identifier.Text, GetSpan(r), GetDocSummary(r)),
                InterfaceDeclarationSyntax i => new AstSymbolInfo("INTERFACE", i.Identifier.Text, GetSpan(i), GetDocSummary(i)),
                StructDeclarationSyntax s => new AstSymbolInfo("STRUCT", s.Identifier.Text, GetSpan(s), GetDocSummary(s)),
                EnumDeclarationSyntax e => new AstSymbolInfo("ENUM", e.Identifier.Text, GetSpan(e), GetDocSummary(e)),
                MethodDeclarationSyntax m => new AstSymbolInfo("METHOD", FormatMethod(m), GetSpan(m), GetDocSummary(m)),
                ConstructorDeclarationSyntax ctor => new AstSymbolInfo("CTOR", $"{ctor.Identifier.Text}({string.Join(", ", ctor.ParameterList.Parameters.Select(p => $"{p.Type} {p.Identifier}"))})", GetSpan(ctor), GetDocSummary(ctor)),
                PropertyDeclarationSyntax p when p.Parent is TypeDeclarationSyntax => new AstSymbolInfo("PROP", $"{p.Type} {p.Identifier.Text}", GetSpan(p), GetDocSummary(p)),
                _ => null
            };

            if (info != null)
            {
                symbols.Add(info);
            }
        }
        return symbols;
    }

    private static string FormatMethod(MethodDeclarationSyntax m)
    {
        string returnType = m.ReturnType.ToString();
        string name = m.Identifier.Text;
        string parameters = string.Join(", ", m.ParameterList.Parameters.Select(p => $"{p.Type} {p.Identifier.Text}"));
        return $"{returnType} {name}({parameters})";
    }

    private static (int Start, int End) GetSpan(SyntaxNode node)
    {
        var lineSpan = node.GetLocation().GetLineSpan();
        int startLine = lineSpan.StartLinePosition.Line + 1;
        int endLine = lineSpan.EndLinePosition.Line + 1;
        return (startLine, endLine);
    }

    private static string GetDocSummary(SyntaxNode node)
    {
        var trivia = node.GetLeadingTrivia();
        foreach (var t in trivia)
        {
            if (t.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) ||
                t.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia))
            {
                string text = t.ToString();
                var match = Regex.Match(text, @"<summary>\s*([\s\S]*?)\s*</summary>");
                if (match.Success)
                {
                    string summary = Regex.Replace(match.Groups[1].Value, @"[\r\n]+", " ").Trim();
                    summary = Regex.Replace(summary, @"^\s*///?\s*", "", RegexOptions.Multiline).Trim();
                    return summary;
                }
            }
            if (t.IsKind(SyntaxKind.SingleLineCommentTrivia))
            {
                string comment = t.ToString().TrimStart('/', ' ').Trim();
                if (!string.IsNullOrEmpty(comment)) return comment;
            }
        }
        return "";
    }

    private static void PrintGenericSkeleton(string filePath)
    {
        string[] lines = File.ReadAllLines(filePath);
        string relPath = Path.GetRelativePath(Directory.GetCurrentDirectory(), filePath).Replace('\\', '/');
        Console.WriteLine($"\n=== SKELETON: {Path.GetFileName(filePath)} ({lines.Length} lines) [{relPath}] ===");
        
        var classRegex = new Regex(@"^(?:export\s+)?class\s+([A-Za-z0-9_]+)", RegexOptions.Compiled);
        var methodRegex = new Regex(@"^\s*(?:async\s+)?(?:static\s+)?([A-Za-z0-9_]+)\s*\(([^)]*)\)", RegexOptions.Compiled);

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            var cm = classRegex.Match(line);
            if (cm.Success)
            {
                Console.WriteLine($"L{(i + 1).ToString().PadLeft(4)} | CLASS     | {cm.Groups[1].Value}");
                continue;
            }
            var mm = methodRegex.Match(line);
            if (mm.Success && !new[] { "if", "for", "while", "switch", "catch" }.Contains(mm.Groups[1].Value))
            {
                Console.WriteLine($"L{(i + 1).ToString().PadLeft(4)} | METHOD    | {mm.Groups[1].Value}({mm.Groups[2].Value})");
            }
        }
    }

    #endregion

    #region 2. SYMBOLS (Extract Code by Exact Symbol Name with Deduplication)

    private static int RunSymbols(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine(@"Usage: symbol ""<file1.cs>: Sym1, Sym2; <file2.cs>: Sym3, Sym4"" [--all | --force] [...]");
            return 1;
        }

        bool forceAll = args.Any(a => a is "--all" or "-a" or "--force" or "-f");
        var filteredArgs = args.Where(a => a is not ("--all" or "-a" or "--force" or "-f")).ToArray();

        var session = SessionTracker.Load();

        // Support both multiple CLI arguments and semicolon-separated batches in a single argument
        var queries = filteredArgs
            .SelectMany(a => a.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(q => !string.IsNullOrWhiteSpace(q))
            .ToList();

        foreach (string rawArg in queries)
        {
            int colonIndex = rawArg.IndexOf(':');
            if (colonIndex == -1)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"[Warning] Invalid syntax: '{rawArg}'. Format must be 'filePath: Symbol1, Symbol2'");
                Console.ResetColor();
                continue;
            }

            string file = rawArg[..colonIndex].Trim();
            string[] symbolNames = rawArg[(colonIndex + 1)..]
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            string fullPath = Path.GetFullPath(file);
            if (!File.Exists(fullPath))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"[Warning] File not found: {file}");
                Console.ResetColor();
                continue;
            }

            if (fullPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                ExtractCSharpSymbols(fullPath, symbolNames, session, forceAll);
            }
            else
            {
                ExtractGenericSymbols(fullPath, symbolNames, session, forceAll);
            }
        }

        session.Save();
        return 0;
    }

    private static void ExtractCSharpSymbols(string filePath, string[] symbolNames, SessionTracker session, bool forceAll)
    {
        string code = File.ReadAllText(filePath);
        var tree = CSharpSyntaxTree.ParseText(code);
        var root = tree.GetCompilationUnitRoot();
        var lines = code.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        string relPath = Path.GetRelativePath(Directory.GetCurrentDirectory(), filePath).Replace('\\', '/');

        var allSymbols = GetCSharpSymbols(root);

        foreach (string targetSymbol in symbolNames)
        {
            bool found = false;

            foreach (var node in root.DescendantNodes())
            {
                string? identifier = node switch
                {
                    ClassDeclarationSyntax c => c.Identifier.Text,
                    RecordDeclarationSyntax r => r.Identifier.Text,
                    InterfaceDeclarationSyntax i => i.Identifier.Text,
                    StructDeclarationSyntax s => s.Identifier.Text,
                    EnumDeclarationSyntax e => e.Identifier.Text,
                    MethodDeclarationSyntax m => m.Identifier.Text,
                    ConstructorDeclarationSyntax ctor => ctor.Identifier.Text,
                    PropertyDeclarationSyntax p => p.Identifier.Text,
                    _ => null
                };

                if (identifier != null && identifier.Equals(targetSymbol, StringComparison.OrdinalIgnoreCase))
                {
                    var span = GetSpan(node);
                    int startLine = span.Start;
                    int endLine = span.End;

                    if (!forceAll && session.IsRangeFullyInspected(relPath, startLine, endLine))
                    {
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.WriteLine($"\n[CodeNav Deduplicated] Symbol '{identifier}' in {relPath} (Lines {startLine}-{endLine}) was already returned this session. Use --all or 'reset' to view again.");
                        Console.ResetColor();
                        found = true;
                        break;
                    }

                    Console.WriteLine($"\n=== SYMBOL: {relPath} -> {identifier} [Lines {startLine}-{endLine}] ===");
                    
                    int startIdx = Math.Max(0, startLine - 1);
                    int endIdx = Math.Min(lines.Length, endLine);

                    for (int i = startIdx; i < endIdx; i++)
                    {
                        Console.WriteLine($"{(i + 1).ToString().PadLeft(4)}: {lines[i]}");
                    }

                    session.MarkRangeInspected(relPath, startLine, endLine);
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"[Not Found] Symbol '{targetSymbol}' was not found in {relPath}");
                Console.WriteLine($"  Available symbols in {Path.GetFileName(filePath)} ({allSymbols.Count}):");
                var topSymbols = allSymbols.Take(25).Select(s => $"{s.Kind} {s.Name} (L{s.StartLine}-L{s.EndLine})");
                foreach (var symStr in topSymbols)
                {
                    Console.WriteLine($"    - {symStr}");
                }
                if (allSymbols.Count > 25)
                {
                    Console.WriteLine($"    ... and {allSymbols.Count - 25} more (use 'skeleton {relPath}' to view full outline)");
                }
                Console.ResetColor();
            }
        }
    }

    private static void ExtractGenericSymbols(string filePath, string[] symbolNames, SessionTracker session, bool forceAll)
    {
        string[] lines = File.ReadAllLines(filePath);
        string relPath = Path.GetRelativePath(Directory.GetCurrentDirectory(), filePath).Replace('\\', '/');

        foreach (string sym in symbolNames)
        {
            var pattern = new Regex($@"(?:function\s+{Regex.Escape(sym)}|class\s+{Regex.Escape(sym)}|{Regex.Escape(sym)}\s*\()", RegexOptions.Compiled);
            int startLine = -1;

            for (int i = 0; i < lines.Length; i++)
            {
                if (pattern.IsMatch(lines[i]))
                {
                    startLine = i + 1;
                    break;
                }
            }

            if (startLine == -1)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"[Not Found] Symbol '{sym}' was not found in {relPath}");
                Console.ResetColor();
                continue;
            }

            int openBraces = 0;
            bool foundOpen = false;
            int endLine = lines.Length;

            for (int i = startLine - 1; i < lines.Length; i++)
            {
                foreach (char ch in lines[i])
                {
                    if (ch == '{') { openBraces++; foundOpen = true; }
                    else if (ch == '}') { openBraces--; }
                }
                if (foundOpen && openBraces == 0)
                {
                    endLine = i + 1;
                    break;
                }
            }

            if (!forceAll && session.IsRangeFullyInspected(relPath, startLine, endLine))
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"\n[CodeNav Deduplicated] Symbol '{sym}' in {relPath} (Lines {startLine}-{endLine}) was already returned this session. Use --all or 'reset' to view again.");
                Console.ResetColor();
                continue;
            }

            Console.WriteLine($"\n=== SYMBOL: {relPath} -> {sym} [Lines {startLine}-{endLine}] ===");
            for (int i = startLine - 1; i < endLine; i++)
            {
                Console.WriteLine($"{(i + 1).ToString().PadLeft(4)}: {lines[i]}");
            }
            session.MarkRangeInspected(relPath, startLine, endLine);
        }
    }

    #endregion

    #region 3. SLICE (Line Range Extraction with Deduplication)

    private static int RunSlice(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine(@"Usage: slice ""<file1.cs>:<start>-<end>; <file2.cs>:<start>-<end>"" [--all | --force] [...]");
            return 1;
        }

        bool forceAll = args.Any(a => a is "--all" or "-a" or "--force" or "-f");
        var requests = args
            .Where(a => a is not ("--all" or "-a" or "--force" or "-f"))
            .SelectMany(a => a.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(q => !string.IsNullOrWhiteSpace(q))
            .ToArray();

        var session = SessionTracker.Load();

        foreach (string req in requests)
        {
            int lastColon = req.LastIndexOf(':');
            if (lastColon == -1)
            {
                Console.Error.WriteLine($"[Invalid Slice Format] '{req}'. Expected <filePath>:<startLine>-<endLine>");
                continue;
            }

            string filePath = req[..lastColon].Trim();
            string rangePart = req[(lastColon + 1)..].Trim();
            string[] bounds = rangePart.Split('-');
            if (bounds.Length != 2 || !int.TryParse(bounds[0], out int startLine) || !int.TryParse(bounds[1], out int endLine))
            {
                Console.Error.WriteLine($"[Invalid Range Format] '{rangePart}' in '{req}'. Expected e.g. 10-50");
                continue;
            }

            string fullPath = Path.GetFullPath(filePath);
            if (!File.Exists(fullPath))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"[Warning] File not found: {filePath}");
                Console.ResetColor();
                continue;
            }

            string[] lines = File.ReadAllLines(fullPath);
            string relPath = Path.GetRelativePath(Directory.GetCurrentDirectory(), fullPath).Replace('\\', '/');

            int actualStart = Math.Max(1, startLine);
            int actualEnd = Math.Min(lines.Length, endLine);

            if (!forceAll && session.IsRangeFullyInspected(relPath, actualStart, actualEnd))
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"\n[CodeNav Deduplicated] Slice {relPath}:{actualStart}-{actualEnd} was already returned this session. Use --all or 'reset' to view again.");
                Console.ResetColor();
                continue;
            }

            Console.WriteLine($"\n=== SLICE: {relPath} (Lines {actualStart}-{actualEnd} of {lines.Length}) ===");
            for (int i = actualStart - 1; i < actualEnd; i++)
            {
                Console.WriteLine($"{(i + 1).ToString().PadLeft(4)}: {lines[i]}");
            }

            session.MarkRangeInspected(relPath, actualStart, actualEnd);
        }

        session.Save();
        return 0;
    }

    #endregion

    #region 4. INDEX (Directory Scan & Header Extraction)

    private static int RunIndex(string[] args)
    {
        string targetDir = args.Length > 0 ? Path.GetFullPath(args[0]) : Directory.GetCurrentDirectory();
        if (!Directory.Exists(targetDir))
        {
            Console.Error.WriteLine($"Directory not found: {targetDir}");
            return 1;
        }

        var results = new List<(string RelPath, string Description, int LineCount)>();
        ScanDirectory(targetDir, targetDir, results);

        Console.WriteLine($"\n=== COMPACT FILE INDEX: {Path.GetFileName(targetDir)} ({results.Count} files) ===");
        int maxLen = Math.Min(60, Math.Max(35, results.Count > 0 ? results.Max(r => r.RelPath.Length) : 35));

        foreach (var (relPath, desc, lineCount) in results.OrderBy(r => r.RelPath))
        {
            string p = relPath.PadRight(maxLen);
            string linesStr = $"({lineCount}L)".PadLeft(7);
            Console.WriteLine($"{p} {linesStr} | {desc}");
        }
        return 0;
    }

    private static void ScanDirectory(string dir, string baseDir, List<(string RelPath, string Description, int LineCount)> results, GitIgnoreFilter? gitIgnore = null)
    {
        gitIgnore ??= GitIgnoreFilter.Load(baseDir);

        var di = new DirectoryInfo(dir);
        foreach (var subDir in di.GetDirectories())
        {
            string relDir = Path.GetRelativePath(baseDir, subDir.FullName).Replace('\\', '/');
            if (gitIgnore.IsIgnored(relDir, isDirectory: true)) continue;
            ScanDirectory(subDir.FullName, baseDir, results, gitIgnore);
        }

        foreach (var file in di.GetFiles())
        {
            string relFile = Path.GetRelativePath(baseDir, file.FullName).Replace('\\', '/');
            if (gitIgnore.IsIgnored(relFile, isDirectory: false)) continue;

            if (file.Extension.Equals(".cs", StringComparison.OrdinalIgnoreCase) ||
                file.Extension.Equals(".js", StringComparison.OrdinalIgnoreCase))
            {
                string code = File.ReadAllText(file.FullName);
                string[] lines = code.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                string desc = ExtractHeaderDoc(lines, file.Extension);
                results.Add((relFile, desc, lines.Length));
            }
        }
    }

    private static string ExtractHeaderDoc(string[] lines, string extension)
    {
        for (int i = 0; i < Math.Min(lines.Length, 15); i++)
        {
            string line = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;

            if (line.StartsWith("///"))
            {
                string clean = line.TrimStart('/', ' ').Trim();
                if (clean.StartsWith("<summary>", StringComparison.OrdinalIgnoreCase))
                {
                    string inner = clean.Replace("<summary>", "", StringComparison.OrdinalIgnoreCase)
                                       .Replace("</summary>", "", StringComparison.OrdinalIgnoreCase).Trim();
                    if (!string.IsNullOrEmpty(inner)) return inner;
                    if (i + 1 < lines.Length)
                    {
                        return lines[i + 1].TrimStart('/', ' ').Replace("</summary>", "").Trim();
                    }
                }
                if (!string.IsNullOrEmpty(clean)) return clean;
            }
            else if (line.StartsWith("/*") || line.StartsWith("/**"))
            {
                string clean = Regex.Replace(line, @"^\/\*+\s*", "");
                clean = Regex.Replace(clean, @"\*+\/$", "").Trim();
                if (!string.IsNullOrEmpty(clean) && !clean.StartsWith('*')) return clean;
                if (i + 1 < lines.Length)
                {
                    string next = lines[i + 1].Trim().TrimStart('*', ' ').Trim();
                    if (!string.IsNullOrEmpty(next)) return next;
                }
            }
            else if (line.StartsWith("//"))
            {
                string clean = line.TrimStart('/', ' ').Trim();
                if (!string.IsNullOrEmpty(clean) && !clean.StartsWith('@')) return clean;
            }
            else if (line.StartsWith("namespace ") || line.StartsWith("public ") || line.StartsWith("class "))
            {
                return line;
            }
        }
        return "(no description header)";
    }

    #endregion
}

public class SessionTracker
{
    public Dictionary<string, List<LineRange>> Entries { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    private static string GetSessionFilePath()
    {
        string tmp = Path.Combine(Path.GetTempPath(), "EventHorizon_CodeNav_Session.json");
        return tmp;
    }

    public static SessionTracker Load()
    {
        string path = GetSessionFilePath();
        if (File.Exists(path))
        {
            try
            {
                string json = File.ReadAllText(path);
                var tracker = JsonSerializer.Deserialize<SessionTracker>(json);
                if (tracker != null) return tracker;
            }
            catch
            {
                // Fall back to clean instance
            }
        }
        return new SessionTracker();
    }

    public static void Reset()
    {
        string path = GetSessionFilePath();
        if (File.Exists(path))
        {
            try { File.Delete(path); } catch { }
        }
    }

    public void Save()
    {
        try
        {
            string path = GetSessionFilePath();
            string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch
        {
            // Ignore write errors
        }
    }

    public bool IsRangeFullyInspected(string relPath, int start, int end)
    {
        string key = relPath.Replace('\\', '/');
        if (!Entries.TryGetValue(key, out var ranges)) return false;

        return ranges.Any(r => r.Start <= start && r.End >= end);
    }

    public void MarkRangeInspected(string relPath, int start, int end)
    {
        string key = relPath.Replace('\\', '/');
        if (!Entries.TryGetValue(key, out var ranges))
        {
            ranges = new List<LineRange>();
            Entries[key] = ranges;
        }

        ranges.Add(new LineRange(start, end));

        // Merge overlapping or adjacent ranges
        var merged = new List<LineRange>();
        foreach (var r in ranges.OrderBy(r => r.Start))
        {
            if (merged.Count == 0)
            {
                merged.Add(r);
            }
            else
            {
                var last = merged[^1];
                if (r.Start <= last.End + 1)
                {
                    merged[^1] = new LineRange(last.Start, Math.Max(last.End, r.End));
                }
                else
                {
                    merged.Add(r);
                }
            }
        }
        Entries[key] = merged;
    }
}

public record LineRange(int Start, int End);

public class GitIgnoreFilter
{
    private readonly List<(Regex Pattern, bool IsNegation, bool DirectoryOnly)> _rules = new();

    public static GitIgnoreFilter Load(string startDir)
    {
        var filter = new GitIgnoreFilter();
        filter.AddRule(".git");
        filter.AddRule(".vs");
        filter.AddRule("bin/");
        filter.AddRule("obj/");
        filter.AddRule("node_modules/");
        filter.AddRule(".gemini");

        string current = startDir;
        while (!string.IsNullOrEmpty(current))
        {
            string gitIgnorePath = Path.Combine(current, ".gitignore");
            if (File.Exists(gitIgnorePath))
            {
                foreach (string rawLine in File.ReadAllLines(gitIgnorePath))
                {
                    filter.AddRule(rawLine);
                }
                break;
            }
            string? parent = Directory.GetParent(current)?.FullName;
            if (parent == current) break;
            current = parent ?? "";
        }

        return filter;
    }

    public void AddRule(string rawLine)
    {
        string line = rawLine.Trim();
        if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) return;

        bool isNegation = line.StartsWith('!');
        if (isNegation) line = line[1..].Trim();

        bool directoryOnly = line.EndsWith('/');
        if (directoryOnly) line = line[..^1].Trim();

        line = line.Replace('\\', '/');
        if (line.StartsWith('/')) line = line[1..];

        string regexPattern = "^" + Regex.Escape(line)
            .Replace(@"\*\*", ".*")
            .Replace(@"\*", @"[^/]*")
            .Replace(@"\?", @"[^/]") + (directoryOnly ? "(/.*)?$" : "(/.*)?$");

        if (!line.Contains('/'))
        {
            regexPattern = "(^|/)" + Regex.Escape(line)
                .Replace(@"\*", @"[^/]*")
                .Replace(@"\?", @"[^/]") + (directoryOnly ? "(/.*)?$" : "(/.*)?$");
        }

        try
        {
            _rules.Add((new Regex(regexPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled), isNegation, directoryOnly));
        }
        catch
        {
        }
    }

    public bool IsIgnored(string relativePath, bool isDirectory)
    {
        string norm = relativePath.Replace('\\', '/').Trim('/');
        bool ignored = false;

        foreach (var (pattern, isNegation, dirOnly) in _rules)
        {
            if (dirOnly && !isDirectory && !norm.Contains('/')) continue;

            if (pattern.IsMatch(norm))
            {
                ignored = !isNegation;
            }
        }
        return ignored;
    }
}

public record AstSymbolInfo(string Kind, string Name, (int StartLine, int EndLine) Span, string DocSummary)
{
    public int StartLine => Span.StartLine;
    public int EndLine => Span.EndLine;
}
