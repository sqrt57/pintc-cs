using System;
using System.IO;
using Pintc;

var args_ = args; // top-level args

if (args_.Length == 0)
{
    Console.Error.WriteLine("pintc: no input files");
    return 1;
}

var inputFiles = new List<string>();
string? outputFile = null;
bool isDll = false;

for (int i = 0; i < args_.Length; i++)
{
    switch (args_[i])
    {
        case "--dll":
            isDll = true;
            break;
        case "-o" when i + 1 < args_.Length:
            outputFile = args_[++i];
            break;
        case { } a when a.StartsWith('-'):
            Console.Error.WriteLine($"pintc: unknown flag '{a}'");
            return 1;
        default:
            inputFiles.Add(args_[i]);
            break;
    }
}

if (inputFiles.Count == 0)
{
    Console.Error.WriteLine("pintc: no input file");
    return 1;
}

foreach (var f in inputFiles)
{
    if (!File.Exists(f))
    {
        Console.Error.WriteLine($"pintc: file not found: {f}");
        return 1;
    }
}

outputFile ??= Path.ChangeExtension(inputFiles[0], isDll ? ".dll" : ".exe");

// Parse all source files; accumulate all modules across files
var allModules = new List<ModuleDecl>();
foreach (var inputFile in inputFiles)
{
    var source = File.ReadAllText(inputFile);

    var lexer = new Lexer(source);
    var tokens = lexer.Tokenize();
    if (lexer.Diagnostics.Count > 0)
    {
        foreach (var d in lexer.Diagnostics)
            Console.Error.WriteLine($"{inputFile}: error: {d.Message}");
        return 1;
    }

    var parser = new Parser(tokens);
    var modules = parser.ParseProgram();
    if (parser.Diagnostics.Count > 0)
    {
        foreach (var d in parser.Diagnostics)
            Console.Error.WriteLine($"{inputFile}: error: {d.Message}");
        return 1;
    }
    allModules.AddRange(modules);
}

var resolveResult = Resolver.Resolve(allModules);
if (resolveResult.Diagnostics.Count > 0)
{
    foreach (var d in resolveResult.Diagnostics)
        Console.Error.WriteLine($"{inputFiles[0]}: error: {d.Message}");
    return 1;
}

var typeErrors = TypeChecker.Check(allModules, resolveResult);
if (typeErrors.Count > 0)
{
    foreach (var d in typeErrors)
        Console.Error.WriteLine($"{inputFiles[0]}: error: {d.Message}");
    return 1;
}

var unit = Codegen.Emit(allModules, isDll);
using var output = File.Create(outputFile);
if (isDll)
    PeWriter.WriteDll(unit, Path.GetFileName(outputFile), output);
else
    PeWriter.Write(unit, output);
return 0;
