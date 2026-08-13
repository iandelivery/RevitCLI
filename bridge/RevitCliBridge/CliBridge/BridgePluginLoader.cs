using RevitCliBridge.Abstractions;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;

namespace RevitCliBridge
{
    public static class BridgePluginLoader
    {
        /// <summary>
        /// Loads all plugin DLLs from <paramref name="pluginDir"/>. Each DLL
        /// is checked for an Authenticode signature unless
        /// <paramref name="allowUnsigned"/> is true. Signed DLLs must have a
        /// publisher matching <paramref name="trustedPublishers"/> (when that
        /// list is non-empty).
        /// </summary>
        public static void LoadPlugins(string pluginDir, bool allowUnsigned = false, string[]? trustedPublishers = null)
        {
            if (!Directory.Exists(pluginDir))
                return;

            foreach (var dll in Directory.GetFiles(pluginDir, "*.dll"))
            {
                LoadPluginDll(dll, allowUnsigned, trustedPublishers);
            }
        }

        private static void LoadPluginDll(string dllPath, bool allowUnsigned, string[]? trustedPublishers)
        {
            try
            {
                // Verify signature before loading the assembly.
                var (isSigned, publisher) = CheckSignature(dllPath);

                if (!isSigned)
                {
                    if (!allowUnsigned)
                    {
                        CliLogger.Warn("plugin_rejected_unsigned",
                            ("dll", Path.GetFileName(dllPath)),
                            ("reason", "unsigned and allow_unsigned_plugins is false"));
                        return;
                    }
                    CliLogger.Info("plugin_loaded_unsigned",
                        ("dll", Path.GetFileName(dllPath)),
                        ("reason", "allow_unsigned_plugins is true"));
                }
                else
                {
                    // Signed: check publisher against whitelist if configured.
                    if (trustedPublishers is not null && trustedPublishers.Length > 0)
                    {
                        bool trusted = trustedPublishers.Any(tp =>
                            publisher is not null && publisher.IndexOf(tp, StringComparison.OrdinalIgnoreCase) >= 0);

                        if (!trusted)
                        {
                            CliLogger.Warn("plugin_rejected_untrusted_publisher",
                                ("dll", Path.GetFileName(dllPath)),
                                ("publisher", publisher ?? "unknown"));
                            return;
                        }
                    }

                    CliLogger.Info("plugin_loaded_signed",
                        ("dll", Path.GetFileName(dllPath)),
                        ("publisher", publisher ?? "unknown"));
                }

                var assembly = Assembly.LoadFrom(dllPath);

                var handlerTypes = assembly.GetTypes()
                    .Where(t => typeof(IBridgeCommand).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface);

                foreach (var handlerType in handlerTypes)
                {
                    var cmd = (IBridgeCommand)Activator.CreateInstance(handlerType);
                    CommandRouter.Register($"{cmd.CommandName}@{cmd.Version}", cmd);

                    // Also register bare name for default version.
                    if (cmd.Version == CommandNameResolver.DefaultVersion)
                    {
                        CommandRouter.Register(cmd.CommandName, cmd);
                    }
                }
            }
            catch (Exception ex)
            {
                CliLogger.Error("plugin_load_failed",
                    ("dll", Path.GetFileName(dllPath)),
                    ("error", ex.Message));
            }
        }

        /// <summary>
        /// Checks whether <paramref name="dllPath"/> has an Authenticode
        /// signature and extracts the publisher (certificate subject CN).
        /// Returns (isSigned, publisher) — publisher is null when unsigned.
        ///
        /// Note: this checks signature presence, not chain validity. Full
        /// WinVerifyTrust chain validation would require P/Invoke and is left
        /// for a future hardening pass if needed.
        /// </summary>
        private static (bool isSigned, string? publisher) CheckSignature(string dllPath)
        {
            try
            {
                // X509Certificate.CreateFromSignedFile throws if the file has
                // no Authenticode signature.
                var cert = X509Certificate.CreateFromSignedFile(dllPath);
                var subject = cert.Subject ?? string.Empty;

                // Extract CN= value from the subject string.
                // Subject format: "CN=Publisher Name, O=Org, ..."
                string? cn = null;
                var parts = subject.Split(',');
                foreach (var part in parts)
                {
                    var trimmed = part.Trim();
                    if (trimmed.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
                    {
                        cn = trimmed.Substring(3);
                        break;
                    }
                }

                return (true, cn ?? subject);
            }
            catch
            {
                return (false, null);
            }
        }
    }
}
