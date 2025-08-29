using Antlr4Ast;

using NSourceGenerators;

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
    public required IdentifierString LexerClassName { get; init; }
    public required HyphenDotIdentifierString LanguageId { get; init; }

    protected IndentedTextWriter Output { get; }

    public LanguageServerGenerator(TextWriter output)
        => Output = new IndentedTextWriter(output);

    public void GenerateLanguageServer()
    {
        const string LSP = "OmniSharp.Extensions.LanguageServer";

        Output.WriteCode($$"""
            #nullable enable

            using System;
            using System.Linq;
            using System.Collections.Generic;
            using System.Threading.Tasks;

            namespace {{Namespace ?? "LanguageServer"}}
            {
                using Stream = System.IO.Stream;
                using TextWriter = System.IO.TextWriter;
                using Path = System.IO.Path;
                using System.Threading;
                using System.Collections.Concurrent;
                using System.Collections.Immutable;
                using System.Reflection;
                using Microsoft.Extensions.DependencyInjection;
                using Antlr4.Runtime;
                using Antlr4.Runtime.Tree;
                using Antlr4.Runtime.Atn;
                using Antlr4.Runtime.Misc;
                using MediatR;
                using OmniSharp.Extensions.JsonRpc;
                using LSP = OmniSharp.Extensions.LanguageServer;
                using {{LSP}}.Protocol;
                using {{LSP}}.Protocol.Models;
                using {{LSP}}.Protocol.Server;
                using {{LSP}}.Protocol.Document;
                using {{LSP}}.Protocol.Serialization;
                using {{LSP}}.Protocol.Client.Capabilities;
                using {{LSP}}.Protocol.Server.Capabilities;
                using {{LSP}}.Server;
                using AstNode = global::{{AstNamespace?.Value.Append(".")}}{{AstNodeBaseClassName}};
                using AstRootNode = global::{{AstNamespace?.Value.Append(".")}}{{AstRootNodeClassName}};
                using {{ParserClassName}} = global::{{AntlrNamespace?.Value.Append(".")}}{{ParserClassName}};
                using {{LexerClassName}} = global::{{AntlrNamespace?.Value.Append(".")}}{{LexerClassName}};

                partial class {{LanguageServerClassName}} : {{LanguageServerClassName}}Base
                {
                }

                {{_ => GenerateLanguageServerBaseClass()}}

                {{_ => GenerateLspConnectionInfoClass()}}

                {{_ => GenerateDocumentManagerClass()}}

                {{_ => GenerateHoverHandler()}}

                {{_ => GenerateTextDocumentSyncHandler()}}

                {{_ => GenerateHelperClasses()}}

                {{_ => GenerateExtensionMethods()}}

                {{_ => GenerateSemanticTokensBuilderReorderBufferClass()}}

                {{_ => GenerateDocumentClass()}}

                {{_ => GenerateAstRequestHandler()}}

                {{_ => GenerateCodeCompletionHelperClass()}}

                {{_ => GenerateBasicCodeCompletionHandler()}}
                
                {{_ => GenerateBasicSemanticTokensHandler()}}
            }
            """);
    }

    public void GenerateLanguageServerBaseClass() => Output.WriteCode($$"""
        public abstract class {{LanguageServerClassName}}Base
        {
            public const string LanguageId = "{{LanguageId}}";

            public virtual async Task HandleDocumentUpdate(DocumentManager documents,
                TextDocumentIdentifier documentId, string documentText,
                ILanguageServerFacade server)
            {
                List<Diagnostic> diagnostics = [];
                var parser = CreateParser(documentText,
                    lexErr => diagnostics.Add(lexErr.ToLspDiagnostic()),
                    synErr => diagnostics.Add(synErr.ToLspDiagnostic()));

                var document = await AnalyzeDocument(documentId, documentText, parser, diagnostics);

                // publish all collected diagnostics
                server.TextDocument.PublishDiagnostics(documentId, diagnostics);

                // send the AST data
                server.SendNotification(new PublishASTParams {
                    Uri = documentId.Uri,
                    Version = documentId.GetVersion(),
                    Root = AstNodeInfo.From(document?.Ast)
                });

                Console.Error.WriteLine($"Analyzed the document and pushed {diagnostics.Count} diagnostics.");

                if (document is not null)
                {
                    documents.AddOrUpdate(documentId.Uri, document);
                }
            }

            /// <summary>
            /// Parse the given document with the given parser
            /// (lexer and syntax errors are collected automatically)
            /// and analyze its semantics.
            /// </summary>
            /// <param name="parser">A parser (created by <see cref="CreateParser"/>)
            ///     which can be used to analyze the syntax of the given document.</param>
            /// <param name="diagnostics">A (mutable) collection of diagnostics (errors, warnings).</param>
            protected abstract Task<Document> AnalyzeDocument(
                TextDocumentIdentifier documentId, string documentText, {{ParserClassName}} parser,
                IList<Diagnostic> diagnostics);

            protected virtual {{ParserClassName}} CreateParser(string documentText,
                Action<SyntaxErrorInfo<int>> lexicalErrorHandler,
                Action<SyntaxErrorInfo<IToken>> syntaxErrorHandler)
            {
                // parse the document using the ANTLR-generated lexer and parser
                var stream = CharStreams.fromString(documentText);
                var lexer = new {{LexerClassName}}(stream);
                var tokenStream = new CommonTokenStream(lexer);
                var parser = new {{ParserClassName}}(tokenStream);
                lexer.AddErrorListener(new DelegateErrorListener<int>(lexicalErrorHandler));
                parser.AddErrorListener(new DelegateErrorListener<IToken>(syntaxErrorHandler));
                return parser;
            }
        }
        """);

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

            public virtual Document? Get(DocumentUri uri) => Documents.GetValueOrDefault(uri);
            public virtual void Remove(DocumentUri uri) => Documents.Remove(uri, out _);
            public virtual void AddOrUpdate(DocumentUri uri, Document document) => Documents[uri] = document;
        }
        """);

    public void GenerateHoverHandler() => Output.WriteCode($$""""
        public class BasicHoverHandler(DocumentManager documents) : HoverHandlerBase
        {
            public override async Task<Hover?> Handle(HoverParams request, CancellationToken cancellationToken)
            {
                var doc = documents.Get(request.TextDocument.Uri);
                if (doc is null)
                {
                    Console.Error.WriteLine($"received request for unknown document: {request.TextDocument.Uri}");
                    return null;
                }

                return await GetHover(request, cancellationToken, doc);
            }

            protected virtual async Task<Hover?> GetHover(
                HoverParams request, CancellationToken cancellationToken, Document doc)
            {
                return await Task.Run(() => new Hover {
                    Contents = new(new MarkedString($"""
                        Position: `{request.Position}`

                        Token: `{doc.FindTokenAt(request.Position)}`

                        AST node: `{doc.FindDeepestNodeAt(request.Position).GetType().Name}`
                        """)),
                });
            }

            protected override HoverRegistrationOptions CreateRegistrationOptions(
                HoverCapability capability, ClientCapabilities clientCapabilities) => new();
        }
        """");

    public void GenerateTextDocumentSyncHandler() => Output.WriteCode($$"""
        public delegate Task TextDocumentUpdateHandler(DocumentManager documents,
            TextDocumentIdentifier documentId, string documentText, ILanguageServerFacade server);

        public class TextDocumentSyncHandler(DocumentManager documents, ILanguageServerFacade server,
            TextDocumentUpdateHandler handler) : TextDocumentSyncHandlerBase
        {
            public override TextDocumentAttributes GetTextDocumentAttributes(DocumentUri uri)
                => new(uri, languageId: {{LanguageServerClassName}}.LanguageId);

            public override async Task<Unit> Handle(DidOpenTextDocumentParams e, CancellationToken cancellationToken)
            {
                await Console.Error.WriteLineAsync($"Opened {e.TextDocument.Uri}");
                await handler(documents, e.TextDocument, e.TextDocument.Text, server);
                return Unit.Value;
            }

            public override async Task<Unit> Handle(DidChangeTextDocumentParams e, CancellationToken cancellationToken)
            {
                await Console.Error.WriteLineAsync($"Changed {e.TextDocument.Uri} (version {e.TextDocument.Version})");
                await handler(documents, e.TextDocument, e.ContentChanges.Last().Text, server);
                return Unit.Value;
            }

            public override async Task<Unit> Handle(DidSaveTextDocumentParams e, CancellationToken cancellationToken)
            {
                await Console.Error.WriteLineAsync($"Saved {e.TextDocument.Uri}");
                if (e.Text is string documentText)
                {
                    await handler(documents, e.TextDocument, documentText, server);
                }
                return Unit.Value;
            }

            public override async Task<Unit> Handle(DidCloseTextDocumentParams e, CancellationToken cancellationToken)
            {
                await Console.Error.WriteLineAsync($"Closed {e.TextDocument.Uri}");
                documents.Remove(e.TextDocument.Uri);
                return Unit.Value;
            }

            protected override TextDocumentSyncRegistrationOptions CreateRegistrationOptions(TextSynchronizationCapability capability, ClientCapabilities clientCapabilities)
                => new TextDocumentSyncRegistrationOptions() {
                    Change = TextDocumentSyncKind.Full,
                    Save = new SaveOptions { IncludeText = true },
                };
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

            public readonly Diagnostic ToLspDiagnostic(string? details = null) => new() {
                Severity = DiagnosticSeverity.Error,
                Range = GetRange(),
                Code = $"{Path.GetFileNameWithoutExtension(Recognizer.GrammarFileName)}_Error",
                Message = (Message + "\n" + details).TrimEnd(),
            };
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
            public static LanguageServerOptions WithConnection(this LanguageServerOptions options,
                LspConnectionInfo connection, TextWriter? logOutput = null)
            {
                logOutput ??= Console.Error;
            
                switch (connection)
                {
                    case LspConnectionInfo.TcpServer(int port):
                    {
                        var listener = new System.Net.Sockets.TcpListener(
                            System.Net.IPAddress.Loopback, port);
                        listener.Start();
                        logOutput.WriteLine($"Waiting for LSP client to connect to port {port}...");
                        var client = listener.AcceptTcpClient();
                        listener.Stop();
                        options.RegisterForDisposal(client);
                        Stream stream = client.GetStream();
                        logOutput.WriteLine($"LSP client connected.");
                        return options.WithInput(stream).WithOutput(stream);
                    }
                    case LspConnectionInfo.StdIO:
                        return options
                            .WithInput(Console.OpenStandardInput())
                            .WithOutput(Console.OpenStandardOutput());
                    default:
                        throw new NotImplementedException($"unknown connection type {connection.GetType().Name}");
                }
            }

            public static LanguageServerOptions WithTextDocumentSyncHandler(
                this LanguageServerOptions options, TextDocumentUpdateHandler handler)
            {
                return options.AddHandler(s => new TextDocumentSyncHandler(
                    s.GetRequiredService<DocumentManager>(), 
                    s.GetRequiredService<ILanguageServerFacade>(), handler));
            }

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

            public static void PublishDiagnostics(this ITextDocumentLanguageServer server,
                    TextDocumentIdentifier documentId, IEnumerable<Diagnostic> diagnostics)
                => server.PublishDiagnostics(new() {
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
            protected virtual List<(Range range, SemanticTokenType? type, SemanticTokenModifier[] modifiers)> BufferedTokens { get; } = new();

            public bool AutoFlushPreviousLine { get; set; } = true;

            /// <summary>
            /// If <paramref name="pushedToken"/> is on a new line, flushes previously buffered tokens.
            /// Adds <paramref name="pushedToken"/> into the current line's buffer.
            /// </summary>
            public void PushToken(Range? range, (SemanticTokenType? type, SemanticTokenModifier[] modifiers) pushedToken)
            {
                if (range is null)
                    return;

                if (AutoFlushPreviousLine
                    && BufferedTokens is [.., var last]
                    && range.Start.Line > last.range.Start.Line) // new line
                {
                    Flush();
                }

                BufferedTokens.Add((range, pushedToken.type, pushedToken.modifiers));
            }

            /// <summary>
            /// Push buffered tokens to the underlying <see cref="LSP.Protocol.Document.SemanticTokensBuilder"/>
            /// ordered by position in the document.
            /// This method must be called (either explicitly or via <see cref="IDisposable.Dispose()"/>)
            /// to flush tokens from the last line.
            /// </summary>
            public void Flush()
            {
                BufferedTokens.Sort(static (a, b) => a.range.Start.CompareTo(b.range.Start));
                foreach (var token in BufferedTokens)
                {
                    builder.Push(token.range, token.type, token.modifiers);
                }
                BufferedTokens.Clear();
            }
        }
        """);

    public void GenerateDocumentClass() => Output.WriteCode($$"""
        /// <summary>
        /// Represents a single document in the {{LanguageId}} language.
        /// </summary>
        /// <param name="Text">The full contents of the document.</param>
        /// <param name="Ast">The root node of the abstract syntax tree (AST) for this document.
        {{(AstRootNodeClassName == AstNodeBaseClassName
            ? "///     <para>TIP: for more type safety, configure AST.RootNodeClass in `dtg.json`</para>"
            : null)}}
        /// </param>
        /// <param name="Parser">The <see cref="{{ParserClassName}}"/> instance used to parse this document.</param>
        public partial record Document(string Text, AstRootNode Ast, {{ParserClassName}} Parser)
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

            public IToken? FindTokenAt(Position position, bool preferLeftTokenAtBoundary = false)
            {
                var tokens = (BufferedTokenStream)Parser.TokenStream;
                // assuming that there are no holes
                Func<IToken, bool> predicate = preferLeftTokenAtBoundary
                    ? (t => t.GetEndPosition() >= position)
                    : (t => t.GetEndPosition() > position);
                IList<IToken> tokenList = tokens.GetTokens();
                return tokenList.FirstOrDefault(predicate);
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

            public static AstNodeInfo From(AstNode? node, string? propertyName = null)
            {
                if (node is null)
                    return new AstNodeInfo("null", propertyName);

                Type type = node.GetType();
                return new AstNodeInfo(type.Name, propertyName) {
                    Properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                        .Select(p => new Property(p.Name, p.GetValue(node) switch {
                            AstNode n => From(n, p.Name),
                            IEnumerable<AstNode> nodes => nodes.Select(n => From(n)).ToList(),
                            ParserRuleContext c => c.GetType().Name,
                            object value when value.GetType() is { IsPrimitive: true } => value,
                            var value => value?.ToString(),
                        }, p.PropertyType.Name))
                        .DistinctBy(p => p.Name)
                        .ToList()
                };
            }

            public record struct Property(string Name, object? Value, string TypeName);
        }
        """);

    public void GenerateCodeCompletionHelperClass()
        => Output.WriteLine(CodeToStringRepo.GetText("CodeCompletionHelperClass"));

    public void GenerateBasicCodeCompletionHandler()
    {
        Output.WriteCode($$"""
            public class BasicCodeCompletionHandler(DocumentManager documents) : CompletionHandlerBase
            {
                public override async Task<CompletionList> Handle(CompletionParams e, CancellationToken cancellationToken)
                {
                    Document? doc = documents.Get(e.TextDocument.Uri);
                    if (doc is null)
                    {
                        await Console.Error.WriteLineAsync($"cannot fulfill request for unknown document: {e.TextDocument.Uri}");
                        return [];
                    }

                    var codeCompletion = InitializeCodeCompletion(doc);

                    var tokenAtCaret = doc.FindTokenAt(e.Position, preferLeftTokenAtBoundary: true);
                    await Console.Error.WriteLineAsync($"token at caret: {tokenAtCaret?.ToString() ?? "null"}");

                    if (tokenAtCaret?.TokenIndex is not int caretTokenIndex)
                        return [];

                    var nodeAtCaret = doc.FindDeepestNodeAt(e.Position);
                    await Console.Error.WriteLineAsync($"node at caret: {nodeAtCaret?.GetType().Name ?? "null"}");

                    var candidates = codeCompletion.CollectCandidates(caretTokenIndex, context: nodeAtCaret?.ParserContext);
                    return candidates.Tokens
                        .Select(kvp => {
                            if (doc.Parser.Vocabulary.GetLiteralName(kvp.Key) is not string literalText)
                                return null;
                            return new CompletionItem {
                                Label = literalText[1..^1],
                                Kind = CompletionItemKind.Keyword,
                                LabelDetails = new() { Description = "token" },
                            };
                        })
                        .Concat(candidates.Rules.SelectMany(kvp =>
                            GetCompletionsForRule(doc, kvp.Key, kvp.Value)))
                        .WhereNotNull()
                        .ToList();
                }

                public virtual IEnumerable<CompletionItem> GetCompletionsForRule(
                    Document doc, int ruleIndex, IList<int> callStack)
                {
                    string ruleName = doc.Parser.RuleNames[ruleIndex];

                    return (GetCompletionSnippetsForRule(ruleName) ?? [])
                        .DefaultIfEmpty((label: ruleName, snippet: "${1:" + ruleName + "}"))
                        .Select(s => new CompletionItem {
                            Label = s.label,
                            Kind = CompletionItemKind.Snippet,
                            //Documentation = "rule call stack: " + string.Concat(
                            //    callStack.Select(ri => "\n- " + doc.Parser.RuleNames[ri])),
                            LabelDetails = new() { Description = "rule" },
                            InsertTextFormat = InsertTextFormat.Snippet,
                            InsertText = s.snippet,
                        });
                }

                public override Task<CompletionItem> Handle(CompletionItem request, CancellationToken cancellationToken)
                    => Task.FromResult(request);

                protected override CompletionRegistrationOptions CreateRegistrationOptions(CompletionCapability capability, ClientCapabilities clientCapabilities)
                    => new();

                public virtual CodeCompletionCore InitializeCodeCompletion(Document doc)
                {
                    return new CodeCompletionCore(doc.Parser,
                        preferredRules: new HashSet<int> {
                            {{ForEach(Grammar.ParserRules, r => Output.WriteCode($""""
                                {ParserClassName}.RULE_{r.Name},
                                """"))}}
                        },
                        ignoredTokens: null);
                }

                public virtual IEnumerable<(string label, string snippet)>? GetCompletionSnippetsForRule(string ruleName)
                    => GetDefaultCompletionSnippetsForRule(ruleName);

                protected virtual string GetSnippetLabel(string ruleName, string? altLabel)
                    => altLabel ?? ruleName;

                protected virtual string GetSnippetPlaceholderText(string containingRule, string referencedRule, string? label)
                    => label ?? referencedRule;

                protected (string label, string snippet)[]? GetDefaultCompletionSnippetsForRule(string ruleName) => ruleName switch {
                    {{ForEach(Grammar.ParserRules, r => Output.WriteCode($""""
                        "{r.Name}" => [
                            {ForEach(r.GetAlts(), a => writeSnippetForAlt(r, a))}
                        ],
                        """"))}}
                    _ => null,
                };
            }
            """);

        void writeSnippetForAlt(Rule rule, Alternative alt)
        {
            if (makeSnippetForAlt(rule, alt) is not (string snippet and not ""))
                return;
            Output.WriteCode($""""
                (GetSnippetLabel({toCsharpLiteral(rule.Name)}, {toCsharpLiteral(alt.ParserLabel)}), $$"""{snippet}"""),
                """");
        }

        string makeSnippetForAlt(Rule containingRule, Alternative alt)
        {
            int placeholderCounter = 1;
            return string.Join(" ", alt.Elements
                .Select(e => getSnippetTextForElement(containingRule, e, ref placeholderCounter))
                .TakeWhile(s => s is not null));
        }

        string? getSnippetTextForElement(Rule containingRule, SyntaxElement element, ref int placeholderCounter) => element switch {
            Literal(string text) => text,
            TokenRef tr when tr.GetTokenText(Grammar) is string text => text,
            TokenRef(string ruleName) t => $"${{{placeholderCounter++}:{
                interpolationForPlaceholderText(containingRule, t.Name, t.Label)}}}",
            RuleRef(string ruleName) r => $"${{{placeholderCounter++}:{
                interpolationForPlaceholderText(containingRule, r.Name, r.Label)}}}",
            { Label: string label } => $"${{{placeholderCounter++}:{label}}}",
            _ => null,
        };

        string interpolationForPlaceholderText(Rule containingRule, string ruleName, string? label) => $$$"""
            {{GetSnippetPlaceholderText({{{toCsharpLiteral(containingRule.Name)}}}, {{{toCsharpLiteral(ruleName)}}}, {{{toCsharpLiteral(label)}}})}}
            """;

        // assumes that the identifier does not contain quotes, backslashes, control characters, ...
        string toCsharpLiteral(string? identifier)
            => identifier is null ? "null" : ('"' + identifier + '"');
    }

    public void GenerateBasicSemanticTokensHandler() => Output.WriteCode($$"""
        public class BasicSemanticTokensHandler(DocumentManager documents) : SemanticTokensHandlerBase
        {
            protected virtual SemanticTokensLegend SemanticTokensLegend { get; } = new();

            protected override SemanticTokensRegistrationOptions CreateRegistrationOptions(SemanticTokensCapability capability, ClientCapabilities clientCapabilities)
                => new() {
                    Full = new SemanticTokensCapabilityRequestFull { Delta = false },
                    Range = true,
                    Legend = SemanticTokensLegend,
                };

            protected override async Task<SemanticTokensDocument> GetSemanticTokensDocument(
                ITextDocumentIdentifierParams @params, CancellationToken cancellationToken)
            {
                // since we don't support Delta requests, just return a new document every time
                return new SemanticTokensDocument(SemanticTokensLegend);
            }

            protected override async Task Tokenize(SemanticTokensBuilder builder,
                ITextDocumentIdentifierParams @params, CancellationToken cancellationToken)
            {
                Document? doc = documents.Get(@params.TextDocument.Uri);
                if (doc is null)
                {
                    await Console.Error.WriteLineAsync($"cannot fulfill request for unknown document: {@params.TextDocument.Uri}");
                    return;
                }

                var nodes = @params is SemanticTokensRangeParams { Range: Range range }
                    ? doc.GetNodesBetweenLines(range.Start.Line, range.End.Line)
                    : doc.Ast.GetAllDescendantNodes();

                var bufferedBuilder = new SemanticTokensBuilderReorderBuffer(builder);

                foreach (AstNode node in nodes)
                {
                    PushSemanticTokensForNode(doc, node, bufferedBuilder);
                }

                bufferedBuilder.Flush();
            }

            public virtual void PushSemanticTokensForNode(
                Document document, AstNode node, SemanticTokensBuilderReorderBuffer tokens)
            {
            }
        }
        """);

    Action<IndentedTextWriter> ForEach<T>(IEnumerable<T> items, Action<T> action)
    {
        return _ => {
            foreach (var item in items)
            {
                action(item);
            }
        };
    }

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
            LanguageId = languageId.Transform(s => s.ToLowerInvariant()),
            LanguageServerClassName = languageServerClassName,
            Namespace = config.LanguageServer.Namespace,
            AntlrNamespace = config.Parser.Namespace,
            ParserClassName = new(grammar.GetParserClassName()),
            LexerClassName = new(grammar.GetParserClassName().TrimSuffix("Parser") + "Lexer"),
            AstNamespace = config.Ast.Namespace,
            AstNodeBaseClassName = new("AstNode"),
            AstRootNodeClassName = config.Ast.RootNodeClass ?? new("AstNode"),
        };
    }
}
