using Newtonsoft.Json;
using RevitCliBridge.Abstractions;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace RevitCliBridge
{
    /// <summary>
    /// Cache of the command schema derived from the registered handlers,
    /// shared by every CliHttpServer instance in the process. The ETag is
    /// computed once as a version-stamped content hash so clients detect
    /// upgrades even when the content hash happens to collide. Invalidate
    /// when handlers are registered at runtime (e.g. plugin loading).
    /// </summary>
    public static class CommandSchemaCache
    {
        private static List<CommandDef>? _cachedCommandDefs;
        private static string? _cachedSchemaEtag;
        private static readonly object _lock = new object();

        /// <summary>
        /// Returns the cached ETag for the command schema, building the cache
        /// on first access. Thread-safe.
        /// </summary>
        public static string GetEtag()
        {
            lock (_lock)
            {
                if (_cachedSchemaEtag != null) return _cachedSchemaEtag;
                GetDefs();
                return _cachedSchemaEtag!;
            }
        }

        /// <summary>
        /// Returns the cached command definitions, building them on first
        /// access. Thread-safe.
        /// </summary>
        public static List<CommandDef> GetDefs()
        {
            lock (_lock)
            {
                if (_cachedCommandDefs != null) return _cachedCommandDefs;

                var defs = new List<CommandDef>();
                foreach (var handler in CommandRouter.GetAllHandlers())
                {
                    defs.Add(BuildDef(handler));
                }

                var json = JsonConvert.SerializeObject(defs, Formatting.None);
                _cachedSchemaEtag = $"{BridgeVersion}:{defs.Count}:{ComputeEtag(json)}";

                _cachedCommandDefs = defs;
                return defs;
            }
        }

        /// <summary>
        /// Drops the cached schema. During initial handler discovery the
        /// cache is not yet built, so invalidation is a no-op then.
        /// </summary>
        public static void Invalidate()
        {
            lock (_lock)
            {
                _cachedCommandDefs = null;
                _cachedSchemaEtag = null;
            }
        }

        /// <summary>
        /// Bridge version derived from the assembly FileVersion (Nice3point
        /// style, e.g. "2022.1.5").
        /// </summary>
        public static string BridgeVersion
        {
            get
            {
                var v = typeof(CommandSchemaCache).Assembly
                    .GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version;
                if (string.IsNullOrEmpty(v)) return "0.0.0";
                var parts = v!.Split('.');
                return parts.Length >= 3 ? $"{parts[0]}.{parts[1]}.{parts[2]}" : v!;
            }
        }

        public static CommandDef BuildDef(IBridgeCommand handler)
        {
            return new CommandDef
            {
                Name = handler.CommandName,
                Version = handler.Version,
                Description = handler.Description,
                Category = handler.Category,
                Aliases = handler.Aliases.Length > 0 ? handler.Aliases : null,
                SupportsDryRun = handler.SupportsDryRun,
                Parameters = handler.Parameters.Length > 0 ? handler.Parameters : null,
                Examples = handler.Examples.Length > 0 ? handler.Examples : null
            };
        }

        /// <summary>
        /// Version-stamped ETag for a single command definition, format
        /// "{bridgeVersion}:{contentHash}" — stable across requests for the
        /// same command content, changes on bridge upgrade.
        /// </summary>
        public static string ComputeCommandEtag(CommandDef commandDef)
        {
            var json = JsonConvert.SerializeObject(commandDef, Formatting.None);
            return $"{BridgeVersion}:{ComputeEtag(json)}";
        }

        public static string ComputeEtag(string content)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(content));
                return Convert.ToBase64String(hash).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            }
        }
    }
}
