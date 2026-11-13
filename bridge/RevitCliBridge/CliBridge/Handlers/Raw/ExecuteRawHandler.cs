using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCliBridge.Abstractions;
using RevitCliBridge.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace RevitCliBridge.Handlers.Raw
{
    /// <summary>
    /// Executes raw C# code against the Revit API.
    /// Enabled by default; toggled at runtime via the raw-mode command.
    /// Only accessible from localhost for security.
    /// </summary>
    public class ExecuteRawHandler : BridgeCommandBase
    {
        public override string CommandName => "execute_raw";
        public override string Description => "Execute raw C# code (requires server-side enablement)";
        public override string Category => "Raw";
        public override bool SupportsDryRun => true;
        public override string[] Aliases => new[] { "raw_exec", "eval" };

        public override CommandParamSchema[] Parameters => new[]
        {
            new CommandParamSchema
            {
                Name = "code",
                Type = "string",
                Required = true,
                Description = "Script code to execute",
                ShortFlag = "c",
                Context = new ScriptParamContext
                {
                    AvailableVariables = new Dictionary<string, string>
                    {
                        { "app", "Autodesk.Revit.UI.UIApplication — the Revit application instance" },
                        { "doc", "Autodesk.Revit.DB.Document — the active document (null if no doc open)" }
                    },
                    AvailableNamespaces = new[]
                    {
                        "System",
                        "System.Collections.Generic",
                        "System.Linq",
                        "Autodesk.Revit.DB",
                        "Autodesk.Revit.UI",
                        "Autodesk.Revit.DB.Structure"
                    },
                    ReturnConvention = "Use 'return <value>;' to return a result.",
                    Prerequisites = new[]
                    {
                        "Set 'allow_raw_execution: true' in cli_bridge_setting.json to enable this command",
                        "Only accessible from localhost for security",
                        "Wrap modifications in a Transaction when changing the Revit model",
                        "Use --dry-run to validate code without committing changes"
                    },
                    LanguageNotes = new Dictionary<string, string>
                    {
                        {
                            "csharp",
                            "Code is compiled as the body of: public static object Execute(UIApplication app, Document doc) { <your_code> } "
                            + "You can directly use 'app' and 'doc'. Use 'return' to send back a result. "
                            + "For model changes, wrap in: using (var t = new Transaction(doc, \"name\")) { t.Start(); ... t.Commit(); }"
                        }
                    }
                }
            },
            new CommandParamSchema
            {
                Name = "language",
                Type = "string",
                Required = false,
                Description = "Script language (only csharp is supported)",
                Default = "csharp",
                EnumValues = new[] { "csharp" },
                ShortFlag = "l"
            }
        };

        public override string[] Examples => new[]
        {
            "{ \"command\": \"execute_raw\", \"parameters\": { \"code\": \"return doc.Title;\" } }",
            "{ \"command\": \"execute_raw\", \"parameters\": { \"code\": \"return new FilteredElementCollector(doc).OfClass(typeof(Wall)).Count();\" } }",
            "{ \"command\": \"execute_raw\", \"parameters\": { \"code\": \"var walls = new FilteredElementCollector(doc).OfClass(typeof(Wall)).Cast<Wall>(); return walls.Select(w => new { id = w.Id.IntegerValue, name = w.Name }).ToList();\" } }",
            "{ \"command\": \"execute_raw\", \"parameters\": { \"code\": \"using (var t = new Transaction(doc, \\\"Delete\\\")) { t.Start(); doc.Delete(new ElementId(123)); t.Commit(); return \\\"deleted\\\"; }\" } }",
            "{ \"command\": \"execute_raw\", \"parameters\": { \"code\": \"return doc.Title;\" }, \"dry_run\": true }"
        };

        protected override string Execute(UIApplication app, QueuedCommand cmd)
        {
            if (!IsRawExecutionEnabled())
            {
                return CommandResponse.Error(cmd.TaskId,
                    "Raw execution is disabled. Set 'allow_raw_execution: true' in cli_bridge_setting.json to enable.").ToJson();
            }

            var parameters = cmd.Parameters as Dictionary<string, object> ?? new Dictionary<string, object>();

            if (!parameters.TryGetValue("code", out var codeObj) || string.IsNullOrEmpty(codeObj?.ToString()))
            {
                return CommandResponse.Error(cmd.TaskId, "Parameter 'code' is required.").ToJson();
            }

            var code = codeObj!.ToString()!;
            var language = parameters.TryGetValue("language", out var langObj)
                ? (langObj?.ToString() ?? "csharp").ToLowerInvariant()
                : "csharp";

            if (language != "csharp")
            {
                return CommandResponse.Error(cmd.TaskId,
                    $"Unsupported language: {language}. Only csharp is supported (Python execution is not available).").ToJson();
            }

            try
            {
                if (cmd.DryRun)
                {
                    return CommandResponse.Success(cmd.TaskId, new
                    {
                        dry_run = true,
                        language,
                        code_length = code.Length,
                        message = "[DRY-RUN] Code would be compiled and executed. No changes committed."
                    }).ToJson();
                }

                return ExecuteCSharp(app, cmd, code);
            }
            catch (Exception ex)
            {
                return CommandResponse.Error(cmd.TaskId,
                    $"Raw execution failed: {ex.Message}", ex.ToString()).ToJson();
            }
        }

        /// <summary>
        /// Execute C# code by compiling it as a script method at runtime.
        /// Uses Microsoft.CSharp compiler for .NET Framework compatibility.
        /// </summary>
        private string ExecuteCSharp(UIApplication app, QueuedCommand cmd, string code)
        {
            try
            {
                var scriptClass = BuildCSharpScriptClass(code);
                var (assembly, compileErrors) = CompileCSharp(scriptClass);

                if (assembly == null)
                    return CommandResponse.Error(cmd.TaskId,
                        $"C# compilation failed:\n{compileErrors}").ToJson();

                var scriptType = assembly.GetType("ScriptHost");
                var executeMethod = scriptType?.GetMethod("Execute");

                if (executeMethod == null)
                    return CommandResponse.Error(cmd.TaskId, "Script method 'Execute' not found.").ToJson();

                var doc = app.ActiveUIDocument?.Document;
                if (doc is null)
                {
                    return CommandResponse.Error(cmd.TaskId, "Active document not found.").ToJson();
                }
                
                var result = executeMethod.Invoke(null, new object[] { app, doc });

                var resultStr = result?.ToString() ?? "null";

                return CommandResponse.Success(cmd.TaskId, new
                {
                    language = "csharp",
                    result = resultStr,
                    output = resultStr
                }).ToJson();
            }
            catch (Exception ex)
            {
                var inner = ex is System.Reflection.TargetInvocationException tie ? tie.InnerException : ex;
                return CommandResponse.Error(cmd.TaskId,
                    $"C# execution error: {inner?.Message ?? ex.Message}",
                    inner?.StackTrace ?? ex.StackTrace).ToJson();
            }
        }

        /// <summary>
        /// Build a compilable C# class wrapping the user's code snippet.
        /// The user code can reference `app` (UIApplication), `doc` (Document),
        /// and bridge helpers such as CliFailurePreprocessor.
        /// If the user code contains a `return` statement, the trailing
        /// `return null;` is omitted to avoid unreachable-code warnings/errors.
        /// </summary>
        private static string BuildCSharpScriptClass(string userCode)
        {
            return $@"
using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB.Structure;
using System.Collections.Generic;
using System.Linq;
using RevitCliBridge.Handlers;

public static class ScriptHost
{{
    public static object Execute(UIApplication app, Document doc)
    {{
        {userCode}
        // return fallback
        return null;
    }}
}}
";
        }

        /// <summary>
        /// Compile C# source code using Roslyn-less approach (CodeDOM for .NET Framework).
        /// Returns a tuple of (assembly, errorMessage). If compilation succeeds,
        /// errorMessage is empty. If it fails, assembly is null and errorMessage
        /// contains the compilation errors.
        /// </summary>
        private static (Assembly?, string) CompileCSharp(string source)
        {
            try
            {
                var compiler = new Microsoft.CSharp.CSharpCodeProvider();
                var parameters = new System.CodeDom.Compiler.CompilerParameters
                {
                    GenerateInMemory = true,
                    GenerateExecutable = false,
                    IncludeDebugInformation = false,
                    CompilerOptions = "/nowarn:CS0162,CS0219"
                };

                var revitDir = System.IO.Path.GetDirectoryName(
                    typeof(Autodesk.Revit.DB.Document).Assembly.Location);

                parameters.ReferencedAssemblies.Add("System.dll");
                parameters.ReferencedAssemblies.Add("System.Core.dll");
                parameters.ReferencedAssemblies.Add("System.Linq.dll");
                parameters.ReferencedAssemblies.Add(typeof(Autodesk.Revit.DB.Document).Assembly.Location);
                parameters.ReferencedAssemblies.Add(typeof(Autodesk.Revit.UI.UIApplication).Assembly.Location);
                parameters.ReferencedAssemblies.Add(typeof(Newtonsoft.Json.JsonConvert).Assembly.Location);
                parameters.ReferencedAssemblies.Add(typeof(CliFailurePreprocessor).Assembly.Location);

                var result = compiler.CompileAssemblyFromSource(parameters, source);

                if (result.Errors.HasErrors)
                {
                    var errors = string.Join("\n",
                        result.Errors.Cast<System.CodeDom.Compiler.CompilerError>()
                            .Where(e => !e.IsWarning)
                            .Select(e => $"Line {e.Line}: {e.ErrorText}"));
                    CliLogger.Error($"C# compilation errors:\n{errors}");
                    return (null, errors);
                }

                return (result.CompiledAssembly, "");
            }
            catch (Exception ex)
            {
                CliLogger.Error($"C# compilation failed: {ex.Message}");
                return (null, ex.Message);
            }
        }

        private static bool IsRawExecutionEnabled()
        {
            return CliBridgeConfigLoader.Config.AllowRawExecution;
        }

        /// <summary>
        /// Script execution context metadata. Owned by ExecuteRawHandler —
        /// not in the abstractions layer because this shape is specific to
        /// script/code parameters. Other commands may define their own
        /// context shapes and assign them to <see cref="CommandParamSchema.Context"/>
        /// as an opaque <c>object</c>.
        /// </summary>
        private class ScriptParamContext
        {
            public Dictionary<string, string>? AvailableVariables { get; set; }
            public string[]? AvailableNamespaces { get; set; }
            public string? ReturnConvention { get; set; }
            public string[]? Prerequisites { get; set; }
            public Dictionary<string, string>? LanguageNotes { get; set; }
        }
    }
}
