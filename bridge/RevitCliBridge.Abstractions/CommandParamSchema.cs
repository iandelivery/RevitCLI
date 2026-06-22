using Newtonsoft.Json;

namespace RevitCliBridge.Abstractions
{
    /// <summary>
    /// Describes a single parameter of a bridge command.
    /// Used by the schema discovery endpoint for agent self-correction.
    /// </summary>
    public class CommandParamSchema
    {
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Parameter type: "string", "int", "double", "bool", "int[]", "string[]", "object".
        /// </summary>
        [JsonProperty("type")]
        public string Type { get; set; } = "string";

        [JsonProperty("required")]
        public bool Required { get; set; }

        [JsonProperty("description", NullValueHandling = NullValueHandling.Ignore)]
        public string? Description { get; set; }

        [JsonProperty("default", NullValueHandling = NullValueHandling.Ignore)]
        public object? Default { get; set; }

        /// <summary>
        /// Short flag alias (e.g. "l" for --level-id).
        /// </summary>
        [JsonProperty("short_flag", NullValueHandling = NullValueHandling.Ignore)]
        public string? ShortFlag { get; set; }

        /// <summary>
        /// Allowed values for enum-like parameters.
        /// </summary>
        [JsonProperty("enum_values", NullValueHandling = NullValueHandling.Ignore)]
        public string[]? EnumValues { get; set; }

        /// <summary>
        /// Sub-properties when Type is "object".
        /// </summary>
        [JsonProperty("properties", NullValueHandling = NullValueHandling.Ignore)]
        public CommandParamSchema[]? Properties { get; set; }

        /// <summary>
        /// Opaque context metadata for the parameter. The shape depends on the
        /// command — e.g. a "code" parameter may include available_variables,
        /// namespaces, and language notes. Serialized as-is to JSON so agents
        /// can discover parameter-specific context without the abstractions
        /// layer knowing the details.
        /// </summary>
        [JsonProperty("context", NullValueHandling = NullValueHandling.Ignore)]
        public object? Context { get; set; }
    }
}
