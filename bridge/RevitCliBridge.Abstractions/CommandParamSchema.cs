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

        /// <summary>
        /// Marks the parameter as deprecated. Agents and clients should prefer
        /// the replacement indicated in <see cref="DeprecationMessage"/>.
        /// The parameter still accepts input for backward compatibility.
        /// </summary>
        [JsonProperty("deprecated")]
        public bool Deprecated { get; set; }

        /// <summary>
        /// Human-readable guidance shown when <see cref="Deprecated"/> is true.
        /// Should name the replacement parameter and the version where removal
        /// is planned, e.g. "Use 'level_id' instead; removed in v3."
        /// </summary>
        [JsonProperty("deprecation_message", NullValueHandling = NullValueHandling.Ignore)]
        public string? DeprecationMessage { get; set; }

        /// <summary>
        /// Marks the parameter as sensitive (passwords, tokens, paths under
        /// NDA). The bridge redacts sensitive values from structured logs and
        /// error messages; the value itself is still passed to the handler
        /// unchanged. Defaults to <c>false</c>.
        /// </summary>
        [JsonProperty("sensitive")]
        public bool Sensitive { get; set; }
    }
}
