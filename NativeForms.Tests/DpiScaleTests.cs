using System.Drawing;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// The per-monitor DPI groundwork: a backend reports its device-to-logical scale through
/// <c>GetDpiScale</c>, <see cref="Control.LogicalToDevice(int)"/> maps logical lengths through it
/// (identity before realization), and the popup math that mixes logical constants into screen
/// coordinates (the tool-tip cursor offset) honors the factor.
/// </summary>
[TestFixture]
internal sealed class DpiScaleTests
{
    /// <summary>Realizes a panel on a backend with the given scripted scale.</summary>
    private static Panel Realize(double dpiScale, out HeadlessBackend backend)
    {
        var panel = new Panel { Bounds = new(0, 0, 100, 100) };
        backend = new HeadlessBackend { DpiScale = dpiScale };
        var form = new Form();
        form.Controls.Add(panel);
        Application.Run(form, backend);
        return panel;
    }

    [Test]
    public void Backend_reports_unity_scale_by_default()
        => Assert.That(new HeadlessBackend().GetDpiScale(), Is.EqualTo(1.0));

    [Test]
    public void LogicalToDevice_scales_and_rounds_by_the_backend_factor()
    {
        var panel = Realize(1.5, out _);

        Assert.Multiple(() =>
        {
            Assert.That(panel.LogicalToDevice(10), Is.EqualTo(15));
            Assert.That(panel.LogicalToDevice(9), Is.EqualTo(14), "13.5 rounds to the nearest even pixel");
            Assert.That(panel.LogicalToDevice(new Size(10, 20)), Is.EqualTo(new Size(15, 30)));
        });
    }

    [Test]
    public void LogicalToDevice_is_identity_before_realization()
    {
        var panel = new Panel();

        Assert.That(panel.LogicalToDevice(10), Is.EqualTo(10));
    }

    [Test]
    public void Tooltip_cursor_offset_scales_with_the_dpi()
    {
        var panel = Realize(2.0, out var backend);
        var canvas = backend.Created.OfType<HeadlessCanvasPeer>().Single();
        canvas.ScreenOrigin = new(50, 60);
        using var toolTip = new ToolTip();
        toolTip.SetToolTip(panel, "hint");

        canvas.RaiseMouseMove(20, 30);
        backend.Timers.Single().FireTick();

        var popup = backend.Created.OfType<HeadlessPopupPeer>().Single();
        // 50+20 = 70; 60 + 30 + (18 logical * 2.0) = 126.
        Assert.That(popup.ShowCalls.Single().Location, Is.EqualTo(new Point(70, 126)));
    }
}
