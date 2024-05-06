using System.CommandLine;
using System.CommandLine.Parsing;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.FileProviders.Physical;
using Microsoft.Extensions.FileProviders;
using NJsonSchema;
using NJsonSchema.Generation;
using Humanizer;
using Antlr4Ast;

using DSLToolsGenerator;
using DSLToolsGenerator.AST;
using DSLToolsGenerator.SyntaxHighlighting;
using DSLToolsGenerator.EditorExtensions;
using DSLToolsGenerator.LSP;

[assembly: InternalsVisibleTo("DSLToolsGenerator.Tests")]

const string DefaultConfigFileName = "dtg.json";

//var grammarArg = new Argument<FileInfo>("grammar-file",
//    "input ANTLR4 grammar file path (.g4)");

var outputArg = new Option<FileInfo?>(["-o", "--output"],
    "path of the file to write the generated code to");

var watchOption = new Option<bool>("--watch",
    "watch the inputs for changes and regenerate when changed");

//var syntaxHighlightingVerboseOption = new Option<bool>("--verbose",
//    "print more details about how each rule was translated into a TM pattern/regex");

var generateAstCommand = new Command("ast",
    "generates C# code of a syntax tree data structure")
    { watchOption };
generateAstCommand.SetHandler(w =>
    InitializePipeline(watchForChanges: w)
        .RunGenerators(new OutputSet { AST = true }),
    watchOption);

var generateLanguageServerCommand = new Command("languageServer",
    "generates C# code of a (LSP) language server")
    { watchOption };
generateLanguageServerCommand.SetHandler(w =>
    InitializePipeline(watchForChanges: w)
        .RunGenerators(new OutputSet { LanguageServer = true }),
    watchOption);

var generateTextMateGrammarCommand = new Command("tmLanguage",
    "generates a TextMate grammar for syntax highlighting")
    { watchOption };
generateTextMateGrammarCommand.SetHandler(w =>
    InitializePipeline(watchForChanges: w)
        .RunGenerators(new OutputSet { TmLanguageJson = true }),
    watchOption);

var generateVscodeExtensionCommand = new Command("vscodeExtension",
    "generates a VSCode extension")
    { watchOption };
generateVscodeExtensionCommand.SetHandler(w =>
    InitializePipeline(watchForChanges: w)
        .RunGenerators(new OutputSet { VscodeExtension = true }),
    watchOption);

var generateConfigSchemaCommand = new Command("dtgConfigSchema",
    "generates a JSON schema of DTG configuration")
    { outputArg };
generateConfigSchemaCommand.SetHandler(GenerateConfigSchema, outputArg);

var generateCommand = new Command("generate",
    "runs all configured generators") {
        generateAstCommand,
        generateLanguageServerCommand,
        generateTextMateGrammarCommand,
        generateVscodeExtensionCommand,
        generateConfigSchemaCommand,
    };
generateCommand.SetHandler(() =>
    InitializePipeline(watchForChanges: false).RunEnabledGenerators());

var watchCommand = new Command("watch",
    "runs all configured generators and reruns them when their inputs are modified");
watchCommand.SetHandler(() =>
    InitializePipeline(watchForChanges: true).RunEnabledGenerators());

var rootCommand = new RootCommand("DSL Tools Generator") {
    generateCommand,
    watchCommand,
};

return await rootCommand.InvokeAsync(args);

GeneratorPipeline InitializePipeline(bool watchForChanges)
{
    return new(InitializePipelineInputs(watchForChanges),
        new AstCodeGeneratorRunner(async (g, c) => {
            if (checkConfigValueIsPresent(c.Ast.OutputPath, out var outputPath))
                await GenerateAstCodeFromGrammarFile(g, c, new FileInfo(outputPath));
        }),
        new LanguageServerGeneratorRunner(async (g, c) => {
            if (checkConfigValueIsPresent(c.LanguageServer.OutputPath, out var outputPath))
                await GenerateLanguageServer(g, c, new FileInfo(outputPath));
        }),
        new TmLanguageGeneratorRunner(async (g, c) => {
            if (checkConfigValueIsPresent(c.SyntaxHighlighting.OutputPath, out var outputPath))
                await GenerateTextMateGrammar(g, c, new FileInfo(outputPath), verbose: false);
        }),
        new VscodeExtensionGeneratorRunner(async (g, c) => {
            if (checkConfigValueIsPresent(c.VscodeExtension, out _))
                await GenerateVscodeExtension(g, c);
        }),
        new ParserGeneratorRunner(async (g, c) => {
            if (checkConfigValueIsPresent(c.Parser.OutputDirectory, out var outputPath)
                && checkConfigValueIsPresent(c.Parser.AntlrCommand, out var command))
                await RunAntlr(g, c, command, outputPath);
        }));

    bool checkConfigValueIsPresent<T>(T value, [NotNullWhen(true)] out T? valueIfPresent,
        [CallerArgumentExpression(nameof(value))] string configValueName = null!)
    {
        return Configuration.CheckValuePresent(value, out valueIfPresent,
            Console.Error.WriteLine, x => x is not (null or ""), configValueName);
    }
}

GeneratorPipelineInputs InitializePipelineInputs(bool watchForChanges)
    => GeneratorPipelineInputs.FromConfigFile(DefaultConfigFileName, watchForChanges,
        f => LoadConfiguration(f, warnIfNotFound: true),
        f => TryParseGrammarAndReportErrors(f, out var grammar) ? grammar : null);

async Task<int> GenerateAstCodeFromGrammarFile(
    Grammar grammar, Configuration config, FileInfo? outputFile)
{
    var generator = AstCodeGenerator.FromConfig(config, grammar,
        diagnosticHandler: Console.Error.WriteLine);

    var model = generator.GenerateAstCodeModel();

    FileStream? fileStream = null;
    if (outputFile != null && !TryOpenWrite(outputFile, out fileStream))
        return 1;

    await using var writer = CreateOutputWriter(fileStream);

    var modelWriter = CSharpModelWriter.FromConfig(config, writer);
    modelWriter.Visit(model);

    return 0;
}

async Task<int> GenerateLanguageServer(
    Grammar grammar, Configuration config, FileInfo? outputFile)
{
    FileStream? fileStream = null;
    if (outputFile != null && !TryOpenWrite(outputFile, out fileStream))
        return 1;

    await using var writer = CreateOutputWriter(fileStream);

    var generator = LanguageServerGenerator.FromConfig(config, grammar,
        writer, diagnosticHandler: Console.Error.WriteLine);

    generator.GenerateLanguageServer();

    return 0;
}

async Task<int> GenerateTextMateGrammar(
    Grammar grammar, Configuration config, FileInfo? outputFile, bool verbose)
{
    FileStream? fileStream = null;
    if (outputFile != null && !TryOpenWrite(outputFile, out fileStream))
        return 1;

    await using var _ = fileStream;
    Stream outputStream = fileStream ?? Console.OpenStandardOutput();

    var generator = TmLanguageGenerator.FromConfig(config, grammar,
        diagnosticHandler: Console.Error.WriteLine);

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
                    Console.Error.WriteLine($"     regex: {YELLOW}{generator.MakeRegex(alt, [rule])}{RESET}");
                }
            }
            Console.Error.WriteLine();
        }
    }

    await generator.GenerateTextMateLanguageJsonAsync(outputStream);
    return 0;
}

Task<int> GenerateVscodeExtension(Grammar grammar, Configuration config)
{
    var generator = VscodeExtensionGenerator.FromConfig(grammar,
        diagnosticHandler: Console.Error.WriteLine,
        config);

    if (generator is null)
        return Task.FromResult(1); // we assume an error has been reported by FromConfig

    generator.GenerateExtension(file => {
        if (TryOpenWrite(file, out FileStream? fileStream))
            return new IndentedTextWriter(CreateOutputWriter(fileStream));
        return null;
    });

    return Task.FromResult(0);
}

async Task<int> RunAntlr(Grammar grammar, Configuration config, string command, string outputDirectory)
{
    // split to (for example): ["java", "-jar", "antlr.jar"]
    var commandParts = new Parser().Parse(command).Tokens.Select(t => t.Value).ToList();
    //string[] commandParts = command.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
    string?[] grammarPaths = [
        grammar.GetLexerGrammarFile()?.FullName,
        config.GrammarFile! // we know grammar is not null, so GrammarFile must be non-null too
    ];
    string[] args = [..commandParts[1..], ..grammarPaths.WhereNotNull(),
        "-visitor", "-listener",
        "-o", outputDirectory,
        "-Dlanguage=CSharp",
        ..(config.Parser.Namespace is { } ns ? ["-package", ns] : Array.Empty<string>()),
    ];
    await Console.Error.WriteLineAsync(AnsiColors.Gray +
        $"Launching process `{commandParts[0]}`" +
        $" with arguments [{args.Select(a => $"`{a}`").MakeString(", ")}]"
        + AnsiColors.Default);
    try
    {
        //ProcessStartInfo startInfo = new(commandParts[0], commandParts.ElementAtOrDefault(1) ?? "");

        //foreach (string arg in args)
        //    startInfo.ArgumentList.Add(arg);
        //var process = Process.Start(commandParts[0], args);
        var process = Process.Start(commandParts[0], args);
        await process.WaitForExitAsync();
        return process.ExitCode;
    }
    catch (Exception ex)
    {
        await Console.Error.WriteLineAsync("Error while starting ANTLR: " +
            $"{ex.GetType().Name}: {ex.Message}");
        return 1;
    }
}

bool TryParseGrammarAndReportErrors(IFileInfo grammarFile,
    [NotNullWhen(returnValue: true)] out Grammar? grammar,
    GrammarKind? expectedGrammarKind = null)
{
    try
    {
        using StreamReader streamReader = new(grammarFile.CreateReadStream());
        grammar = Grammar.Parse(streamReader, grammarFile.Name);
        grammar.Analyze();
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

    // Merge associated lexer grammar
    if (grammar.Options.Find("tokenVocab")?.Value is string tokenVocabValue)
    {
        var fileInfo = new FileInfo(
            Path.Combine(grammarFile.GetDirectoryName() ?? ".", tokenVocabValue + ".g4"));
        var lexerGrammarFile = new PhysicalFileInfo(fileInfo);
        if (!TryParseGrammarAndReportErrors(lexerGrammarFile, out Grammar? lexerGrammar, GrammarKind.Lexer))
            return false;

        grammar.SetLexerGrammarFile(fileInfo);
        grammar.MergeFrom(lexerGrammar);
        grammar.Kind = GrammarKind.Full;
    }

    return true;
}

bool TryOpenWrite(FileInfo file, [NotNullWhen(true)] out FileStream? stream,
    bool createDirectory = true)
{
    try
    {
        if (createDirectory)
            file.Directory?.Create();
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

StreamWriter CreateOutputWriter(Stream? stream) => stream is null
    ? new StreamWriter(Console.OpenStandardOutput(), leaveOpen: true)
    : new StreamWriter(stream);

async Task<int> GenerateConfigSchema(FileInfo? outputFile)
{
    var settings = new SystemTextJsonSchemaGeneratorSettings {
        SchemaProcessors = {
            new MarkdownDescriptionSchemaProcessor(),
            new DefaultSnippetsSchemaProcessor(),
        }
    };
    var generator = new JsonSchemaGenerator(settings);
    var schema = generator.Generate(typeof(Configuration));
    schema.Properties.Add("$schema", new() { Type = JsonObjectType.String });
    schema.ExtensionData ??= new Dictionary<string, object?>();
    schema.ExtensionData["allowTrailingCommas"] = true;
    schema.ExtensionData["allowComments"] = true;

    FileStream? fileStream = null;
    if (outputFile != null && !TryOpenWrite(outputFile, out fileStream))
        return 1;

    await using var writer = CreateOutputWriter(fileStream);
    writer.WriteLine(schema.ToJson());
    return 0;
}

static Configuration? LoadConfiguration(IFileInfo file, bool warnIfNotFound = false)
{
    JsonSerializerOptions? configDeserializationOptions = new() {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        // disallow unknown keys by default, but Skip on the top level (Configuration)
        // to allow keys like "$schema" etc.
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    Configuration? config = null;
    try
    {
        using Stream stream = file.CreateReadStream();
        config = JsonSerializer.Deserialize<Configuration>(stream, configDeserializationOptions);
    }
    catch (FileNotFoundException)
    {
        if (warnIfNotFound)
            Console.Error.WriteLine($"Warning: No configuration file ({DefaultConfigFileName}) found.");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error while reading config file: {ex.GetType().Name}: {ex.Message}");
    }

    return config;
}

static class AnsiColors
{
    public const string Gray = "\u001b[90m";
    public const string Default = "\u001b[39m";
}
