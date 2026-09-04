using System.Reflection;
using Hawkynt.NativeForms.Backends.MacOS;

namespace Hawkynt.NativeForms.Tests;

[TestFixture]
public sealed class CocoaScrollBarRangeTests {
  private static readonly MethodInfo _CalculateReach = typeof(CocoaBackend).Assembly
      .GetType("Hawkynt.NativeForms.Backends.MacOS.CocoaScrollBarPeer", throwOnError: true)!
      .GetMethod("CalculateReach", BindingFlags.Static | BindingFlags.NonPublic)!;

  [TestCase(0, 100, 10, 91)]
  [TestCase(0, 0, 1, 0)]
  [TestCase(10, 10, 10, 0)]
  [TestCase(10, 12, 10, 0)]
  [TestCase(-5, 5, 3, 8)]
  public void EffectiveRangeNeverExtendsPastMaximum(int minimum, int maximum, int largeChange, int expectedReach) {
    var reach = (int)_CalculateReach.Invoke(null, [minimum, maximum, largeChange])!;
    Assert.That(reach, Is.EqualTo(expectedReach));
    Assert.That(minimum + reach, Is.LessThanOrEqualTo(maximum));
  }
}
