# DSL Tools Generator

This is a project aimed at simplifying the development of domain-specific languages by generating parts of their implementation (e.g. AST) and tools for using the language in an editor like VSCode (e.g. LSP server and editor extensions with syntax highlighting) based on an ANTLR4 grammar and a DSL config file.

## Usage

Build with `dotnet build` (requires .NET 8 SDK).  
Run with `dotnet run -- <args>`.

```bash
cd DSLToolsGenerator
dotnet build
dotnet run -- generate ast AbcParser.g4 -o AST.cs [--watch]
dotnet run -- generate tmLanguage AbcLexer.g4 -o Abc.tmLanguage.json [--watch]
```

## Generators

### AST Code Generator

Generates C# source code of AST node classes (as `record`s) and an `AstBuilder` class, which is a Visitor implementation that transforms parse trees (output by ANTLR-generated parsers) into ASTs ready for further analysis, validation and transformations.

```bash
dotnet run -- generate ast ./Samples/ExampleParser.g4 -o ../gen/AST.cs [--watch]
```

### Syntax Highlighting

Outputs a `.tmLanguage.json` file, which defines a TextMate grammar for syntax highlighting supported by many editors, including VSCode.

```bash
dotnet run -- generate tmLanguage ./Samples/ExampleLexer.g4 -o ../gen/Example.tmLanguage.json
```

## Implementation

For analysis of the ANTLR4 grammar provided as input, this project currently uses [Antlr4Ast](http://github.com/xoofx/Antlr4Ast), an easy-to-use C# library for parsing ANTLR grammar files (`.g4`) developed by Alexandre Mutel.

A potential future improvement would be to create a little helper program for extracting grammar analysis data directly from the official ANTLR tool (used as a Java library) and then use this data in the rest of the project (from C#). This would probably be more robust and future-proof than using a third-party library.
