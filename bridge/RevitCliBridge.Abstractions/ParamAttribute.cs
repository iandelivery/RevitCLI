using System;

namespace RevitCliBridge.Abstractions
{
    /// <summary>
    /// Marks a property on a parameter POCO as a bindable command parameter.
    /// Used by <see cref="ParameterBinder"/> to populate the POCO from the
    /// command's <c>parameters</c> dictionary.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public sealed class ParamAttribute : Attribute
    {
        /// <summary>
        /// The parameter name as it appears in the command's JSON parameters
        /// (e.g. "element_id", "start_x"). Defaults to the property name
        /// converted to snake_case.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Whether the parameter is required. When true and the value is
        /// missing or null, <see cref="ParameterBinder.Bind{T}"/> throws
        /// <see cref="MissingParameterException"/>.
        /// </summary>
        public bool Required { get; set; }

        /// <summary>
        /// Default value used when the parameter is absent. Applied before
        /// the required check, so a required parameter with a default is
        /// always satisfied. Must be assignable to the property type.
        /// </summary>
        public object? Default { get; set; }

        public ParamAttribute(string name = "")
        {
            Name = name ?? string.Empty;
        }
    }

    /// <summary>
    /// Raised when a required parameter is missing from the input dictionary.
    /// </summary>
    public class MissingParameterException : Exception
    {
        public string ParameterName { get; }

        public MissingParameterException(string parameterName)
            : base($"Missing required parameter: {parameterName}.")
        {
            ParameterName = parameterName;
        }
    }

    /// <summary>
    /// Raised when a parameter value cannot be converted to the target type.
    /// </summary>
    public class ParameterTypeException : Exception
    {
        public string ParameterName { get; }
        public Type TargetType { get; }

        public ParameterTypeException(string parameterName, Type targetType, Exception inner)
            : base($"Parameter '{parameterName}' could not be converted to {targetType.Name}: {inner.Message}", inner)
        {
            ParameterName = parameterName;
            TargetType = targetType;
        }
    }
}
