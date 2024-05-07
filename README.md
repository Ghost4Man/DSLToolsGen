﻿# DSL Tools Generator

This is a project aimed at simplifying the development of domain-specific languages by generating parts of their implementation (e.g. AST) and tools for using the language in an editor like VSCode (e.g. LSP server and editor extensions with syntax highlighting) based on an ANTLR4 grammar and a DSL config file.

## Generators

- **Syntax Highlighting** – outputs a `.tmLanguage.json` file, which contains a TextMate grammar for syntax highlighting supported by many editors, including VSCode.

- **AST** – generates C# source code of AST node classes (as `record`s) and an `AstBuilder` class, which is a Visitor implementation that transforms parse trees (output by ANTLR-generated parsers) into ASTs ready for further analysis, validation and transformations.

- **VSCode Extension**

- **Language Server**

## Usage

Build with `dotnet build` (requires .NET 8 SDK).  
```bash
cd DSLToolsGenerator
dotnet build
```

Run with `dotnet run -- <args>`, or create a `dtg` alias to the executable (recommended):

- `dtg generate` to run the configured generators once
- `dtg watch` to automatically rerun the configured generators once their inputs change

Detailed steps for adding DTG-generated tools into a C# project:

1. create or open a C# project and add these NuGet packages as dependencies:
    - ANTLR runtime library: `Antlr4.Runtime.Standard`
    - OmniSharp LSP library: `OmniSharp.Extensions.LanguageServer`
2. run `dtg generate dtgConfigSchema -o dtg.schema.json` (to make editing the config file easier)
3. create a `dtg.json` file and fill it with configuration values (primarily the grammar file name, VSCode extension ID, and output paths). See the "Sample Configuration" section.
4. run `dtg generator` or `dtg watch` (to keep the generator running and regenerating whenever the inputs are modified)
5. generate the VS Code extension using `dtg generate vscodeExtension` (it is not generated automatically by default because it's usually enough to generate it once and then only regenerate the TextMate grammar for syntax highlighting; it's also a little risky since there's no clear separation of user-written vs autogenerated code)
6. add code to run the generated LanguageServer if the `--ls` argument is provided to the top of `Program.cs`, for example:
    ```c#
    if (args.Contains("--ls"))
    {
        var connection = LspConnectionInfo.FromCommandLineArgs(args);
        bool loop = connection is LspConnectionInfo.TcpServer;
        do
        {
            await new XyzLanguageServer().RunAsync(connection);
            Console.Error.WriteLine("Language server stopped.");
        }
        while (loop);
    }
    ```
7. execute `cd vscode-extension && npm install`
8. launch the language server using `dotnet watch -- --ls --tcpserver=42882` (or through Visual Studio; you can add a launch profile with the arguments)
9. now you can edit code while running (using hot-reload) or restart the LSP server – the LSP client should try to reconnect automatically (or reconnect manually via the `Xyz: Restart Language Server` command)
10. add a `LanguageServer.cs` file with the following contents (replace Xyz with the name of your language):
    ```c#
    using Microsoft.Extensions.DependencyInjection;
    using LSP = OmniSharp.Extensions.LanguageServer;
    using OmniSharp.Extensions.LanguageServer.Protocol.Models;
    using Xyz.Parser;
    using Xyz.AST;

    namespace Xyz.LanguageServer;

    partial class XyzLanguageServer
    {
        public async Task RunAsync(LspConnectionInfo connection)
        {
            DocumentManager documents = new();

            var server = await LSP.Server.LanguageServer.From(options => options
                .WithConnection(connection)
                .WithServices(s => s.AddSingleton(documents))
                .WithTextDocumentSyncHandler(HandleDocumentUpdate)
                .WithHandler<BasicHoverHandler>()
                .WithHandler<BasicCodeCompletionHandler>()
            );

            await server.WaitForExit.ConfigureAwait(false);
        }
    
        protected override async Task<Document> AnalyzeDocument(
            TextDocumentIdentifier documentId, string documentText,
            XyzParser parser, IList<Diagnostic> diagnostics)
        {
            // Convert parse tree to AST
            var ast = new AstBuilder().VisitIdlFile(parser.idlFile());

            // Here you can perform any additional semantic analysis,
            // add errors and warnings to `diagnostics`,
            // attach data to AST nodes (use `partial` class declarations 
            // to add new properties to the AST node classes), etc.

            return new Document(documentText, ast, parser);
        }
    }

    ```
11. implement any custom Language Server functionality like Publishing Diagnostics (errors), Code Completion (using the generated `BasicCodeCompletionHandler` base class), Semantic Highlighting (using the generated `BasicSemanticTokensHandler` base class), etc. Don't forget to register custom handlers using `.WithHandler<T>()`

You can also check out the sample projects in the `Samples` directory.

### Troubleshooting

If the syntax highlighting looks wrong, open the TextMate Scope Inspector in VSCode and look at what scope is being assigned to the token; the default scope names include the original lexer name at the end... there might be a rule conflict (shadowing) -> add it into the configuration

## Sample Configuration

```jsonc
{
    "$schema": "dtg.schema.json",
    "CsprojName": "AvroIDL",
    "GrammarFile": "AvroIDL.g4",
    "Outputs": {
        "AST": true,
        "Parser": true,
        "LanguageServer": true,
        "TmLanguageJson": true,
        // can still be run manually using `dtg generate vscodeExtension`
        "VscodeExtension": false,
    },
    "AST": {
        "Namespace": "AvroIDL.AST",
    },
    "SyntaxHighlighting": {
        "OutputPath": "vscode-extension/syntaxes/AvroIDL.tmLanguage.json",
    },
    "Parser": {
        "OutputDirectory": "Parser",
        "Namespace": "AvroIDL.Parser",
        "AntlrCommand": "java -jar T:\\Antlr\\antlr-4.13.1-complete.jar"
    },
    "VscodeExtension": {
        "ExtensionId": "dtgtest.avroidl",
        "ExtensionDisplayName": "AvroIDL",
    },
    "LanguageServer": {
        "Namespace": "AvroIDL.LanguageServer"
    }
}

```

## Dependencies

To parse command line options and commands, this project uses `System.CommandLine`.

For analysis of the ANTLR4 grammar provided as input, this project currently uses [Antlr4Ast](http://github.com/xoofx/Antlr4Ast), an easy-to-use C# library for parsing ANTLR grammar files (`.g4`) developed by Alexandre Mutel.

For implementation of the watch mode (reacting to changes in input files), this code uses *Reactive Extensions for .NET* (Rx.NET).

The generated language server code includes an adapted version of the code from the *antlr4-c3* library created by Mike Lischke and ported to C# by Jonathan Philipps.
