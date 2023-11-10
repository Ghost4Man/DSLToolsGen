using System.CommandLine;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

using Antlr4Ast;

using DSLToolsGenerator;

var grammarArg = new Argument<FileInfo>("grammar-file",
    "input ANTLR4 grammar file path (.g4)");

var outputArg = new Option<FileInfo?>(["-o", "--output"],
    "name of the file to write the generated code to");

var watchOption = new Option<bool>("--watch",
    "watch the input grammar for changes and regenerate when changed");

var generateAstCommand = new Command("ast",
    "generates C# code of a syntax tree data structure") { grammarArg, outputArg, watchOption };
generateAstCommand.SetHandler(async (gf, of, w) => {
        await WithWatchMode(w, gf, () => GenerateAstCodeFromGrammarFile(gf, of));
    },
    grammarArg, outputArg, watchOption);

var generateTextMateGrammarCommand = new Command("tmLanguage",
    "generates a TextMate grammar for syntax highlighting") { grammarArg, outputArg, watchOption };
generateTextMateGrammarCommand.SetHandler(async (gf, of, w) => {
        await WithWatchMode(w, gf, () => GenerateTextMateGrammar(gf, of));
    },
    grammarArg, outputArg, watchOption);

var generateCommand = new Command("generate",
    "generates a (part of a) tool for the language described by an ANTLR4 grammar") {
        generateAstCommand,
        generateTextMateGrammarCommand
    };
var rootCommand = new RootCommand("DSL Tools Generator") { generateCommand };
return await rootCommand.InvokeAsync(args);

Task<int> GenerateAstCodeFromGrammarFile(FileInfo grammarFile, FileInfo? outputFile)
{
    if (!TryParseGrammarAndReportErrors(grammarFile, out Grammar? grammar, GrammarKind.Parser))
        return ExitCode(1);

    var grammarOptions = GetGrammarOptionsAsDictionary(grammar);
    if (grammarOptions.GetValueOrDefault("tokenVocab") is string tokenVocabValue)
    {
        var lexerGrammarFile = new FileInfo(
            Path.Combine(grammarFile.DirectoryName ?? ".", tokenVocabValue + ".g4"));
        if (!TryParseGrammarAndReportErrors(lexerGrammarFile, out Grammar? lexerGrammar, GrammarKind.Lexer))
            return ExitCode(1);

        grammar.MergeFrom(lexerGrammar);
        grammar.Kind = GrammarKind.Full;
    }

    var generator = new AstCodeGenerator(grammar);
    if (outputFile != null)
    {
        using var sw = new StreamWriter(outputFile.FullName);
        generator.GenerateFullAstCode(sw);
    }
    else
        generator.GenerateFullAstCode(Console.Out);
    return ExitCode(0);
}

bool TryParseGrammarAndReportErrors(FileInfo grammarFile,
    [NotNullWhen(returnValue: true)] out Grammar? grammar,
    GrammarKind? expectedGrammarKind = null)
{
    try
    {
        grammar = Grammar.ParseFile(grammarFile.FullName);
    }
    catch (IOException ex)
    {
        Console.Error.WriteLine($"Error while opening grammar file ({ex.GetType().Name}): {ex.Message}");
        grammar = null;
        return false;
    }

    if (expectedGrammarKind.HasValue && grammar.Kind != expectedGrammarKind)
    {
        Console.Error.WriteLine(
            $"Error: {grammarFile.Name} is a {grammar.Kind} grammar, " +
            $"expected a {expectedGrammarKind} grammar");
        return false;
    }

    if (grammar.ErrorMessages.Count is > 0 and int errorCount)
    {
        Console.Error.WriteLine($"Found {errorCount} errors while parsing {grammarFile.Name}:");
        foreach (var msg in grammar.ErrorMessages)
        {
            Console.Error.WriteLine(msg);
            return false;
        }
    }
    return true;
}

Dictionary<string, object?> GetGrammarOptionsAsDictionary(Grammar grammar)
    => grammar.Options.SelectMany(o => o.Items)
        .ToDictionary(o => o.Name, o => o.Value);

async Task<int> GenerateTextMateGrammar(FileInfo grammarFile, FileInfo? outputFile)
{
    string antlrHelperJarFullPath = Path.Combine(GetExeDirectory(), "antlrhelper.jar");
    var process = Process.Start(new ProcessStartInfo(
        "cmd", ["/C", "java", "-jar", antlrHelperJarFullPath, grammarFile.FullName,
            (outputFile != null ? ">" + outputFile.FullName : "")
            // TODO: "--output", outputFile.FullName
        ]));
    await process.WaitForExitAsync();
    return process.ExitCode;
}

async Task<TResult> WithWatchMode<TResult>(bool watchModeEnabled, FileInfo watchedFile, Func<Task<TResult>> action)
{
    if (!watchModeEnabled)
        return await action();

    FileSystemWatcher watcher = new(watchedFile.DirectoryName ?? ".", watchedFile.Name);
    TResult result;
    do
    {
        result = await action();
        if (watcher.WaitForChanged(WatcherChangeTypes.All).ChangeType is not WatcherChangeTypes.Changed)
            break;
        await Task.Delay(200);
    }
    // TODO: this call is actually synchronous?
    while (true);
    return result;
}

Task<int> ExitCode(int code) => Task.FromResult(code);

// Gets the path of the directory that contains this program
string GetExeDirectory() => Path.GetDirectoryName(typeof(Program).Assembly.Location)!;
