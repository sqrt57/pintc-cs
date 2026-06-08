using System;
using System.IO;

var args_ = args; // top-level args

if (args_.Length == 0)
{
    Console.Error.WriteLine("pintc: no input files");
    return 1;
}

string? inputFile = null;
string? outputFile = null;

for (int i = 0; i < args_.Length; i++)
{
    switch (args_[i])
    {
        case "-o" when i + 1 < args_.Length:
            outputFile = args_[++i];
            break;
        case { } a when a.StartsWith('-'):
            Console.Error.WriteLine($"pintc: unknown flag '{a}'");
            return 1;
        default:
            if (inputFile is not null)
            {
                Console.Error.WriteLine("pintc: multiple input files not yet supported");
                return 1;
            }
            inputFile = args_[i];
            break;
    }
}

if (inputFile is null)
{
    Console.Error.WriteLine("pintc: no input file");
    return 1;
}

if (!File.Exists(inputFile))
{
    Console.Error.WriteLine($"pintc: file not found: {inputFile}");
    return 1;
}

outputFile ??= Path.ChangeExtension(inputFile, ".exe");

Console.Error.WriteLine($"pintc: not implemented (would compile '{inputFile}' -> '{outputFile}')");
return 1;
