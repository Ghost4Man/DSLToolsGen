using System.Reactive.Linq;

using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;
using Antlr4Ast;

using DSLToolsGenerator;

namespace DSLToolsGenerator;

public class GeneratorPipeline(GeneratorPipelineInputs inputs,
    AstCodeGeneratorRunner? astCodeGeneratorRunner,
    LanguageServerGeneratorRunner? languageServerGeneratorRunner,
    TmLanguageGeneratorRunner? tmLanguageGeneratorRunner,
    VscodeExtensionGeneratorRunner? vscodeExtensionGeneratorRunner,
    ParserGeneratorRunner? parserGeneratorRunner)
{
    readonly EqualityComparer<IReadOnlyList<IGeneratorRunner>> collectionEqualityComparer =
        EqualityComparer<IReadOnlyList<IGeneratorRunner>>.Create(
            equals: (a, b) => a != null && b != null && a.SequenceEqual(b));

    public Task RunEnabledGenerators()
        => RunGenerators(c => GetGenerators(c.Outputs).ToList());

    public Task RunGenerators(OutputSet outputSet)
        => RunGenerators(_ => GetGenerators(outputSet).ToList());

    // runs until the Observables complete
    async Task RunGenerators(Func<Configuration, IReadOnlyList<IGeneratorRunner>> getGenerators)
    {
        // `Replay(1)` makes a connectable (shared) observable
        // (meaning that any processing work is not duplicated for all subscribers)
        // and ensures that new subscribers always receive the last value

        var configuration = inputs.Configuration
            .Log("Configuration")
            .Replay(1).AutoConnect();

        var grammar = inputs.Grammar
            .Log("Grammar")
            .Replay(1).AutoConnect();

        var generators = configuration
            .Select(getGenerators, orIfNull: [])
            .DistinctUntilChanged(collectionEqualityComparer)
            .Log("Generator list")
            .Replay(1).AutoConnect();

        var generatorRun = generators
            .Select(observeInputAndRunGenerators)
            .Switch()
            .WhereNotNull()
            .Select(Observable.FromAsync)
            .Concat()
            .Replay(1).AutoConnect();

        // Run the generators whose inputs have been updated
        generatorRun.Subscribe();

        // Wait until the "last" generator run
        // (i.e. the first and only one if watch mode is disabled,
        // or the "last" of the infinitely many runs if it's enabled)
        await generatorRun.LastOrDefaultAsync();

        IObservable<Func<Task>?> observeInputAndRunGenerators(IReadOnlyList<IGeneratorRunner> generators)
            => generators
                .Select(observeInputAndRunGenerator)
                .Merge();

        IObservable<Func<Task>?> observeInputAndRunGenerator(IGeneratorRunner generator)
            => generator.ObserveAndRegisterRunner(
                inputs: new(configuration, grammar));
    }

    IEnumerable<IGeneratorRunner> GetGenerators(OutputSet outputSet)
    {
        if (astCodeGeneratorRunner is null
            || languageServerGeneratorRunner is null
            || tmLanguageGeneratorRunner is null
            || vscodeExtensionGeneratorRunner is null
            || parserGeneratorRunner is null)
            throw new InvalidOperationException("Missing Generator Runners!");

        return getGenerators();

        IEnumerable<IGeneratorRunner> getGenerators()
        {
            if (outputSet.AST)
                yield return astCodeGeneratorRunner;
            if (outputSet.LanguageServer)
                yield return languageServerGeneratorRunner;
            if (outputSet.TmLanguageJson)
                yield return tmLanguageGeneratorRunner;
            if (outputSet.VscodeExtension)
                yield return vscodeExtensionGeneratorRunner;
            if (outputSet.Parser)
                yield return parserGeneratorRunner;
        }
    }
}

public readonly record struct GeneratorPipelineInputs(
    // these IObservables emit an event each time that input changes
    IObservable<Configuration?> Configuration,
    IObservable<Grammar?> Grammar)
{
    public static GeneratorPipelineInputs FromConfigFile(string configFileName, bool watchForChanges,
        Func<IFileInfo, Configuration?> configurationLoader,
        Func<IFileInfo, Grammar?> grammarLoader)
    {
        var configuration = ObserveFile(new FileInfo(configFileName), watchForChanges)
            .Log("Configuration file")
            .Select(configurationLoader)
            .Replay(1).AutoConnect();

        var grammarFileInfo = configuration
            .Do(c => {
                if (c is { GrammarFile: null })
                    Console.Error.WriteLine("Warning: No grammar file configured");
            })
            .Select(c => c?.GrammarFile)
            .DistinctUntilChanged()
            .Select(f => new FileInfo(f), orIfNull: null)
            .Log("Grammar file");

        var grammar = grammarFileInfo
            .Select(gfi => observeGrammar(gfi, watchForChanges),
                orIfNull: Observable.Return((Grammar?)null))
            .Switch()
            .Replay(1).AutoConnect();

        return new GeneratorPipelineInputs(configuration, grammar);

        IObservable<Grammar?> observeGrammar(FileInfo grammarFile, bool watchForChanges)
            => ObserveFile(grammarFile, watchForChanges)
                .Select(grammarLoader);
    }

    public static IObservable<IFileInfo> ObserveFile(FileInfo file, bool watchForChanges)
    {
        if (file.DirectoryName is not string directory)
            return Observable.Empty<IFileInfo>();

        var fileProvider = new PhysicalFileProvider(directory);
        var fileInfo = fileProvider.GetFileInfo(file.Name);

        if (watchForChanges)
        {
            return Observable.Create<IFileInfo>(
                observer => ChangeToken.OnChange(
                    () => fileProvider.Watch(file.Name),
                    static state => state.observer.OnNext(state.fileInfo),
                    state: (observer, fileInfo)
                ))
                .Throttle(TimeSpan.FromMilliseconds(100)) // debounce
                .StartWith(fileInfo);
        }
        else
        {
            return Observable.Return(fileInfo);
        }
    }
}
