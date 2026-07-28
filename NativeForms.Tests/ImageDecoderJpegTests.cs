using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// The JPEG decoder (PRD §14), checked against libjpeg-turbo rather than against itself: every
/// fixture is embedded beside the pixels <c>djpeg -nosmooth -dct int</c> produced from it, so the
/// assertion is a per-pixel comparison with the reference implementation whose integer IDCT and
/// sample-replicating upsampling this decoder deliberately matches.
/// </summary>
/// <remarks>
/// <c>-nosmooth</c> is what makes the comparison exact rather than approximate: libjpeg's default
/// "fancy" upsampling interpolates chroma triangularly, which is a different picture from the one any
/// nearest-neighbour decoder produces, and comparing against it would only prove the two differ.
/// </remarks>
[TestFixture]
internal sealed class ImageDecoderJpegTests
{
    /// <summary>The fixture itself — baseline DCT, 4:2:0 chroma.</summary>
    private const string _Baseline420Jpeg =
        "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAMCAgMCAgMDAwMEAwMEBQgFBQQEBQoHBwYIDAoMDAsKCwsNDhIQDQ4RDgsLEBYQ"
        + "ERMUFRUVDA8XGBYUGBIUFRT/2wBDAQMEBAUEBQkFBQkUDQsNFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQU"
        + "FBQUFBQUFBQUFBQUFBT/wAARCAALABEDASIAAhEBAxEB/8QAGAAAAgMAAAAAAAAAAAAAAAAAAAcCBAj/xAAjEAABAwMCBwAA"
        + "AAAAAAAAAAABAAIDBAYRIXESExQWMVNi/8QAFgEBAQEAAAAAAAAAAAAAAAAAAQcI/8QAHxEAAQMDBQAAAAAAAAAAAAAAAQAC"
        + "AwUhMQcSF2GR/9oADAMBAAIRAxEAPwDJ0Vy8xpIfruqhu97Ji1x03S5FXM3xI4KJqZSc8ZytbwaG0iN7zJtcDi1wqhNrHVHs"
        + "aI9wIzfKZvdn0hLPqpfYUJ4Mo3XiOZKr36v/2Q==";

    /// <summary>libjpeg-turbo's own decode of it — baseline DCT, 4:2:0 chroma.</summary>
    private const string _Baseline420Reference =
        "iVBORw0KGgoAAAANSUhEUgAAABEAAAALCAIAAAAWQvFQAAAAIGNIUk0AAHomAACAhAAA+gAAAIDoAAB1MAAA6mAAADqYAAAX"
        + "cJy6UTwAAAAGYktHRAD/AP8A/6C9p5MAAAAHdElNRQfqBxwRJQlfLwd3AAAAJXRFWHRkYXRlOmNyZWF0ZQAyMDI2LTA3LTI4"
        + "VDE3OjM3OjA5KzAwOjAwnaZnNAAAACV0RVh0ZGF0ZTptb2RpZnkAMjAyNi0wNy0yOFQxNzozNzowOSswMDowMOz734gAAAAo"
        + "dEVYdGRhdGU6dGltZXN0YW1wADIwMjYtMDctMjhUMTc6Mzc6MDkrMDA6MDC77v5XAAABPklEQVQozz2RsW5UURBDj+fdVTYh"
        + "gShIFBQUFNRUVHw/NR9AhUQoiBIk2GT3zZ0xxUNx4cKSdSRbJ94YFxPwcpK0kMDoto0HMHUmKRm2h9aR5NYR6kqhJo0NBtNA"
        + "OTAFxniOL2cfgfN6krTrAsoXtg/LTXcf2QOKKWmnR0nn83HMOYHqkrS4bU+m8TrXpk8IUM8gUEoanePr8gl47buIeFkHxBM3"
        + "tr/rfbvvuTbecYyIKx4kXcZhZKak2TM6igL+u6vpopqGjI4khZIc3/iMeeA2Iq79S9IxXtj+sbyrqjvednfYQpc8Ce30d6xe"
        + "hVbWpZckZWXnRquqoopaANg48tQHfkracy/pgt+SOgT88ZXtg151d7OTFLKk4RxJYhYSM1iBKoSSvfH2XiMgsJDZPnyWAQKA"
        + "BT3v0eEtApT6BwyK4AqAi0CLAAAAAElFTkSuQmCC";

    /// <summary>The fixture itself — baseline DCT, 4:4:4 chroma.</summary>
    private const string _Baseline444Jpeg =
        "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAMCAgICAgMCAgIDAwMDBAYEBAQEBAgGBgUGCQgKCgkICQkKDA8MCgsOCwkJDREN"
        + "Dg8QEBEQCgwSExIQEw8QEBD/2wBDAQMDAwQDBAgEBAgQCwkLEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQ"
        + "EBAQEBAQEBAQEBAQEBD/wAARCAALABEDAREAAhEBAxEB/8QAGQAAAQUAAAAAAAAAAAAAAAAAAQIDBAcI/8QAIhAAAQQBAgcA"
        + "AAAAAAAAAAAAAQACAxEEBlESExQWMVNj/8QAGQEAAgMBAAAAAAAAAAAAAAAAAQUDBggJ/8QAJhEAAAUCBAcBAAAAAAAAAAAA"
        + "AAECAwQFBxESE6EVFyEyU2Jxgf/aAAwDAQACEQMRAD8AydFqXmNJEova06l2nKM4SVNdPg21GuYchBqJzr9ER2r5WTcDzQvd"
        + "WduxsZ+LrtFj+CurvE+zI0XT3Dvdn0S3kt6bBhzc99xWgy8lviZwXQ9yiU9zuaIxhJFYnI7XTCTkzk2ZTalTS4aU5UtlgIlV"
        + "KWpWY3DxB6rI9rkOEwvGQPFJnkMf/9k=";

    /// <summary>libjpeg-turbo's own decode of it — baseline DCT, 4:4:4 chroma.</summary>
    private const string _Baseline444Reference =
        "iVBORw0KGgoAAAANSUhEUgAAABEAAAALCAIAAAAWQvFQAAAAIGNIUk0AAHomAACAhAAA+gAAAIDoAAB1MAAA6mAAADqYAAAX"
        + "cJy6UTwAAAAGYktHRAD/AP8A/6C9p5MAAAAHdElNRQfqBxwRJQlfLwd3AAAAJXRFWHRkYXRlOmNyZWF0ZQAyMDI2LTA3LTI4"
        + "VDE3OjM3OjA5KzAwOjAwnaZnNAAAACV0RVh0ZGF0ZTptb2RpZnkAMjAyNi0wNy0yOFQxNzozNzowOSswMDowMOz734gAAAAo"
        + "dEVYdGRhdGU6dGltZXN0YW1wADIwMjYtMDctMjhUMTc6Mzc6MDkrMDA6MDC77v5XAAABUUlEQVQozy3RwW7UMBRG4XOv7SQz"
        + "0wqQEAgxQrz/a6GOhNi0nZLEsX1/FmV59C2PhSEhzDC34D2Fm0kCzAwIBMjx4XbjGmZDhkUmjBAuHOuSIQ8zwBRmcgw83+wq"
        + "yYiklhlAtzKYhvUhH7iUEyR6UhQiGPmmz04UeuEodGFNc2N64bGJxiQmJybqQp0JqPkX18SY2U8ckx2SNcpBeYqflbxxahQn"
        + "ZrYL62J1Uc1/+JLpM/XB6sTu1rtKI73qw8p853HlLJhZL7ydrF605998y4zCftF6ZpvoAZ00sIOycn7lYyMXto3LHNvCnp/4"
        + "YYyZfeF+5u9Cc4SlHb8zP/PwbJ+q5qS+8HaiFqv5xndHTiv0zJFp7t0Uqx4P8s65agpSIhrTRnNhX7klZIQxMsOJRACNPKwc"
        + "TI0yMFckG0mRwPCGAEwYOAH8v4gPHBwDwTtJ/wDwOcnq2c5/1wAAAABJRU5ErkJggg==";

    /// <summary>The fixture itself — progressive DCT, 4:2:0 chroma.</summary>
    private const string _ProgressiveJpeg =
        "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAMCAgMCAgMDAwMEAwMEBQgFBQQEBQoHBwYIDAoMDAsKCwsNDhIQDQ4RDgsLEBYQ"
        + "ERMUFRUVDA8XGBYUGBIUFRT/2wBDAQMEBAUEBQkFBQkUDQsNFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQU"
        + "FBQUFBQUFBQUFBQUFBT/wgARCAALABEDASIAAhEBAxEB/8QAGAAAAgMAAAAAAAAAAAAAAAAAAAYBAwf/xAAWAQEBAQAAAAAA"
        + "AAAAAAAAAAABBgf/2gAMAwEAAhADEAAAAcmqXI1uoZhZE//EABoQAAICAwAAAAAAAAAAAAAAAAABEhQEESH/2gAIAQEAAQUC"
        + "WTst9tkmSZJn/8QAGxEAAgEFAAAAAAAAAAAAAAAAAAEEAgYREhb/2gAIAQMBAT8BrvGU0tcnZSj/xAAbEQABBAMAAAAAAAAA"
        + "AAAAAAABAAIEBhESFv/aAAgBAgEBPwFlGiNJ2wVw0Nf/xAAWEAEBAQAAAAAAAAAAAAAAAAAQMQD/2gAIAQEABj8CaXf/xAAb"
        + "EAACAQUAAAAAAAAAAAAAAAAAEQEQMUFhcf/aAAgBAQABPyFC48LJ0LyNlJ//2gAMAwEAAgADAAAAEIvf/8QAGhEAAgIDAAAA"
        + "AAAAAAAAAAAAAAERMSFxkf/aAAgBAwEBPxBLQNXmzb0//8QAGhEAAgIDAAAAAAAAAAAAAAAAAAERITFxkf/aAAgBAgEBPxB1"
        + "wHirRr4f/8QAGxABAAICAwAAAAAAAAAAAAAAAQARIXEQMWH/2gAIAQEAAT8QvEz3F5Mb4DqBF273PVP/2Q==";

    /// <summary>libjpeg-turbo's own decode of it — progressive DCT, 4:2:0 chroma.</summary>
    private const string _ProgressiveReference =
        "iVBORw0KGgoAAAANSUhEUgAAABEAAAALCAIAAAAWQvFQAAAAIGNIUk0AAHomAACAhAAA+gAAAIDoAAB1MAAA6mAAADqYAAAX"
        + "cJy6UTwAAAAGYktHRAD/AP8A/6C9p5MAAAAHdElNRQfqBxwRJQrGJlbNAAAAJXRFWHRkYXRlOmNyZWF0ZQAyMDI2LTA3LTI4"
        + "VDE3OjM3OjEwKzAwOjAwxJQieQAAACV0RVh0ZGF0ZTptb2RpZnkAMjAyNi0wNy0yOFQxNzozNzoxMCswMDowMLXJmsUAAAAo"
        + "dEVYdGRhdGU6dGltZXN0YW1wADIwMjYtMDctMjhUMTc6Mzc6MTArMDA6MDDi3LsaAAABPklEQVQozz2RsW5UURBDj+fdVTYh"
        + "gShIFBQUFNRUVHw/NR9AhUQoiBIk2GT3zZ0xxUNx4cKSdSRbJ94YFxPwcpK0kMDoto0HMHUmKRm2h9aR5NYR6kqhJo0NBtNA"
        + "OTAFxniOL2cfgfN6krTrAsoXtg/LTXcf2QOKKWmnR0nn83HMOYHqkrS4bU+m8TrXpk8IUM8gUEoanePr8gl47buIeFkHxBM3"
        + "tr/rfbvvuTbecYyIKx4kXcZhZKak2TM6igL+u6vpopqGjI4khZIc3/iMeeA2Iq79S9IxXtj+sbyrqjvednfYQpc8Ce30d6xe"
        + "hVbWpZckZWXnRquqoopaANg48tQHfkracy/pgt+SOgT88ZXtg151d7OTFLKk4RxJYhYSM1iBKoSSvfH2XiMgsJDZPnyWAQKA"
        + "BT3v0eEtApT6BwyK4AqAi0CLAAAAAElFTkSuQmCC";

    /// <summary>The fixture itself — baseline DCT, a single component.</summary>
    private const string _GrayscaleJpeg =
        "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAMCAgMCAgMDAwMEAwMEBQgFBQQEBQoHBwYIDAoMDAsKCwsNDhIQDQ4RDgsLEBYQ"
        + "ERMUFRUVDA8XGBYUGBIUFRT/wAALCAALABEBAREA/8QAGQAAAQUAAAAAAAAAAAAAAAAAAAECBAYI/8QAIhAAAQMBCQEAAAAA"
        + "AAAAAAAAAQACAyIEBQYREhQWIVJi/9oACAEBAAA/AMOx35raSHdqOcSPbIWkp/IfpUZlplGdZSGeQk1FG4k9lf/Z";

    /// <summary>libjpeg-turbo's own decode of it — baseline DCT, a single component.</summary>
    private const string _GrayscaleReference =
        "iVBORw0KGgoAAAANSUhEUgAAABEAAAALCAIAAAAWQvFQAAAABmJLR0QA/wD/AP+gvaeTAAAAB3RJTUUH6gccESUKxiZWzQAA"
        + "ACV0RVh0ZGF0ZTpjcmVhdGUAMjAyNi0wNy0yOFQxNzozNzoxMCswMDowMMSUInkAAAAldEVYdGRhdGU6bW9kaWZ5ADIwMjYt"
        + "MDctMjhUMTc6Mzc6MTArMDA6MDC1yZrFAAAAKHRFWHRkYXRlOnRpbWVzdGFtcAAyMDI2LTA3LTI4VDE3OjM3OjEwKzAwOjAw"
        + "4ty7GgAAAJNJREFUKM9dksERACEIAwljTfZfGMI94kUGHo4QFxDF3ruqzMzMAGilUVKE7jrn0OkA3ZHrMREhQQA1bSTRVkSo"
        + "gpjMrN9GRgC3NwDuziiBcw5hMu6uY7cOgMykwPT5m9qjehmFWEqXEcYDmcn19cb0fUTCdB+qlxnRatanXFWvtzGcMXEl4oRW"
        + "RPRHGJ+g/wPV/AC3ncY5wPG/HAAAAABJRU5ErkJggg==";

    /// <summary>The fixture itself — baseline DCT with a restart interval of one MCU.</summary>
    private const string _RestartJpeg =
        "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAMCAgMCAgMDAwMEAwMEBQgFBQQEBQoHBwYIDAoMDAsKCwsNDhIQDQ4RDgsLEBYQ"
        + "ERMUFRUVDA8XGBYUGBIUFRT/2wBDAQMEBAUEBQkFBQkUDQsNFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQU"
        + "FBQUFBQUFBQUFBQUFBT/wAARCAALABEDASIAAhEBAxEB/8QAGAAAAwEBAAAAAAAAAAAAAAAAAAcIAgT/xAAjEAABAwMCBwAA"
        + "AAAAAAAAAAABAAIDBAYRIXESExQWMVNi/8QAFQEBAQAAAAAAAAAAAAAAAAAABwj/xAAfEQABAwMFAAAAAAAAAAAAAAABAAID"
        + "BSExBxIXYZH/3QAEAAH/2gAMAwEAAhEDEQA/AJOiuXmNJD9d1yG73smLXHTdLkVczfEjgsmplJzxnKreDQ2kRveZNrgcWuEo"
        + "Tax1R7GiPcCM3yv/0JI7s+kJZ9VL7ChVxwZRuvEn8yVXv1f/2Q==";

    /// <summary>libjpeg-turbo's own decode of it — baseline DCT with a restart interval of one MCU.</summary>
    private const string _RestartReference =
        "iVBORw0KGgoAAAANSUhEUgAAABEAAAALCAIAAAAWQvFQAAAAIGNIUk0AAHomAACAhAAA+gAAAIDoAAB1MAAA6mAAADqYAAAX"
        + "cJy6UTwAAAAGYktHRAD/AP8A/6C9p5MAAAAHdElNRQfqBxwRJQrGJlbNAAAAJXRFWHRkYXRlOmNyZWF0ZQAyMDI2LTA3LTI4"
        + "VDE3OjM3OjEwKzAwOjAwxJQieQAAACV0RVh0ZGF0ZTptb2RpZnkAMjAyNi0wNy0yOFQxNzozNzoxMCswMDowMLXJmsUAAAAo"
        + "dEVYdGRhdGU6dGltZXN0YW1wADIwMjYtMDctMjhUMTc6Mzc6MTArMDA6MDDi3LsaAAABPklEQVQozz2RsW5UURBDj+fdVTYh"
        + "gShIFBQUFNRUVHw/NR9AhUQoiBIk2GT3zZ0xxUNx4cKSdSRbJ94YFxPwcpK0kMDoto0HMHUmKRm2h9aR5NYR6kqhJo0NBtNA"
        + "OTAFxniOL2cfgfN6krTrAsoXtg/LTXcf2QOKKWmnR0nn83HMOYHqkrS4bU+m8TrXpk8IUM8gUEoanePr8gl47buIeFkHxBM3"
        + "tr/rfbvvuTbecYyIKx4kXcZhZKak2TM6igL+u6vpopqGjI4khZIc3/iMeeA2Iq79S9IxXtj+sbyrqjvednfYQpc8Ce30d6xe"
        + "hVbWpZckZWXnRquqoopaANg48tQHfkracy/pgt+SOgT88ZXtg151d7OTFLKk4RxJYhYSM1iBKoSSvfH2XiMgsJDZPnyWAQKA"
        + "BT3v0eEtApT6BwyK4AqAi0CLAAAAAElFTkSuQmCC";

    /// <summary>The fixtures, each paired with the reference decode it must reproduce.</summary>
    private static IEnumerable<TestCaseData> Fixtures()
    {
        yield return new TestCaseData(_Baseline420Jpeg, _Baseline420Reference).SetName("Baseline 4:2:0");
        yield return new TestCaseData(_Baseline444Jpeg, _Baseline444Reference).SetName("Baseline 4:4:4");
        yield return new TestCaseData(_ProgressiveJpeg, _ProgressiveReference).SetName("Progressive 4:2:0");
        yield return new TestCaseData(_GrayscaleJpeg, _GrayscaleReference).SetName("Grayscale");
        yield return new TestCaseData(_RestartJpeg, _RestartReference).SetName("Restart interval");
    }

    [TestCaseSource(nameof(Fixtures))]
    public void Decodes_what_libjpeg_decodes(string fixture, string reference)
    {
        var (width, height, argb) = ImageDecoder.DecodeJpeg(Convert.FromBase64String(fixture));
        var (referenceWidth, referenceHeight, referenceArgb) = ImageDecoder.DecodePng(Convert.FromBase64String(reference));

        Assert.That((width, height), Is.EqualTo((referenceWidth, referenceHeight)));

        var worst = 0;
        var worstAt = -1;
        for (var i = 0; i < referenceArgb.Length; ++i)
        {
            for (var shift = 0; shift <= 16; shift += 8)
            {
                var difference = Math.Abs(((argb[i] >> shift) & 0xFF) - ((referenceArgb[i] >> shift) & 0xFF));
                if (difference <= worst)
                    continue;

                worst = difference;
                worstAt = i;
            }
        }

        Assert.Multiple(() =>
        {
            // One level of slack, for the rounding in the colour transform: libjpeg converts through a
            // precomputed table and this decoder computes the same fixed-point expression per pixel.
            Assert.That(
                worst,
                Is.LessThanOrEqualTo(1),
                worstAt < 0
                    ? "no pixel differed"
                    : $"pixel {worstAt % width},{worstAt / width} decoded as {argb[worstAt]:X8} where libjpeg produced {referenceArgb[worstAt]:X8}");

            Assert.That(argb.Select(pixel => (pixel >> 24) & 0xFF), Is.All.EqualTo(0xFF), "JPEG has no transparency");
        });
    }

    [Test]
    public void Restart_markers_do_not_stop_the_decode_after_the_first_interval()
    {
        // A reader that latches end-of-data on the restart marker and never clears it decodes the first
        // interval and zeroes for the rest, which at one MCU per interval means one 16x16 block of
        // picture followed by mid-grey. Comparing the whole image against the same image coded without
        // restarts is what makes that failure visible rather than plausible.
        var (_, _, withRestarts) = ImageDecoder.DecodeJpeg(Convert.FromBase64String(_RestartJpeg));
        var (_, _, withoutRestarts) = ImageDecoder.DecodeJpeg(Convert.FromBase64String(_Baseline420Jpeg));

        Assert.That(withRestarts, Is.EqualTo(withoutRestarts));
    }

    [Test]
    public void Progressive_and_baseline_agree_on_the_same_picture()
    {
        var (_, _, progressive) = ImageDecoder.DecodeJpeg(Convert.FromBase64String(_ProgressiveJpeg));
        var (_, _, baseline) = ImageDecoder.DecodeJpeg(Convert.FromBase64String(_Baseline420Jpeg));

        Assert.That(progressive, Is.EqualTo(baseline));
    }

    [Test]
    public void Decode_sniffs_a_jpeg_and_returns_one_frame()
    {
        var image = ImageDecoder.Decode(Convert.FromBase64String(_Baseline420Jpeg));

        Assert.Multiple(() =>
        {
            Assert.That((image.Width, image.Height), Is.EqualTo((17, 11)));
            Assert.That(image.Frames, Has.Count.EqualTo(1));
            Assert.That(image.Frames[0].DelayMilliseconds, Is.Zero);
        });
    }

    [Test]
    public void Odd_dimensions_survive_the_mcu_padding()
    {
        // 17x11 fills neither its last 16x16 MCU nor its last row of them, so every plane carries
        // padding the output has to be cropped past.
        var (width, height, argb) = ImageDecoder.DecodeJpeg(Convert.FromBase64String(_Baseline420Jpeg));

        Assert.That(argb, Has.Length.EqualTo(width * height));
    }

    [Test]
    public void Arithmetic_coding_is_refused_by_name()
    {
        var arithmetic = Convert.FromBase64String(_Baseline420Jpeg);
        arithmetic[IndexOfMarker(arithmetic, 0xC0) + 1] = 0xC9; // SOF0 becomes SOF9

        Assert.That(
            () => ImageDecoder.DecodeJpeg(arithmetic),
            Throws.TypeOf<FormatException>().With.Message.Contains("arithmetic"));
    }

    [Test]
    public void Twelve_bit_samples_are_refused_by_name()
    {
        var deep = Convert.FromBase64String(_Baseline420Jpeg);
        deep[IndexOfMarker(deep, 0xC0) + 4] = 12; // the precision byte, past the marker and the length

        Assert.That(
            () => ImageDecoder.DecodeJpeg(deep),
            Throws.TypeOf<FormatException>().With.Message.Contains("12-bit"));
    }

    [TestCase(0, TestName = "Empty")]
    [TestCase(2, TestName = "Signature only")]
    [TestCase(40, TestName = "Truncated mid-header")]
    public void Truncated_data_is_refused(int length)
    {
        var truncated = Convert.FromBase64String(_Baseline420Jpeg).AsSpan(0, length).ToArray();

        Assert.That(() => ImageDecoder.DecodeJpeg(truncated), Throws.TypeOf<FormatException>());
    }

    [Test]
    public void Data_that_is_not_a_jpeg_is_refused()
        => Assert.That(() => ImageDecoder.DecodeJpeg("not a jpeg at all"u8.ToArray()), Throws.TypeOf<FormatException>());

    /// <summary>Where a two-byte marker starts, so a test can corrupt the field that follows it.</summary>
    private static int IndexOfMarker(byte[] data, byte marker)
    {
        for (var i = 0; i < data.Length - 1; ++i)
            if (data[i] == 0xFF && data[i + 1] == marker)
                return i;

        Assert.Fail($"the fixture carries no 0xFF{marker:X2} marker");
        return -1;
    }
}
