using Hawkynt.NativeForms.Backends.Windows;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// The translation between the toolkit's line ending and the one a Win32 <c>EDIT</c> insists on.
/// </summary>
/// <remarks>
/// This runs everywhere, because none of it touches a window: the peer's translation is four pure
/// functions over a string, and a rule about what a line break means is exactly the kind of thing that
/// must not wait for a Windows runner to be checked. What the shoot on that runner adds is the other
/// half — that a box carrying the pair really does draw three lines.
/// </remarks>
[TestFixture]
public sealed class Win32LineEndingTests
{
    [Test]
    public void ToNative_TurnsEveryLineFeedIntoThePair()
        => Assert.That(TextBoxPeer.ToNativeLineEndings("one\ntwo\nthree"), Is.EqualTo("one\r\ntwo\r\nthree"));

    [Test]
    public void ToNative_LeavesAPairAlone()
        => Assert.That(TextBoxPeer.ToNativeLineEndings("one\r\ntwo"), Is.EqualTo("one\r\ntwo"));

    [Test]
    public void ToNative_HandsBackTheVerySameStringWhenThereIsNoBreak()
    {
        const string Plain = "a single-line text box.";
        Assert.That(TextBoxPeer.ToNativeLineEndings(Plain), Is.SameAs(Plain));
    }

    [Test]
    public void ToCore_FoldsThePairBackToOneCharacter()
        => Assert.That(TextBoxPeer.ToCoreLineEndings("one\r\ntwo\r\nthree"), Is.EqualTo("one\ntwo\nthree"));

    /// <summary>A rich edit hands a paragraph mark back as a bare carriage return, so that folds too.</summary>
    [Test]
    public void ToCore_FoldsALoneCarriageReturn()
        => Assert.That(TextBoxPeer.ToCoreLineEndings("one\rtwo"), Is.EqualTo("one\ntwo"));

    [Test]
    public void TheTranslationRoundTrips()
    {
        const string Core = "A multiline text box.\nLine two.\nLine three.";
        Assert.That(TextBoxPeer.ToCoreLineEndings(TextBoxPeer.ToNativeLineEndings(Core)), Is.EqualTo(Core));
    }

    [Test]
    public void NativeLength_CountsEachBreakTwice()
        => Assert.That(TextBoxPeer.NativeLengthOf("ab\ncd\n"), Is.EqualTo(8));

    /// <summary>
    /// The caret is the reason the translation cannot stop at the text: the widget numbers the pair as
    /// two characters and the core numbers the break as one, so an index means a different place in
    /// each. The two mappings are inverses over every position in a text with breaks in it.
    /// </summary>
    [Test]
    public void EveryCaretPositionSurvivesBothMappings()
    {
        const string Core = "ab\ncd\n\nef";
        for (var i = 0; i <= Core.Length; ++i)
            Assert.That(
                TextBoxPeer.CoreIndexOf(Core, TextBoxPeer.NativeIndexOf(Core, i)),
                Is.EqualTo(i),
                $"core index {i}");
    }

    [Test]
    public void ACaretAfterTwoBreaksIsTwoCharactersFurtherOnInTheWidget()
        => Assert.That(TextBoxPeer.NativeIndexOf("ab\ncd\nef", 7), Is.EqualTo(9));

    [Test]
    public void AWidgetIndexInsideAPairLandsAfterTheBreak()
        => Assert.That(TextBoxPeer.CoreIndexOf("ab\ncd", 3), Is.EqualTo(3));

    [Test]
    public void AnIndexPastTheEndKeepsItsDistance()
        => Assert.That(TextBoxPeer.NativeIndexOf("ab\ncd", 20), Is.EqualTo(21));
}
