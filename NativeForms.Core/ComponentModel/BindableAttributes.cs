namespace Hawkynt.NativeForms;

/// <summary>
/// Marks a class the <c>NativeForms.Generators</c> source generator gives a compile-time member lookup,
/// so a list control can resolve a <c>DisplayMember</c>/<c>ValueMember</c> <em>name</em> without
/// reflection. The class must be declared <see langword="partial"/> and must not be nested.
/// </summary>
/// <remarks>
/// <para>
/// Delegates remain the primary binding surface here — <c>DisplaySelector</c> and <c>ValueSelector</c>
/// take a lambda and always have. This exists for the other case: code ported from Windows Forms, where
/// the member is a string, and configuration that legitimately arrives as one (a column chosen at run
/// time, a member named in a settings file). The generator turns those strings into a
/// <see langword="switch"/> over generated accessors, so the convenience costs a dictionary lookup
/// rather than a reflection call and survives trimming and NativeAOT.
/// </para>
/// <para>
/// The attribute is inert metadata without the generator: the class still compiles, and the delegate
/// surfaces still work. Only the generated lookup is absent.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class)]
public sealed class BindableAttribute : Attribute;

/// <summary>
/// The compile-time member lookup a <see cref="BindableAttribute"/> model gains.
/// </summary>
/// <remarks>
/// A static abstract member, so a control constrained to it calls <c>T.GetMemberAccessor(name)</c>
/// directly on the type argument — the lookup is resolved by the JIT or the AOT compiler from the
/// concrete type, with no instance, no dictionary of types, and nothing for a trimmer to fail to see.
/// </remarks>
public interface IBindableMembers
{
    /// <summary>
    /// The accessor for the named public readable property, or <see langword="null"/> when the type has
    /// no such member.
    /// </summary>
    /// <param name="memberName">The property name, matched exactly and case-sensitively.</param>
    static abstract Func<object?, object?>? GetMemberAccessor(string memberName);
}
