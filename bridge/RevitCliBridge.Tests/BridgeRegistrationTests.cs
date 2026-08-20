using System;
using System.Linq;
using RevitCliBridge.Abstractions;
using Xunit;

namespace RevitCliBridge.Tests
{
    /// <summary>
    /// Tests for BridgeRegistration — the reflection facade that lets plugin
    /// authors register commands using only the Abstractions package.
    /// Covers key generation rules and the bridge-not-loaded error path.
    /// (The test host links bridge sources into its own assembly, so no
    /// assembly named "RevitCliBridge" is loaded here — which is exactly the
    /// precondition for the not-loaded tests.)
    /// </summary>
    public class BridgeRegistrationTests
    {
        private sealed class FakeCommand : IBridgeCommand
        {
            public string CommandName { get; set; } = "fake_command";
            public string Version { get; set; } = "v1";
            public string Description => "fake";
            public string Category => "Test";
            public bool SupportsDryRun => false;
            public string[] Aliases { get; set; } = Array.Empty<string>();
            public string[] Examples => Array.Empty<string>();
            public CommandParamSchema[] Parameters => Array.Empty<CommandParamSchema>();
            public string Handle(object uiApplication, QueuedCommand cmd) => string.Empty;
        }

        [Fact]
        public void GetRegistrationKeys_DefaultVersion_IncludesVersionedBareAndAliases()
        {
            var cmd = new FakeCommand
            {
                CommandName = "create_thing",
                Aliases = new[] { "thing_create" }
            };

            var keys = BridgeRegistration.GetRegistrationKeys(cmd);

            Assert.Equal(new[] { "create_thing@v1", "create_thing", "thing_create@v1", "thing_create" }, keys);
        }

        [Fact]
        public void GetRegistrationKeys_NonDefaultVersion_OmitsBareNames()
        {
            var cmd = new FakeCommand
            {
                CommandName = "create_thing",
                Version = "v2",
                Aliases = new[] { "thing_create" }
            };

            var keys = BridgeRegistration.GetRegistrationKeys(cmd);

            Assert.Equal(new[] { "create_thing@v2", "thing_create@v2" }, keys);
        }

        [Fact]
        public void GetRegistrationKeys_NoAliases_OnlyPrimaryKeys()
        {
            var keys = BridgeRegistration.GetRegistrationKeys(new FakeCommand());

            Assert.Equal(new[] { "fake_command@v1", "fake_command" }, keys);
        }

        [Fact]
        public void GetRegistrationKeys_SkipsBlankAliases()
        {
            var cmd = new FakeCommand { Aliases = new[] { "", "  ", "valid_alias" } };

            var keys = BridgeRegistration.GetRegistrationKeys(cmd);

            Assert.Equal(new[] { "fake_command@v1", "fake_command", "valid_alias@v1", "valid_alias" }, keys);
        }

        [Fact]
        public void GetRegistrationKeys_NullCommand_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => BridgeRegistration.GetRegistrationKeys(null!));
        }

        [Fact]
        public void GetRegistrationKeys_EmptyCommandName_Throws()
        {
            var cmd = new FakeCommand { CommandName = " " };

            Assert.Throws<ArgumentException>(() => BridgeRegistration.GetRegistrationKeys(cmd));
        }

        [Fact]
        public void Register_WithoutLoadedBridge_ThrowsWithGuidance()
        {
            var ex = Assert.Throws<InvalidOperationException>(
                () => BridgeRegistration.Register(new FakeCommand()));

            Assert.Contains("RevitCliBridge assembly is not loaded", ex.Message);
        }

        [Fact]
        public void Register_ExplicitKeyWithoutLoadedBridge_Throws()
        {
            Assert.Throws<InvalidOperationException>(
                () => BridgeRegistration.Register("create_thing@v1", new FakeCommand()));
        }

        [Fact]
        public void Register_NullCommand_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => BridgeRegistration.Register(null!));
        }

        [Fact]
        public void Register_ExplicitKeyNulls_Throw()
        {
            Assert.Throws<ArgumentException>(() => BridgeRegistration.Register("", new FakeCommand()));
            Assert.Throws<ArgumentNullException>(() => BridgeRegistration.Register("k", null!));
        }

        [Fact]
        public void Register_CommandWithEmptyName_ThrowsBeforeBridgeLookup()
        {
            // Validation must fire before the bridge-not-loaded error so
            // plugin authors get the precise diagnostic.
            var cmd = new FakeCommand { CommandName = "" };
            Assert.Throws<ArgumentException>(() => BridgeRegistration.Register(cmd));
        }
    }
}
