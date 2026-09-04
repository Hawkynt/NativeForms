using System.Reflection;
using Hawkynt.NativeForms;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// The shapes a backend assembly calls into, held to the signatures it was compiled against.
/// </summary>
/// <remarks>
/// The backends ship as their own packages on their own version numbers, so an application routinely
/// runs a core newer than the backend beside it — only the packages whose source changed are rebuilt
/// in a release. Anything a backend calls is therefore a runtime contract and not just a compile-time
/// one: adding an optional parameter to a constructor keeps every caller compiling and replaces the
/// method the already-built ones look for, and the first mouse event then throws
/// <see cref="MissingMethodException"/> on a machine where nothing was rebuilt.
/// </remarks>
[TestFixture]
internal sealed class BackendAbiTests {
  private static ConstructorInfo? Ctor(params Type[] parameters)
      => typeof(MouseEventArgs).GetConstructor(parameters);

  [Test]
  public void A_mouse_event_can_still_be_built_the_way_a_backend_builds_one() {
    Assert.Multiple(() => {
      Assert.That(
          Ctor(typeof(MouseButtons), typeof(int), typeof(int), typeof(int), typeof(KeyModifiers)),
          Is.Not.Null,
          "the five-argument form every shipped backend calls");

      Assert.That(
          Ctor(typeof(MouseButtons), typeof(int), typeof(int), typeof(int), typeof(KeyModifiers), typeof(int)),
          Is.Not.Null,
          "and the form that carries the click count");
    });
  }

  [Test]
  public void The_five_argument_form_means_one_click() {
    var e = new MouseEventArgs(MouseButtons.Left, 3, 4, 0, KeyModifiers.None);

    Assert.That(e.Clicks, Is.EqualTo(1));
  }
}
