// Nullable-flow attribute polyfills. netstandard2.0 predates these BCL types, so
// they are declared here to let the library use nullable reference annotations.
// Compiled only for netstandard2.0; on a modern TFM the real BCL types are used.
#if NETSTANDARD2_0
namespace System.Diagnostics.CodeAnalysis;

/// <summary>
/// Specifies that an output may be null even if the corresponding type
/// disallows it, when the method returns the given value.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
// Compile-time metadata only: never executed at runtime, so excluded from coverage.
[ExcludeFromCodeCoverage]
internal sealed class MaybeNullWhenAttribute : Attribute
{
    /// <summary>Initializes the attribute with the return value condition.</summary>
    /// <param name="returnValue">The return value when the output may be null.</param>
    public MaybeNullWhenAttribute(bool returnValue) => ReturnValue = returnValue;

    /// <summary>The return value condition under which the output may be null.</summary>
    public bool ReturnValue { get; }
}
#endif
