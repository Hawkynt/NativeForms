using System.Drawing;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// The shared hit-testing predicate every click-to-activate control routes through: inside the
/// client rectangle is a hit, the right/bottom edges are exclusive, and negative coordinates miss.
/// </summary>
[TestFixture]
internal sealed class HitTestTests
{
    private static readonly Button _control = new() { Bounds = new(10, 10, 100, 40) };

    [TestCase(0, 0, true)]
    [TestCase(50, 20, true)]
    [TestCase(99, 39, true)]
    [TestCase(100, 20, false, TestName = "Right_edge_is_exclusive")]
    [TestCase(50, 40, false, TestName = "Bottom_edge_is_exclusive")]
    [TestCase(-1, 20, false)]
    [TestCase(50, -1, false)]
    public void ClientContains_matches_the_client_rectangle(int x, int y, bool expected)
        => Assert.That(HitTest.ClientContains(_control, new Point(x, y)), Is.EqualTo(expected));
}
