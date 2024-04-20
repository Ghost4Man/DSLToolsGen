using System.Reactive.Linq;
using Antlr4Ast;

namespace DSLToolsGenerator;

public readonly record struct GeneratorInputs(
    IObservable<Configuration?> Configuration,
    IObservable<Grammar?> Grammar);

public interface IGeneratorRunner
{
    IObservable<Func<Task>?> ObserveAndRegisterRunner(GeneratorInputs inputs);
}

public class TmLanguageGeneratorRunner(
    Func<Grammar, Configuration, Task> handler) : IGeneratorRunner
{
    public IObservable<Func<Task>?> ObserveAndRegisterRunner(GeneratorInputs inputs)
        => Observable.CombineLatest(inputs.Grammar, inputs.Configuration,
            (g, c) => g is null || c is null ? null : (Func<Task>)(() => Run(g, c)));

    public async Task Run(Grammar grammar, Configuration config)
    {
        Console.Error.WriteLine("Generating TextMate grammar...");
        await handler(grammar, config);
    }
}

public class AstCodeGeneratorRunner(
    Func<Grammar, Configuration, Task> handler) : IGeneratorRunner
{
    public IObservable<Func<Task>?> ObserveAndRegisterRunner(GeneratorInputs inputs)
        => Observable.CombineLatest(inputs.Grammar, inputs.Configuration,
            (g, c) => g is null || c is null ? null : (Func<Task>)(() => Run(g, c)));

    public async Task Run(Grammar grammar, Configuration config)
    {
        Console.WriteLine("Generating AST...");
        await handler(grammar, config);
    }
}

public class VscodeExtensionGeneratorRunner(
    Func<Configuration, Task> handler) : IGeneratorRunner
{
    public IObservable<Func<Task>?> ObserveAndRegisterRunner(GeneratorInputs inputs)
        => inputs.Configuration
            .Select(c => (Func<Task>)(() => Run(c)), orIfNull: null);

    public async Task Run(Configuration config)
    {
        Console.WriteLine("Generating VSCode extension...");
        await handler(config);
    }
}
