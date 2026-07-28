namespace Hawkynt.NativeForms;

/// <summary>
/// Resolves member names against the lookup a <see cref="BindableAttribute"/> model's generated code
/// provides, turning a miss into an immediate, legible failure.
/// </summary>
internal static class BindableMembers
{
    /// <summary>
    /// The accessor for a named member of <typeparamref name="T"/>.
    /// </summary>
    /// <remarks>
    /// A missing member throws at the call that named it. The reflection-based libraries this shape is
    /// borrowed from bind blank and carry on, which turns a typo into a control that renders empty rows
    /// with nothing to say why; the whole point of resolving members at compile time is that the mistake
    /// is cheap to find.
    /// </remarks>
    /// <typeparam name="T">The model type carrying the generated lookup.</typeparam>
    /// <param name="memberName">The property name to resolve.</param>
    /// <param name="parameterName">The caller's parameter name, for the exception.</param>
    /// <exception cref="ArgumentException">No public readable property of that name exists.</exception>
    public static Func<object?, object?> Require<T>(string memberName, string parameterName)
        where T : IBindableMembers
        => T.GetMemberAccessor(memberName)
           ?? throw new ArgumentException(
               $"'{memberName}' is not a public readable property of {typeof(T).Name}.",
               parameterName);
}
