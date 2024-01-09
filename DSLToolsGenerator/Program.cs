using System.CommandLine;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

using Humanizer;
using Antlr4Ast;

using DSLToolsGenerator.AST;
using DSLToolsGenerator.SyntaxHighlighting;
using DSLToolsGenerator.SyntaxHighlighting.Models;

[assembly: InternalsVisibleTo("DSLToolsGenerator.Tests")]

const string DefaultConfigFileName = "dtg.json";

var grammarArg = new Argument<FileInfo>("grammar-file",
    "input ANTLR4 grammar file path (.g4)");

var outputArg = new Option<FileInfo?>(["-o", "--output"],
    "name of the file to write the generated code to");

var watchOption = new Option<bool>("--watch",
    "watch the input grammar for changes and regenerate when changed");

var syntaxHighlightingVerboseOption = new Option<bool>("--verbose",
    "print more details about how each rule was translated into a TM pattern/regex");

var generateAstCommand = new Command("ast",
    "generates C# code of a syntax tree data structure")
    { grammarArg, outputArg, watchOption };
generateAstCommand.SetHandler((gf, of, w) =>
    WithWatchMode(w, gf, () => GenerateAstCodeFromGrammarFile(gf, of)),
    grammarArg, outputArg, watchOption);

var generateTextMateGrammarCommand = new Command("tmLanguage",
    "generates a TextMate grammar for syntax highlighting")
    { grammarArg, outputArg, watchOption, syntaxHighlightingVerboseOption };
generateTextMateGrammarCommand.SetHandler((gf, of, w, v) =>
    WithWatchMode(w, gf, () => GenerateTextMateGrammar(gf, of, v)),
    grammarArg, outputArg, watchOption, syntaxHighlightingVerboseOption);

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

    var generator = new AstCodeGenerator(grammar, diag => {
        Console.Error.WriteLine(diag.ToString());
    });
    var model = generator.GenerateAstCodeModel();
    if (outputFile != null)
    {
        if (!TryOpenWrite(outputFile, out Stream? stream))
            return ExitCode(1);

        using var sw = new StreamWriter(stream);
        var modelWriter = new CSharpModelWriter(sw);
        modelWriter.Visit(model);
    }
    else
    {
        var modelWriter = new CSharpModelWriter(Console.Out);
        modelWriter.Visit(model);
    }
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

    if (grammar.ErrorMessages.Count is > 0 and int errorCount)
    {
        Console.Error.WriteLine($"Found {"error".ToQuantity(errorCount)} while parsing {grammarFile.Name}:");
        foreach (var msg in grammar.ErrorMessages)
        {
            Console.Error.WriteLine(msg);
            return false;
        }
    }

    if (expectedGrammarKind.HasValue && grammar.Kind != expectedGrammarKind)
    {
        Console.Error.WriteLine(
            $"Error: {grammarFile.Name} is a {grammar.Kind} grammar, " +
            $"expected a {expectedGrammarKind} grammar");
        return false;
    }

    return true;
}

Dictionary<string, object?> GetGrammarOptionsAsDictionary(Grammar grammar)
    => grammar.Options.SelectMany(o => o.Items)
        .ToDictionary(o => o.Name, o => o.Value);

async Task<int> GenerateTextMateGrammar(FileInfo grammarFile, FileInfo? outputFile, bool verbose)
{
    if (!TryParseGrammarAndReportErrors(grammarFile, out Grammar? grammar, GrammarKind.Lexer))
        return 1;

    Stream? fileStream = null;
    if (outputFile != null && !TryOpenWrite(outputFile, out fileStream))
        return 1;

    await using (fileStream)
    {
        await ConvertGrammarToTextMateLanguage(grammar, verbose)(
            fileStream ?? Console.OpenStandardOutput());
    }
    return 0;
}

bool TryOpenWrite(FileInfo file, [NotNullWhen(true)] out Stream? stream)
{
    try
    {
        stream = file.Create(); // create or replace if it exists
        return true;
    }
    catch (IOException ex)
    {
        Console.Error.WriteLine($"Error while opening file '{file.FullName}' for writing ({ex.GetType().Name}): {ex.Message}");
        stream = null;
        return false;
    }
}

Func<Stream, Task> ConvertGrammarToTextMateLanguage(Grammar grammar, bool verbose)
{
    if (grammar.LexerRules.Count == 0)
    {
        Console.Error.WriteLine("Warning: no lexer rules found");
    }

	Configuration? config = LoadConfiguration();

	var generator = new TmLanguageGenerator(grammar,
        diagnosticHandler: Console.Error.WriteLine,
        config?.SyntaxHighlighting ?? new());

    if (verbose)
    {
        const string GREEN = "\u001b[32m";
        const string YELLOW = "\u001b[33m";
        const string RESET = "\u001b[0m";

        foreach (Rule rule in grammar.LexerRules)
        {
            Console.Error.WriteLine($"rule {rule.Name}:");

            if (!generator.ShouldHighlight(rule))
                Console.Error.WriteLine("  (skipped)");
            else
            {
                for (int i = 0; i < rule.AlternativeList.Items.Count; i++)
                {
                    Alternative? alt = rule.AlternativeList.Items[i];
                    Console.Error.WriteLine($"  {i}. {GREEN}{alt}{RESET}");
                    Console.Error.WriteLine($"     regex: {YELLOW}{generator.MakeRegex(alt)}{RESET}");
                }
            }
            Console.Error.WriteLine();
        }
    }

    return generator.GenerateTextMateLanguageJsonAsync;
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
        // TODO: this call is actually synchronous
        if (watcher.WaitForChanged(WatcherChangeTypes.All).ChangeType is not WatcherChangeTypes.Changed)
            break;
        await Task.Delay(200);
    }
    while (true);
    return result;
}

Task<int> ExitCode(int code) => Task.FromResult(code);

// Gets the path of the directory that contains this program
string GetExeDirectory() => Path.GetDirectoryName(typeof(Program).Assembly.Location)!;

static Configuration? LoadConfiguration()
{
	JsonSerializerOptions? configDeserializationOptions = new() {
		AllowTrailingCommas = true,
		ReadCommentHandling = JsonCommentHandling.Allow,
		UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
	};

	Configuration? config = null;
	try
    {
		using Stream stream = File.OpenRead(DefaultConfigFileName);
		config = JsonSerializer.Deserialize<Configuration>(stream, configDeserializationOptions);
    }
    catch (FileNotFoundException) { }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"error while reading config file: {ex.GetType().Name}: {ex.Message}");
    }

    return config;
}

record class Configuration(SyntaxHighlightingConfiguration SyntaxHighlighting);
