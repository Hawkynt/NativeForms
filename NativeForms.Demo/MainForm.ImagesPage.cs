using System.Drawing;

namespace Hawkynt.NativeForms.Demo;

internal sealed partial class MainForm {
  /// <summary>
  /// The Images page: one wide source image shown under every <see cref="PictureBoxSizeMode"/> side by
  /// side, then a translucent image over varying solid colours and over checkerboards of different tile
  /// sizes and colour pairs, so the transparency backdrop reads at a glance.
  /// </summary>
  private TabPage BuildImagesPage() {
    var page = new TabPage("Images") { ImageIndex = _IconBlue };
    var controls = new List<Control>();

    // A wide (150×70) source so the aspect-sensitive modes visibly differ inside a taller box.
    var source = _backend.CreateImage(150, 70, GradientPixels(150, 70, Color.SeaGreen, Color.MediumOrchid));
    var modes = new (string Label, PictureBoxSizeMode Mode)[]
    {
            ("Normal (top-left)", PictureBoxSizeMode.Normal),
            ("CenterImage", PictureBoxSizeMode.CenterImage),
            ("StretchImage", PictureBoxSizeMode.StretchImage),
            ("Zoom (fit, aspect)", PictureBoxSizeMode.Zoom),
            ("FitToWidth", PictureBoxSizeMode.FitToWidth),
            ("FitToHeight", PictureBoxSizeMode.FitToHeight),
    };

    controls.Add(Caption("PictureBox size modes — the same 150×70 image in a 150×100 box", 16, 12, 500));
    for (var i = 0; i < modes.Length; ++i) {
      var x = 16 + ((i % 3) * 170);
      var y = 36 + ((i / 3) * 128);
      controls.Add(Caption(modes[i].Label, x, y, 168));
      controls.Add(new PictureBox {
        Bounds = new(x, y + 20, 150, 100),
        SizeMode = modes[i].Mode,
        BorderStyle = BorderStyle.FixedSingle,
        Image = source,
      });
    }

    // A crimson bar fading opaque → transparent, so the ground behind it shows through the right end.
    var translucent = _backend.CreateImage(140, 70, AlphaRampPixels(140, 70, Color.Crimson));

    controls.Add(Caption("Over solid colours", 540, 12, 160));
    var solids = new[] { Color.White, Color.Black, Color.RoyalBlue, Color.Goldenrod };
    for (var i = 0; i < solids.Length; ++i)
      controls.Add(new PictureBox {
        Bounds = new(540, 36 + (i * 66), 150, 56),
        SizeMode = PictureBoxSizeMode.Zoom,
        BorderStyle = BorderStyle.FixedSingle,
        TransparencyGridSize = 999, // one cell → a flat colour ground
        TransparencyGridColor1 = solids[i],
        TransparencyGridColor2 = solids[i],
        Image = translucent,
      });

    controls.Add(Caption("Over checkerboards", 710, 12, 200));
    var grids = new (int Size, Color A, Color B)[]
    {
            (6, Color.White, Color.Gainsboro),
            (12, Color.LightSteelBlue, Color.White),
            (20, Color.Goldenrod, Color.Cornsilk),
            (8, Color.DimGray, Color.Silver),
    };
    for (var i = 0; i < grids.Length; ++i)
      controls.Add(new PictureBox {
        Bounds = new(710, 36 + (i * 66), 150, 56),
        SizeMode = PictureBoxSizeMode.Zoom,
        BorderStyle = BorderStyle.FixedSingle,
        TransparencyGridSize = grids[i].Size,
        TransparencyGridColor1 = grids[i].A,
        TransparencyGridColor2 = grids[i].B,
        Image = translucent,
      });

    page.Controls.AddRange([.. controls]);
    return page;
  }

  /// <summary>A <paramref name="color"/> bar whose alpha ramps from opaque on the left to clear on the right.</summary>
  private static int[] AlphaRampPixels(int width, int height, Color color) {
    var pixels = new int[width * height];
    for (var x = 0; x < width; ++x) {
      var alpha = 255 - (x * 255 / Math.Max(1, width - 1));
      var argb = Color.FromArgb(alpha, color.R, color.G, color.B).ToArgb();
      for (var y = 0; y < height; ++y)
        pixels[(y * width) + x] = argb;
    }

    return pixels;
  }
}
