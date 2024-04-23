using Antlr4Ast;

namespace DSLToolsGenerator.LSP;

public class LanguageServerGenerator
{
    public required Grammar Grammar { get; init; }
    public required Action<Diagnostic> DiagnosticHandler { get; init; }
    public required DottedIdentifierString? Namespace { get; init; }
    public required IdentifierString LanguageServerClassName { get; init; }
    public required DottedIdentifierString? AntlrNamespace { get; init; }
    public required DottedIdentifierString? AstNamespace { get; init; }
    public required IdentifierString AstNodeBaseClassName { get; init; }
    public required IdentifierString AstRootNodeClassName { get; init; }
    public required IdentifierString ParserClassName { get; init; }
    public required HyphenDotIdentifierString LanguageId { get; init; }

    protected IndentedTextWriter Output { get; }

    public LanguageServerGenerator(TextWriter output)
        => Output = new IndentedTextWriter(output);

    public void GenerateLanguageServer()
    {
        const string LSP = "OmniSharp.Extensions.LanguageServer";

        Output.WriteCode($$"""
            #nullable enable

            namespace {{Namespace ?? "LanguageServer"}}
            {
                using Type = System.Type;
                using System.Collections.Concurrent;
                using System.Collections.Immutable;
                using System.Reflection;
                using Antlr4.Runtime;
                using Antlr4.Runtime.Tree;
                using MediatR;
                using OmniSharp.Extensions.JsonRpc;
                using LSP = OmniSharp.Extensions.LanguageServer;
                using {{LSP}}.Protocol;
                using {{LSP}}.Protocol.Models;
                using {{LSP}}.Protocol.Server;
                using {{LSP}}.Protocol.Document;
                using {{LSP}}.Protocol.Serialization;
                using AstNode = {{AstNamespace?.Value.Append(".")}}{{AstNodeBaseClassName}};
                using {{ParserClassName}} = {{AntlrNamespace?.Value.Append(".")}}{{ParserClassName}};

                partial class {{LanguageServerClassName}}
                {
                    public const string LanguageId = "{{LanguageId}}";
                }

                {{_ => GenerateLspConnectionInfoClass()}}

                {{_ => GenerateDocumentManagerClass()}}

                {{_ => GenerateHelperClasses()}}

                {{_ => GenerateExtensionMethods()}}

                {{_ => GenerateSemanticTokensBuilderReorderBufferClass()}}

                {{_ => GenerateDocumentClass()}}

                {{_ => GenerateAstRequestHandler()}}
            }
            """);
    }

    public void GenerateLspConnectionInfoClass() => Output.WriteCode($$"""
        public abstract partial record LspConnectionInfo
        {
            private LspConnectionInfo() { }

            public partial record StdIO : LspConnectionInfo;
            public partial record TcpServer(int port) : LspConnectionInfo;

            public static LspConnectionInfo FromCommandLineArgs(IEnumerable<string> args)
            {
                foreach (string arg in args)
                {
                    if (substringAfter(arg, "--tcpserver=") is string port)
                    {
                        if (int.TryParse(port, out int portNumber))
                            return new LspConnectionInfo.TcpServer(portNumber);

                        Console.Error.WriteLine("Error: Invalid argument to `--tcpserver` (expected port number)");
                        break;
                    }
                }
                Console.Error.WriteLine("Info: No command-line argument specifying LSP connection info given, assuming standard input/output");
                return new LspConnectionInfo.StdIO();

                string? substringAfter(string str, string prefix)
                    => str.StartsWith(prefix) ? str[prefix.Length..] : null;
            }
        }
        """);

    public void GenerateDocumentManagerClass() => Output.WriteCode($$"""
        public partial class DocumentManager
        {
            protected ConcurrentDictionary<DocumentUri, Document> Documents { get; } = new();

            public void Remove(DocumentUri uri) => Documents.Remove(uri, out _);
            public void AddOrUpdate(DocumentUri uri, Document document) => Documents[uri] = document;
            public Document? Get(DocumentUri uri) => Documents.GetValueOrDefault(uri);
        }
        """);

    public void GenerateHelperClasses() => Output.WriteCode($$"""
        public record struct SyntaxErrorInfo<TSymbol>(
            IRecognizer Recognizer, TSymbol? OffendingSymbol,
            int LineNumber, int CharPositionInLine, string Message, RecognitionException? Exception)
        {
            public readonly Range GetRange() => new(
                LineNumber - 1, CharPositionInLine,
                LineNumber - 1, CharPositionInLine + (OffendingSymbol as IToken)?.Text?.Length ?? 1);
        }

        public class DelegateErrorListener<TSymbol>(Action<SyntaxErrorInfo<TSymbol>> Handler)
            : IAntlrErrorListener<TSymbol>
        {
            public void SyntaxError(TextWriter output, IRecognizer recognizer, TSymbol offendingSymbol,
                int lineNumber, int charPositionInLine, string msg, RecognitionException? e)
            {
                Handler(new(recognizer, offendingSymbol, lineNumber, charPositionInLine, msg, e));
            }
        }
        """);

    public void GenerateExtensionMethods() => Output.WriteCode($$"""
        public static class ParseTreeLspExtensions
        {
            public static Range GetRange(this ParserRuleContext context)
                => context.SourceInterval.Length is 0 // if there are no tokens (or only missing tokens)
                    ? new Range(context.Start.GetStartPosition(), context.Start.GetStartPosition())
                    : new Range(context.Start.GetStartPosition(), context.Stop.GetEndPosition());

            public static Range GetRange(this IToken token)
                => new Range(token.GetStartPosition(), token.GetEndPosition());

            public static Range GetRange(this ITerminalNode terminal)
                => new Range(terminal.Symbol.GetStartPosition(), terminal.Symbol.GetEndPosition());

            public static Position GetStartPosition(this IToken token)
                => new Position(token.Line - 1, token.Column);

            public static Position GetEndPosition(this IToken token)
            {
                // known issue: can't get end position of multi-line tokens (comments, strings, etc.)
                return new Position(token.Line - 1, token.Column + token.Text.Length);
            }
        }

        public static class LspExtensions
        {
            public static StringOrMarkupContent AppendParagraph(this StringOrMarkupContent self, string markdown)
            {
                if (markdown is null or "")
                    return self;

                if (self.String is "" || self.MarkupContent?.Value is "")
                    return markdown;

                if (self.MarkupContent?.Kind is not MarkupKind.Markdown)
                    return (self.String ?? self.MarkupContent?.Value) + "\n\n" + markdown;

                return new(new MarkupContent {
                    Kind = MarkupKind.Markdown,
                    Value = self.MarkupContent.Value + "\n\n---\n\n" + markdown
                });
            }

            public static int? GetVersion(this TextDocumentIdentifier documentId)
                => (documentId as VersionedTextDocumentIdentifier)?.Version
                    ?? (documentId as OptionalVersionedTextDocumentIdentifier)?.Version;

            public static void PublishDiagnostics(this ILanguageServer mediator,
                    TextDocumentIdentifier documentId, IEnumerable<Diagnostic> diagnostics)
                => mediator.PublishDiagnostics(new() {
                    Uri = documentId.Uri,
                    Version = documentId.GetVersion(),
                    Diagnostics = new(diagnostics)
                });
        }

        public static class EnumerableExtensions
        {
            public static IEnumerable<T> WhereNotNull<T>(this IEnumerable<T?> items) where T : struct
                => from item in items
                   where item.HasValue
                   select item.Value;

            public static IEnumerable<T> WhereNotNull<T>(this IEnumerable<T?> items)
                => from item in items
                   where item is not null
                   select item;
        }
        """);

    public void GenerateSemanticTokensBuilderReorderBufferClass() => Output.WriteCode($$$"""
        /// <summary>
        /// A <see cref="LSP.Protocol.Document.SemanticTokensBuilder"/> wrapper
        /// that buffers and sorts tokens on each line so that the tokens
        /// don't have to be pushed in order.
        /// </summary>
        public partial class SemanticTokensBuilderReorderBuffer(LSP.Protocol.Document.SemanticTokensBuilder builder)
        {
            protected virtual List<(Range range, SemanticTokenType? type, SemanticTokenModifier[] modifiers)> LineTokens { get; } = new();

            /// <summary>
            /// If <paramref name="pushedToken"/> is on a new line, flushes previously buffered tokens.
            /// Adds <paramref name="pushedToken"/> into the current line's buffer.
            /// </summary>
            public void PushToken(Range? range, (SemanticTokenType? type, SemanticTokenModifier[] modifiers) pushedToken)
            {
                if (range is null)
                    return;

                if (LineTokens is [.., var last] && range.Start.Line > last.range.Start.Line) // new line
                {
                    Flush();
                }

                LineTokens.Add((range, pushedToken.type, pushedToken.modifiers));
            }

            /// <summary>
            /// Push buffered tokens to the underlying <see cref="LSP.Protocol.Document.SemanticTokensBuilder"/>
            /// ordered by position in the document.
            /// This method must be called (either explicitly or via <see cref="IDisposable.Dispose()"/>)
            /// to flush tokens from the last line.
            /// </summary>
            public void Flush()
            {
                LineTokens.Sort(static (a, b) => a.range.Start.CompareTo(b.range.Start));
                foreach (var token in LineTokens)
                {
                    builder.Push(token.range, token.type, token.modifiers);
                }
                LineTokens.Clear();
            }
        }
        """);

    public void GenerateDocumentClass() => Output.WriteCode($$"""
        public record Document(string Text, {{AstRootNodeClassName}} Ast, {{ParserClassName}} Parser)
        {
            /// <summary>
            /// AST nodes grouped by line index (0..n).
            /// </summary>
            public ILookup<int, AstNode> AstNodesByLine { get; }
                = Ast.GetAllDescendantNodes().ToLookup(n => (n.ParserContext?.Start.Line - 1) ?? -1);

            public AstNode FindDeepestNodeAt(Position position)
                => FindDeepestNodeAt(position, filter: _ => true) ?? Ast;

            public TNode? FindDeepestNodeAt<TNode>(Position position)
                where TNode : AstNode
                => (TNode?)FindDeepestNodeAt(position, n => n is TNode);

            public AstNode? FindDeepestNodeAt(Position position, Func<AstNode, bool> filter)
            {
                for (int line = position.Line; line > 0; line--)
                {
                    if (AstNodesByLine[line].LastOrDefault(n =>
                            filter(n) && (n.ParserContext?.GetRange().Contains(position) ?? false))
                        is AstNode foundNode)
                    {
                        return foundNode;
                    }
                }
                return filter(Ast) ? Ast : null;
            }

            public IEnumerable<AstNode> GetNodesBetweenLines(int startLine, int endLine) => AstNodesByLine
                .Where(l => startLine <= l.Key && l.Key <= endLine)
                .SelectMany(l => l);

            public IToken FindTokenAt(Position position, bool preferLeftTokenAtBoundary = false)
            {
                var tokens = (BufferedTokenStream)Parser.TokenStream;
                // assuming that there are no holes
                Func<IToken, bool> predicate = preferLeftTokenAtBoundary
                    ? (t => t.GetEndPosition() >= position)
                    : (t => t.GetEndPosition() > position);
                return tokens.GetTokens().First(predicate);
            }
        }
        """);

    public void GenerateAstRequestHandler() => Output.WriteCode($$"""
        [Method("{{LanguageId}}/ast", Direction.ServerToClient)]
        public record PublishASTParams : IRequest
        {
            /// <summary>
            /// The URI for which AST information is reported.
            /// </summary>
            public required DocumentUri Uri { get; init; }

            /// <summary>
            /// (Optional) The version number of the document the diagnostics are published for.
            /// </summary>
            [Optional]
            public int? Version { get; init; }

            /// <summary>
            /// The root of the abstract syntax tree.
            /// </summary>
            public required AstNodeInfo Root { get; init; }
        }

        public record AstNodeInfo(string NodeType, string? PropertyName)
        {
            public IReadOnlyList<Property> Properties { get; init; } = [];

            public static AstNodeInfo From(AstNode node, string? propertyName = null)
            {
                if (node is null)
                    return new AstNodeInfo("null", propertyName);

                Type type = node.GetType();
                return new AstNodeInfo(type.Name, propertyName) {
                    Properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                        .Select(p => new Property(p.Name, p.GetValue(node) switch {
                            AstNode n => From(n, p.Name),
                            IEnumerable<AstNode> nodes => nodes.Select(n => From(n)).ToList(),
                            object value when value.GetType() is { IsPrimitive: true } => value,
                            var value => value?.ToString()
                        }, p.PropertyType.Name))
                        .ToList()
                };
            }

            public record struct Property(string Name, object? Value, string TypeName);
        }
        """);

    public static LanguageServerGenerator FromConfig(
        Configuration config, Grammar grammar,
        TextWriter output, Action<Diagnostic> diagnosticHandler)
    {
        var languageId = config.LanguageId ?? config.GetFallbackLanguageId(grammar);
        var languageServerClassName = config.LanguageServer.LanguageServerClassName
            ?? config.LanguageServer.GetFallbackLanguageServerClassName(languageId);

        return new LanguageServerGenerator(output) {
            Grammar = grammar,
            DiagnosticHandler = diagnosticHandler,
            LanguageId = languageId,
            LanguageServerClassName = languageServerClassName,
            Namespace = config.LanguageServer.Namespace,
            AntlrNamespace = config.Parser.Namespace,
            ParserClassName = new(grammar.GetParserClassName()),
            AstNamespace = config.Ast.Namespace,
            AstNodeBaseClassName = new("AstNode"),
            AstRootNodeClassName = config.Ast.RootNodeClass ?? new("AstNode"),
        };
    }
}
