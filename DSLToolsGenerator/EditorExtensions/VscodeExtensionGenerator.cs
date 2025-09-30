using System.Text.Json;

using Antlr4Ast;

namespace DSLToolsGenerator.EditorExtensions;

public class VscodeExtensionGenerator
{
    public required HyphenDotIdentifierString LanguageId { get; init; }
    public required string LanguageDisplayName { get; init; }
    public required string[] LanguageFileExtensions { get; init; }
    public required string CommandCategoryName { get; init; }
    public required string LanguageServerProjectPath { get; init; }
    public required string LanguageServerAssemblyName { get; init; }
    public required string ExtensionDisplayName { get; init; }
    public required HyphenDotIdentifierString ExtensionId { get; init; }
    public required string LanguageClientName { get; init; }
    public required string OutputDirectory { get; init; }
    public required bool IncludeAstExplorerView { get; init; }

    public void GenerateExtension(Func<FileInfo, IndentedTextWriter?> fileWriterFactory)
    {
        writeFile("../.vscode/launch.json", GenerateLaunchConfiguration);
        writeFile("../.vscode/tasks.json", GenerateNpmTasks);
        writeFile("package.json", GenerateManifest);
        writeFile("src/extension.ts", GenerateEntryPoint);
        writeFile("language-configuration.json", GenerateLanguageConfiguration);
        writeFile("tsconfig.json", GenerateTypeScriptConfiguration);
        writeFile(".vscodeignore", GenerateVscodeignoreFile);
        writeFile("README.md", GenerateReadme);

        if (IncludeAstExplorerView)
        {
            writeFile("src/ASTProvider.ts", GenerateAstProvider);
        }

        void writeFile(string fileName, Action<IndentedTextWriter> generatorFunction)
        {
            string filePath = Path.Combine(OutputDirectory, fileName);
            using var writer = fileWriterFactory(new FileInfo(filePath));
            if (writer is not null)
                generatorFunction(writer);
        }
    }

    /// <summary>
    /// Generates the extension's launch (debugging) configuration
    /// (<c>.vscode/launch.json</c>).
    /// </summary>
    /// <param name="output"></param>
    public void GenerateLaunchConfiguration(IndentedTextWriter output)
    {
        string extensionDirectory = Path.Combine("${workspaceFolder}", OutputDirectory)
            .Replace(Path.DirectorySeparatorChar, '/');

        output.WriteCode($$"""
            {
                "version": "0.2.0",
                "configurations": [
                    {
                        "name": "Extension",
                        "type": "extensionHost",
                        "request": "launch",
                        "args": [
                            "--extensionDevelopmentPath={{extensionDirectory}}",
                            "--disable-extensions",
                            //"--profile=",
                        ],
                        "env": {
                            // for debugging: connect to a running language server over TCP
                            "LSP_SERVER_DEV_TCP_PORT": "42882",
                        },
                        "outFiles": [
                            "{{extensionDirectory}}/out/**/*.js",
                        ],
                        "autoAttachChildProcesses": true,
                        "preLaunchTask": {
                            "type": "npm",
                            "script": "watch",
                            "path": "{{OutputDirectory}}",
                        },
                    }
                ]
            }
            """);
    }

    public void GenerateNpmTasks(IndentedTextWriter output) => output.WriteCode($$"""
        {
        	"version": "2.0.0",
        	"tasks": [
        		{
        			"type": "npm",
        			"script": "compile",
                    "path": "{{OutputDirectory}}",
        			"group": "build",
        			"presentation": {
        				"panel": "dedicated",
        				"reveal": "never"
        			},
        			"problemMatcher": ["$tsc"]
        		},
        		{
        			"type": "npm",
        			"script": "watch",
                    "path": "{{OutputDirectory}}",
        			"isBackground": true,
        			"group": {
        				"kind": "build",
        				"isDefault": true
        			},
        			"presentation": {
        				"panel": "dedicated",
        				"reveal": "never"
        			},
        			"problemMatcher": ["$tsc-watch"]
        		}
        	]
        }
        """);

    /// <summary>
    /// Generates the extension's main (entry point) TypeScript file
    /// (<c>src/extension.ts</c>).
    /// </summary>
    public void GenerateEntryPoint(IndentedTextWriter output) => output.WriteCode($$"""
        // based on the official "Language Server Extension Guide"
        // https://code.visualstudio.com/api/language-extensions/language-server-extension-guide

        /* --------------------------------------------------------------------------------------------
         * Copyright (c) Microsoft Corporation. All rights reserved.
         * Licensed under the MIT License. See License.txt in the project root for license information.
         * ------------------------------------------------------------------------------------------ */

        import * as net from 'node:net';
        import * as vscode from 'vscode';
        import { LanguageClient, LanguageClientOptions, ServerOptions, StreamInfo } from 'vscode-languageclient/node';
        {{(IncludeAstExplorerView ? "import { ASTProvider } from './ASTProvider';" : "")}}

        const SECONDS_TO_WAIT_BEFORE_RECONNECTING = 20;

        let client: LanguageClient;
        let devTcpMode: boolean;
        let shouldAttemptToReconnect = true;

        export function activate(context: vscode.ExtensionContext) {
            const config = vscode.workspace.getConfiguration("{{ExtensionId}}");
                    
            // load the language server path and arguments from extension settings
            let languageServerCommand = config.get("languageServer.path") as string;
            let languageServerArgs = config.get("languageServer.args") as string[] ?? ["--ls"];

            if (!languageServerCommand) {
                // use the `dotnet LanguageServer.dll` way to launch the language server
                // since it's cross-platform (unlike using the `.exe` and ELF wrappers)
                const languageServerDllPath = context.asAbsolutePath({{
                    AsJson($"LanguageServer/{LanguageServerAssemblyName}.dll")}});
                languageServerCommand = "dotnet";
                languageServerArgs.unshift(languageServerDllPath, "--");
            }

            // If the extension is launched in debug mode then the debug server options are used
            // Otherwise the run options are used
            const defaultServerOptions: ServerOptions = {
                run: { command: languageServerCommand, args: languageServerArgs },
                debug: {
                    command: languageServerCommand, args: languageServerArgs,
                    options: {
                        // Force enable hot-reload (it's disabled by default when
                        // attaching to the language server from a debugger)
                        env: { "DOTNET_ForceENC": "1" },
                    },
                },
            };

            // In debug mode, try to connect over TCP first (to make debugging the language server from Visual Studio easier)
            const languageServerTcpPort = Number(process.env.LSP_SERVER_DEV_TCP_PORT);    
            devTcpMode = context.extensionMode === vscode.ExtensionMode.Development && !!languageServerTcpPort;
            const serverOptions: ServerOptions = devTcpMode
                ? getDebugServerOptions(languageServerTcpPort)
                : defaultServerOptions;

            // Options to control the language client
            const clientOptions: LanguageClientOptions = {
                documentSelector: [{ scheme: 'file', language: '{{LanguageId}}' }],
                synchronize: {
                    // Notify the server about file changes to '.xyz' files contained in the workspace
                    //fileEvents: workspace.createFileSystemWatcher('**/.xyz')
                },
            };

            // Create the language client and start the client.
            client = new LanguageClient('{{LanguageId}}', '{{LanguageClientName}}', serverOptions, clientOptions);

            {{_ => { if (IncludeAstExplorerView) output.WriteCode($$"""
                // Create an AST tree view and handle AST change notifications from the server
                const astProvider = new ASTProvider();
                client.onNotification('{{LanguageId}}/ast', notification => {
                    console.log("Received {{LanguageId}}/ast notification: ", notification);
                    astProvider.setRoot(notification.root);
                });
                context.subscriptions.push(
                    vscode.window.registerTreeDataProvider('{{ExtensionId}}.astExplorer', astProvider));
                """); }}}

            // Start the client. This will also launch the server
            startClient();

            context.subscriptions.push(
                vscode.commands.registerCommand('{{ExtensionId}}.restartLanguageServer',
                () => { restartClient(); }));
        }

        async function startClient() {
            try {
                let starting = client.start();
                vscode.window.setStatusBarMessage(
                    devTcpMode ? "Connecting..." : "Starting language server...", starting);
                await starting;
            }
            catch (error) {
                client.error(`Error while starting LSP client of {{LanguageClientName}}: ${error}`, error, false);
                showConnectionErrorAndOfferRestart(devTcpMode
                    ? `${error}. Is the language server running in dev (TCP server) mode?`
                    : `${error}`);
            }
        }

        async function restartClient() {
            await client.stop()
                .catch(_ => { }); // ignore "Client is not running and can't be stopped." error
            await new Promise(resolve => setTimeout(resolve, 1000)); // delay to let server restart
            await startClient();
        }

        export function deactivate(): Thenable<void> | undefined {
            if (!client) {
                return undefined;
            }
            return client.stop();
        }

        function getDebugServerOptions(languageServerTcpPort: number): ServerOptions {
            return () => {
                client.info(`Connecting to language server over TCP at port ${languageServerTcpPort}...`, null, false);

                let socket = net.connect({ port: languageServerTcpPort, timeout: 5000 });
                let result: StreamInfo = { writer: socket, reader: socket, detached: true };
                socket.on('connect', () => {
                    client.info("Connected.");
                });
                socket.on('close', async hadError => {
                    if (hadError) {
                        client.warn(`Connection to language server was closed due to an error`, null, true);
                        if (shouldAttemptToReconnect) {
                            shouldAttemptToReconnect = false;
                            await statusBarCountDown(SECONDS_TO_WAIT_BEFORE_RECONNECTING, "Attempting to reconnect");
                            await startClient();
                            await new Promise(resolve => setTimeout(resolve, 5000));
                            shouldAttemptToReconnect = true;
                        }
                    }
                    else
                        client.info(`Connection to language server was closed.`, null, true);
                });
                socket.on('error', err => {
                    if (socket.connecting)
                        client.error(`Error: could not connect to language server over TCP: ${err.message}`, err, true);
                    else
                        client.error(`Error: TCP connection error: ${err.message}`, err, false);
                });
                socket.on('timeout', async () => {
                    if (socket.connecting)
                        client.error("Error: TCP connection to language server timed out", null, false);
                });
                return Promise.resolve(result);
            }
        }

        async function statusBarCountDown(seconds: number, labelPrefix: string) {
            for (let i = seconds; i > 0; i--) {
                let timeout = new Promise(resolve => setTimeout(resolve, 1000));
                vscode.window.setStatusBarMessage(`$(loading~spin) ${labelPrefix} in ${i}s`, timeout);
                await timeout;
            }
        }

        // This should only ever appear when debugging this extension
        // (and thus connecting to language server over TCP)
        async function showConnectionErrorAndOfferRestart(message?: string) {
            let action = await vscode.window.showErrorMessage(
                "Error while connecting to language server" + (message ? `: ${message}` : "."),
                'Retry');
            if (action === 'Retry') {
                restartClient();
            }
        }
        """);

    /// <summary>
    /// Generates the <c>src/ASTProvider.ts</c> file.
    /// </summary>
    public void GenerateAstProvider(IndentedTextWriter output) => output.WriteCode($$"""
        import * as vscode from 'vscode';

        export class ASTProvider implements vscode.TreeDataProvider<ASTTreeItem> {
          private ast?: ASTNodeInfo;
          private _onDidChangeTreeData: vscode.EventEmitter<ASTTreeItem | undefined | null | void> = new vscode.EventEmitter<ASTTreeItem | undefined | null | void>();
          readonly onDidChangeTreeData: vscode.Event<ASTTreeItem | undefined | null | void> = this._onDidChangeTreeData.event;

          setRoot(ast: any): void {
            this.ast = ast;
            this._onDidChangeTreeData.fire();
          }

          getTreeItem(item: ASTTreeItem): vscode.TreeItem {
            if (isProperty(item)) {
              if (isObject(item.value) && !Array.isArray(item.value) && isNode(item.value)) {
                return this.getTreeItem(item.value);
              }

              const valueStr = valueToString(item);
              return {
                label: item.name,
                description: `= ${valueStr}`,
                iconPath: new vscode.ThemeIcon("symbol-property"),
                tooltip: `${item.typeName} ${item.name} = ${valueStr}`,
                contextValue: "property",
                collapsibleState: Array.isArray(item.value) || (isObject(item.value) && isNode(item.value))
                  ? vscode.TreeItemCollapsibleState.Collapsed
                  : vscode.TreeItemCollapsibleState.None,
              };
            }
            else { // AST node
              const [label, descr] = [item.propertyName, item.nodeType].filter(x => !!x);
              return {
                label: label,
                description: descr,
                iconPath: new vscode.ThemeIcon("circle-filled"),
                contextValue: "node",
                collapsibleState: label !== "null"
                  ? vscode.TreeItemCollapsibleState.Collapsed
                  : vscode.TreeItemCollapsibleState.None,
              };
            }

            function valueToString(property: ASTProperty) {
              const { value, typeName } = property;
              return typeof value === 'string' && typeName === "String" ? `"${value}"` :
                isObject(value) ? typeName :
                (value?.toString() ?? "null");
            }
          }

          getChildren(item?: ASTTreeItem): Thenable<ASTTreeItem[]> {
            if (this.ast == undefined) {
              return Promise.resolve([]);
            }

            if (item == undefined) { // root
              return Promise.resolve([this.ast]);
            }

            if (isProperty(item)) {
              return Promise.resolve(
                Array.isArray(item.value) ? item.value :
                isObject(item.value) && isNode(item.value) ? this.getChildren(item.value) :
                []);
            }

            return Promise.resolve(item.properties);
          }
        }

        type ASTTreeItem = ASTNodeInfo | ASTProperty;

        interface ASTNodeInfo {
          nodeType: string,
          propertyName?: string,
          properties: ASTProperty[],
        }

        interface ASTProperty {
          name: string;
          value: ASTNodeInfo[] | ASTNodeInfo | number | string | boolean | null;
          typeName: string;
        }

        function isObject(value: any): value is object { return value !== null && typeof(value) === 'object'; }
        function isProperty(item: ASTTreeItem): item is ASTProperty { return "value" in item; }
        function isNode(item: ASTTreeItem): item is ASTNodeInfo { return "nodeType" in item; }
        """);

    /// <summary>
    /// Generates the <c>.vscodeignore</c> file.
    /// </summary>
    /// <param name="output"></param>
    public void GenerateVscodeignoreFile(IndentedTextWriter output) => output.WriteCode($$"""
        # This file lists files that should not be packaged into the extension.
        # `vsce ls` can be used to check what files will be included in the extension

        # Ignore everything ...
        **/*

        # ... except:
        !README.md
        !package.json
        !language-configuration.json
        !CHANGELOG.md
        !LICENSE*
        !out
        !syntaxes
        !node_modules
        !LanguageServer
        """);

    /// <summary>
    /// Generates the <c>README.md</c> file.
    /// </summary>
    /// <param name="output"></param>
    public void GenerateReadme(IndentedTextWriter output) => output.WriteCode($$"""

        """);

    /// <summary>
    /// Generates the <c>package.json</c> (extension manifest) file.
    /// </summary>
    /// <param name="output"></param>
    public void GenerateManifest(IndentedTextWriter output) => output.WriteCode($$"""
        {
          "name": "{{ExtensionId}}",
          "displayName": {{AsJson(ExtensionDisplayName)}},
          "description": "",
          "version": "0.0.1",
          "engines": {
            "vscode": "^1.82.0"
          },
          "categories": ["Programming Languages"],
          "main": "./out/extension",
          "contributes": {
            "languages": [
              {
                "id": "{{LanguageId}}",
                "aliases": [{{AsJson(LanguageDisplayName)}}],
                "extensions": {{AsJson(LanguageFileExtensions)}},
                "configuration": "./language-configuration.json"
              }
            ],
            "grammars": [
              {
                "language": "{{LanguageId}}",
                "scopeName": "source.{{LanguageId}}",
                "path": "./syntaxes/{{LanguageId}}.tmLanguage.json"
              }
            ],
            "configuration": [
              {
                "title": "LSP",
                "properties": {
                  "{{ExtensionId}}.languageServer.path": {
                    "type": "string",
                    "default": null,
                    "description": {{AsJson($"The path to the {LanguageDisplayName} language server executable")}}
                  },
                  "{{ExtensionId}}.languageServer.args": {
                    "type": "array",
                    "items": { "type": "string" },
                    "default": ["--ls"],
                    "description": {{AsJson($"The command-line arguments passed to the {LanguageDisplayName} language server")}}
                  }
                }
              }
            ],
            "commands": [
              {
                "command": "{{ExtensionId}}.restartLanguageServer",
                "category": "{{CommandCategoryName}}",
                "title": "Restart Language Server",
                "icon": "$(debug-restart)"
              }
            ]{{(IncludeAstExplorerView ? $$"""
                ,
                "views": {
                  "explorer": [
                    {
                      "id": "{{ExtensionId}}.astExplorer",
                      "name": "AST Explorer"
                    }
                  ]
                }
                """ : "")}}
          },
          "scripts": {
            "vscode:prepublish": "npm run compile && dotnet publish {{LanguageServerProjectPath}} -o ./LanguageServer",
            "compile": "tsc -b",
            "watch": "tsc -b -w"
          },
          "dependencies": {
            "vscode-languageclient": "^9.0.1"
          },
          "devDependencies": {
            "@types/vscode": "^1.82.1",
            "@types/node": "^18.14.6",
            "typescript": "^5.3.3"
          }
        }
        """);

    /// <summary>
    /// Generates the <c>language-configuration.json</c> file.
    /// </summary>
    /// <param name="output"></param>
    public void GenerateLanguageConfiguration(IndentedTextWriter output) => output.WriteCode($$"""
        {
            "comments": {
                // symbol used for single line comment. Remove this entry if your language does not support line comments
                "lineComment": "//",
                // symbols used for start and end a block comment. Remove this entry if your language does not support block comments
                "blockComment": [ "/*", "*/" ]
            },
            // symbols used as brackets
            "brackets": [
                ["{", "}"],
                ["[", "]"],
                ["(", ")"]
            ],
            // symbols that are auto closed when typing
            "autoClosingPairs": [
                ["{", "}"],
                ["[", "]"],
                ["(", ")"],
                ["\"", "\""],
                ["'", "'"]
            ],
            // symbols that can be used to surround a selection
            "surroundingPairs": [
                ["{", "}"],
                ["[", "]"],
                ["(", ")"],
                ["\"", "\""],
                ["'", "'"]
            ]
        }
        """);

    /// <summary>
    /// Generates the <c>tsconfig.json</c> file.
    /// </summary>
    /// <param name="output"></param>
    public void GenerateTypeScriptConfiguration(IndentedTextWriter output) => output.WriteCode($$"""
        {
            "compilerOptions": {
                "module": "commonjs",
                "target": "ES2022",
                "lib": ["es2022"],
                "outDir": "out",
                "rootDir": "src",
                "sourceMap": true,
                "forceConsistentCasingInFileNames": true,
                "strict": true
            },
            "include": ["src"],
            "exclude": ["node_modules", ".vscode-test"]
        }
        """);

    static string AsJson(object obj) => JsonSerializer.Serialize(obj);

    public static VscodeExtensionGenerator? FromConfig(
        Grammar grammar,
        Action<Diagnostic> diagnosticHandler,
        string workspaceRootDirectory,
        string languageServerAssemblyName,
        Configuration config)
    {
        if (!Configuration.CheckValuePresent(config.VscodeExtension, out _, diagnosticHandler))
            return null;

        if (!Configuration.CheckValuePresent(config.LanguageServer.ProjectPath, out _, diagnosticHandler))
            return null;

        var languageId = config.LanguageId ?? config.GetFallbackLanguageId(grammar);

        // The configuration paths are relative to workspace root (where the config file is located)
        // but we need the language server path relative to the extension directory
        string languageServerProjectPathFromExtensionDir =
            Path.GetRelativePath(
                Path.GetFullPath(config.VscodeExtension.OutputDirectory, workspaceRootDirectory),
                Path.GetFullPath(config.LanguageServer.ProjectPath, workspaceRootDirectory))
            .Replace(Path.DirectorySeparatorChar, '/'); // use '/' in JSON files

        return new VscodeExtensionGenerator {
            LanguageId = languageId.Transform(s => s.ToLowerInvariant()),
            LanguageDisplayName = config.LanguageDisplayName ?? languageId,
            LanguageFileExtensions = config.LanguageFileExtensions
                ?? [$".{languageId.Value.ToLowerInvariant()}"],
            ExtensionId = config.VscodeExtension.ExtensionId,
            ExtensionDisplayName = config.VscodeExtension.ExtensionDisplayName,
            LanguageClientName = config.VscodeExtension.ExtensionDisplayName,
            CommandCategoryName = config.VscodeExtension.ExtensionDisplayName,
            IncludeAstExplorerView = config.VscodeExtension.IncludeAstExplorerView,
            OutputDirectory = config.VscodeExtension.OutputDirectory,
            LanguageServerProjectPath = languageServerProjectPathFromExtensionDir,
            LanguageServerAssemblyName = languageServerAssemblyName,
        };
    }
}
