# Images, animation and custom cursors

NativeForms carries its own small, pure-managed image pipeline — no native codec, no imaging library —
so pictures and icons ship as ordinary bytes and still work under NativeAOT.

## Supported image formats

`Hawkynt.NativeForms.Drawing.ImageDecoder.Decode(ReadOnlySpan<byte>)` sniffs the format from the
leading bytes and returns a `DecodedImage` (one frame for the stills, several for the animated ones):

| Format | Support |
|---|---|
| **PNG** | 8-bit-per-channel, non-interlaced: grayscale, grayscale+alpha, RGB, RGBA, palette; all five scanline filters. (16-bit, interlaced and `tRNS` palette transparency are not decoded.) |
| **BMP** | Uncompressed `BI_RGB` at 8-bit (palette), 24-bit and 32-bit; bottom-up or top-down. |
| **PCX** | RLE, 8-bit indexed (VGA trailer palette) or 24-bit (three 8-bit planes). |
| **GIF** (87a/89a) | LZW image data composited onto the logical screen, per-frame delay, disposal and transparency, and the NETSCAPE loop count → a multi-frame animation. |
| **ICO** | Directory of embedded PNG or classic `BI_RGB` bitmap entries (32-bit BGRA, or 24-bit BGR + 1-bit AND mask); the best-fit size is chosen. |
| **CUR** | An ICO with directory type 2; the hotspot is read from the entry. |
| **ANI** | RIFF/ACON: the `fram` list of ICO/CUR frames with the `anih` rate and optional `rate`/`seq` chunks; loops forever. |

Every decoder produces row-major **0xAARRGGBB** pixels (straight alpha). The encoders live only in the
test project, so the shipped library carries a decoder alone.

`ImageList.AddPng` / `ImageList.AddIco` are conveniences that decode and store (nearest-neighbor
resampled to the list's `ImageSize`).

## Animated images

Wrap a decoded image in an `AnimatedImage` and hand it to a control that shows one:

```csharp
var image = AnimatedImage.Decode(File.ReadAllBytes("spinner.gif"));
pictureBox.AnimatedImage = image;   // still or animated
```

- **Which frame shows is a pure function of elapsed time** (taken modulo one loop), not of a tick
  counter — so an animation stays in sync whether or not it has been on screen. A control that was
  hidden and comes back paints the exact frame it *would* have shown had it never been hidden.
- One shared **animation clock** (a single `Timer`) repaints only the *visible* animated controls as
  their frame advances; hidden ones neither tick nor repaint.
- **Loop mode** is `AnimatedImage.LoopCount`: `0` loops forever, `1` plays once (no loop), `N` plays
  `N` times and then holds the last frame. It defaults to what the format declared.
- **Disabling** the hosting control **freezes** the animation on its current frame and paints it
  **grayscale**; re-enabling **resumes exactly where it stopped** (the paused span is excluded from the
  clock — distinct from hiding, which keeps the virtual clock running).

## Custom cursors from a `.cur` / `.ani` file

`Cursor.FromBytes` turns image bytes into a custom bitmap pointer and honours a `.cur`'s hotspot; assign
it to any control's ambient `Cursor`:

```csharp
using Hawkynt.NativeForms;

// A .cur — its hotspot (the pixel the click aligns to) is read from the file:
control.Cursor = Cursor.FromBytes(File.ReadAllBytes("pointer.cur"));

// An animated .ani uses its first frame; an .ico or any still image pins the hotspot top-left:
form.Cursor = Cursor.FromBytes(File.ReadAllBytes("wait.ani"));
```

`Cursor.FromImage(int[] argb, width, height, hotspotX, hotspotY)` builds one straight from raw pixels
(no file), for a procedurally-drawn pointer.

The backend realizes it natively — `gdk_cursor_new_from_pixbuf` on GTK, `CreateIconIndirect` (with a
zero `fIcon` and the hotspot) on Win32 — so the pointer changes for real while it is over the control.
Animated-cursor frames are not driven by the OS here; the `.ani`'s first frame is used as a still
pointer.
