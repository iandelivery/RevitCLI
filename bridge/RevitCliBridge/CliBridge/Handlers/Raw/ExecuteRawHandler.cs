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

namespace RevitCliBridge.Handlers
{
    /// <summary>
    /// Executes raw C# or Python code against the Revit API.
    /// Disabled by default — requires explicit opt-in via configuration.
    /// Only accessible from localhost for security.
    /// </summary>
    public class ExecuteRawHandler : BridgeCommandBase
    {
        public override string CommandName => "execute_raw";
        public override string Description => "Execute raw C# or Python code (requires server-side enablement)";
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
                        { "doc", "Autodesk.Revit.DB.Document — the active document (null if no doc open)" },
                        { "uidoc", "Autodesk.Revit.UI.UIDocument — the active UI document (Python only)" }
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
                    ReturnConvention = "C#: use 'return <value>;' to return a result. Python: use print() for output.",
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
                        },
                        {
                            "python",
                            "IronPython is loaded at runtime if available. Variables 'app', 'doc', 'uidoc' are injected into scope. "
                            + "Use print() for output. No need for explicit return. "
                            + "For model changes: t = Transaction(doc, 'name'); t.Start(); ... t.Commit()"
                        }
                    }
                }
            },
            new CommandParamSchema
            {
                Name = "language",
                Type = "string",
                Required = false,
                Description = "Script language: csharp | python",
                Default = "csharp",
                EnumValues = new[] { "csharp", "python" },
                ShortFlag = "l"
            }
        };

        public override string[] Examples => new[]
        {
            // C# examples
            "{ \"command\": \"execute_raw\", \"parameters\": { \"code\": \"return doc.Title;\" } }",
            "{ \"command\": \"execute_raw\", \"parameters\": { \"code\": \"return new FilteredElementCollector(doc).OfClass(typeof(Wall)).Count();\" } }",
            "{ \"command\": \"execute_raw\", \"parameters\": { \"code\": \"var walls = new FilteredElementCollector(doc).OfClass(typeof(Wall)).Cast<Wall>(); return walls.Select(w => new { id = w.Id.IntegerValue, name = w.Name }).ToList();\" } }",
            "{ \"command\": \"execute_raw\", \"parameters\": { \"code\": \"using (var t = new Transaction(doc, \\\"Delete\\\")) { t.Start(); doc.Delete(new ElementId(123)); t.Commit(); return \\\"deleted\\\"; }\" } }",
            // Python examples
            "{ \"command\": \"execute_raw\", \"parameters\": { \"code\": \"print(doc.Title)\", \"language\": \"python\" } }",
            "{ \"command\": \"execute_raw\", \"parameters\": { \"code\": \"from Autodesk.Revit.DB import FilteredElementCollector, Wall\\ncount = FilteredElementCollector(doc).OfClass(Wall).GetElementCount()\\nprint(str(count) + ' walls found')\", \"language\": \"python\" } }",
            // Dry-run
            "{ \"command\": \"execute_raw\", \"parameters\": { \"code\": \"return doc.Title;\" }, \"dry_run\": true }"
        };

        protected override string Execute(UIApplication app, QueuedCommand cmd)
        {
            // Security gate: check if raw execution is enabled
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

            var code = codeObj.ToString()!;
            var language = parameters.TryGetValue("language", out var langObj)
                ? (langObj?.ToString() ?? "csharp").ToLowerInvariant()
                : "csharp";

            if (language != "csharp" && language != "python")
            {
                return CommandResponse.Error(cmd.TaskId,
                    $"Unsupported language: {language}. Supported: csharp, python").ToJson();
            }

            try
            {
                // In dry-run mode, just validate and report
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

                // Execute based on language
                return language == "python"
                    ? ExecutePython(app, cmd, code)
                    : ExecuteCSharp(app, cmd, code);
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
                // Wrap user code in a method signature for compilation
                var scriptClass = BuildCSharpScriptClass(code);
                var assembly = CompileCSharp(scriptClass);

                if (assembly == null)
                    return CommandResponse.Error(cmd.TaskId, "C# compilation failed. Check syntax.").ToJson();

                var scriptType = assembly.GetType("ScriptHost");
                var executeMethod = scriptType?.GetMethod("Execute");

                if (executeMethod == null)
                    return CommandResponse.Error(cmd.TaskId, "Script method 'Execute' not found.").ToJson();

                // Invoke: Execute(UIApplication app, Document doc)
                var doc = app.ActiveUIDocument?.Document;
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
        /// Execute Python code using IronPython (if available).
        /// IronPython DLLs are loaded at runtime — no hard dependency.
        /// </summary>
        private string ExecutePython(UIApplication app, QueuedCommand cmd, string code)
        {
            try
            {
                // Try to load IronPython at runtime
                var ironPythonDll = FindIronPythonAssembly();
                if (ironPythonDll == null)
                {
                    return CommandResponse.Error(cmd.TaskId,
                        "IronPython is not installed. Place IronPython.dll and IronPython.Modules.dll " +
                        "in the Revit add-ins directory to enable Python support.").ToJson();
                }

                var pythonEngine = CreatePythonEngine(ironPythonDll);
                if (pythonEngine == null)
                {
                    return CommandResponse.Error(cmd.TaskId,
                        "Failed to initialize IronPython engine.").ToJson();
                }

                // Set up scope with Revit variables
                var doc = app.ActiveUIDocument?.Document;
                pythonEngine.SetVariable("app", app);
                pythonEngine.SetVariable("doc", doc);
                pythonEngine.SetVariable("uidoc", app.ActiveUIDocument);

                // Execute the script
                var output = new StringBuilder();
                pythonEngine.RedirectOutput(output);
                pythonEngine.Execute(code);

                var resultStr = output.ToString().Trim();

                return CommandResponse.Success(cmd.TaskId, new
                {
                    language = "python",
                    output = string.IsNullOrEmpty(resultStr) ? "(no output)" : resultStr
                }).ToJson();
            }
            catch (Exception ex)
            {
                var inner = ex is TargetInvocationException tie ? tie.InnerException : ex;
                return CommandResponse.Error(cmd.TaskId,
                    $"Python execution error: {inner?.Message ?? ex.Message}",
                    inner?.StackTrace ?? ex.StackTrace).ToJson();
            }
        }

        /// <summary>
        /// Build a compilable C# class wrapping the user's code snippet.
        /// The user code can reference `app` (UIApplication) and `doc` (Document).
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

public static class ScriptHost
{{
    public static object Execute(UIApplication app, Document doc)
    {{
        {userCode}
        return null;
    }}
}}
";
        }

        /// <summary>
        /// Compile C# source code using Roslyn-less approach (CodeDOM for .NET Framework).
        /// </summary>
        private static Assembly? CompileCSharp(string source)
        {
            try
            {
                // Use CodeDOM compiler available in .NET Framework
                var compiler = new Microsoft.CSharp.CSharpCodeProvider();
                var parameters = new System.CodeDom.Compiler.CompilerParameters
                {
                    GenerateInMemory = true,
                    GenerateExecutable = false,
                    IncludeDebugInformation = false
                };

                // Add Revit API references
                var revitDir = System.IO.Path.GetDirectoryName(
                    typeof(Autodesk.Revit.DB.Document).Assembly.Location);

                parameters.ReferencedAssemblies.Add("System.dll");
                parameters.ReferencedAssemblies.Add("System.Core.dll");
                parameters.ReferencedAssemblies.Add("System.Linq.dll");
                parameters.ReferencedAssemblies.Add(typeof(Autodesk.Revit.DB.Document).Assembly.Location);
                parameters.ReferencedAssemblies.Add(typeof(Autodesk.Revit.UI.UIApplication).Assembly.Location);
                parameters.ReferencedAssemblies.Add(typeof(Newtonsoft.Json.JsonConvert).Assembly.Location);

                var result = compiler.CompileAssemblyFromSource(parameters, source);

                if (result.Errors.HasErrors)
                {
                    var errors = string.Join("\n",
                        result.Errors.Cast<System.CodeDom.Compiler.CompilerError>()
                            .Where(e => !e.IsWarning)
                            .Select(e => $"Line {e.Line}: {e.ErrorText}"));
                    CliLogger.Error($"C# compilation errors:\n{errors}");
                    return null;
                }

                return result.CompiledAssembly;
            }
            catch (Exception ex)
            {
                CliLogger.Error($"C# compilation failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Find IronPython assemblies in the Revit add-ins directory or PATH.
        /// Returns null if not found — Python support is optional.
        /// </summary>
        private static string? FindIronPythonAssembly()
        {
            var searchPaths = new List<string>();

            // 1. Look in the same directory as the executing assembly
            var asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (asmDir != null) searchPaths.Add(asmDir);

            // 2. Look in a "python" subdirectory
            if (asmDir != null) searchPaths.Add(Path.Combine(asmDir, "python"));

            // 3. Look in common Revit add-ins paths
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            searchPaths.Add(Path.Combine(appData, "Autodesk", "Revit", "Addins"));

            foreach (var dir in searchPaths.Where(Directory.Exists))
            {
                var dll = Directory.GetFiles(dir, "IronPython.dll", SearchOption.TopDirectoryOnly)
                    .FirstOrDefault();
                if (dll != null) return dll;
            }

            // 4. Try GAC
            try
            {
                Assembly.Load("IronPython, Version=2.7.0.0, Culture=neutral, PublicKeyToken=7f709c5b713576e1");
                return "IronPython";
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Create a dynamic IronPython engine wrapper using reflection.
        /// Avoids hard dependency on IronPython — loads at runtime if available.
        /// </summary>
        private static IPythonEngine? CreatePythonEngine(string dllPath)
        {
            try
            {
                Assembly ironPythonAsm;
                if (dllPath == "IronPython")
                    ironPythonAsm = Assembly.Load("IronPython");
                else
                    ironPythonAsm = Assembly.LoadFrom(dllPath);

                // Also load IronPython.Modules if available
                try
                {
                    var modulesPath = Path.Combine(Path.GetDirectoryName(dllPath)!, "IronPython.Modules.dll");
                    if (File.Exists(modulesPath))
                        Assembly.LoadFrom(modulesPath);
                }
                catch { }

                return new IronPythonEngineProxy(ironPythonAsm);
            }
            catch (Exception ex)
            {
                CliLogger.Error($"Failed to load IronPython: {ex.Message}");
                return null;
            }
        }

        private static bool IsRawExecutionEnabled()
        {
            return CliBridgeConfigLoader.Config.AllowRawExecution;
        }

        /// <summary>
        /// Interface for Python engine abstraction (avoid hard IronPython dependency).
        /// </summary>
        private interface IPythonEngine
        {
            void SetVariable(string name, object value);
            void RedirectOutput(StringBuilder output);
            string Execute(string code);
        }

        /// <summary>
        /// IronPython engine proxy using reflection — no compile-time dependency.
        /// </summary>
        private class IronPythonEngineProxy : IPythonEngine
        {
            private readonly object _engine;
            private readonly object _scope;
            private readonly MethodInfo _executeMethod;
            private readonly MethodInfo _setVariableMethod;

            public IronPythonEngineProxy(Assembly ironPythonAssembly)
            {
                var pythonType = ironPythonAssembly.GetType("IronPython.Hosting.Python");
                var createEngineMethod = pythonType.GetMethod("CreateEngine");
                _engine = createEngineMethod.Invoke(null, null);

                var scopeType = ironPythonAssembly.GetType("IronPython.Runtime.Scope");
                _scope = Activator.CreateInstance(scopeType);

                var engineType = _engine.GetType();
                _executeMethod = engineType.GetMethod("Execute",
                    new[] { typeof(string), _scope.GetType() });
                _setVariableMethod = engineType.GetMethod("SetVariable",
                    new[] { typeof(string), typeof(object) });

                if (_executeMethod == null || _setVariableMethod == null)
                    throw new InvalidOperationException("IronPython engine methods not found.");
            }

            public void SetVariable(string name, object value)
            {
                _setVariableMethod.Invoke(_engine, new object[] { name, value });
            }

            public void RedirectOutput(StringBuilder output)
            {
                // Redirect IronPython stdout to our StringBuilder
                var writerType = typeof(StreamWriter);
                var memStream = new MemoryStream();
                var writer = new StreamWriter(memStream) { AutoFlush = true };

                try
                {
                    var runtimeType = _engine.GetType().Assembly.GetType("IronPython.Runtime.PythonContext");
                    if (runtimeType != null)
                    {
                        // Set sys.stdout redirect
                        SetVariable("__output_stream", writer);
                        try
                        {
                            _executeMethod.Invoke(_engine, new object[]
                            {
                                "import sys\nsys.stdout = __output_stream",
                                _scope
                            });
                        }
                        catch { }
                    }
                }
                catch { }
            }

            public string Execute(string code)
            {
                var result = _executeMethod.Invoke(_engine, new object[] { code, _scope });
                return result?.ToString() ?? string.Empty;
            }
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
