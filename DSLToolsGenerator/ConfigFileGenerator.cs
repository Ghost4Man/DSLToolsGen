using Humanizer;

namespace DSLToolsGenerator;

public class ConfigFileGenerator
{
    public required HyphenDotIdentifierString LanguageId { get; set; }
    public required string? GrammarFile { get; set; }

    public string Generate()
    {
        string languageNamePascalCase = LanguageId.Value.Pascalize();
        var output = new StringWriter { NewLine = "\n" };
        var writer = new IndentedTextWriter(output);
        writer.WriteCode($$"""
          {
            // The schema for this config file (provides code completion and hover tooltips in editors)
            // Run `dtg generate dtgConfigSchema -o dtg.schema.json` to (re)generate the schema
            "$schema": "dtg.schema.json",

            // TODO: replace all of the configuration values as appropriate for your language
            // TIP:  use code completion to discover available configuration options

            "GrammarFile": {{AsJson(GrammarFile ?? $"{LanguageId}.g4")}},
            "LanguageFileExtensions": [".abc"],

            // Which generators to run by default when running `dtg generate` or `dtg watch`:
            "Outputs": {
              "AST": true,
              "LanguageServer": true,
              "Parser": true,
              "TmLanguageJson": true,
              // When disabled, run `dtg generate vscodeExtension` to generate the VSCode extension
              "VscodeExtension": false
            },
            "AST": {
              "OutputPath": "AST.g.cs",
              "Namespace": "{{languageNamePascalCase}}.AST"
            },
            "LanguageServer": {
              "OutputPath": "LanguageServer.g.cs",
              "ProjectPath": "Abc.csproj",
              "Namespace": "{{languageNamePascalCase}}.LanguageServer"
            },
            "Parser": {
              "OutputDirectory": "Parser",
              "Namespace": "{{languageNamePascalCase}}.Parser",
              "AntlrCommand": "java -jar D:\\Antlr\\antlr-4.13.1-complete.jar"
            },
            "SyntaxHighlighting": {
              // The default path is "syntaxes/{LanguageId}.tmLanguage.json"
              //    within the VSCode extension directory
              //"OutputPath": "./tmgrammars/{{LanguageId}}.tmLanguage.json"

              // To customize the syntax highlighting, tweak RuleSettings and RuleConflicts
            },
            "VscodeExtension": {
              "OutputDirectory": "vscode-extension",
              "ExtensionId": "YOURNAME.{{LanguageId.Value.ToLowerInvariant()}}",
              "ExtensionDisplayName": "{{LanguageId}}"
            }
          }
          """);
        return output.ToString();
    }

    static string AsJson(string s) => System.Text.Json.JsonSerializer.Serialize(s);
}
