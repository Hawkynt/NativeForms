using System.Drawing;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// <see cref="ImageList"/> must accept pixel data with no backend present, materialize native
/// images lazily against a backend, cache per index, and survive a backend swap (tests only).
/// </summary>
[TestFixture]
internal sealed class ImageListTests
{
    private static int[] Pixels(int count) => new int[count];

    [Test]
    public void Add_before_any_backend_exists_stores_and_counts()
    {
        using var list = new ImageList(new Size(2, 2));
        Assert.That(list.Add(Pixels(4)), Is.Zero);
        Assert.That(list.Add(Pixels(4)), Is.EqualTo(1));
        Assert.That(list.Count, Is.EqualTo(2));
    }

    [Test]
    public void Add_rejects_wrong_pixel_count()
    {
        using var list = new ImageList(16);
        Assert.Throws<ArgumentException>(() => list.Add(Pixels(4)));
    }

    [Test]
    public void Square_convenience_constructor_sets_both_dimensions()
    {
        using var list = new ImageList(16);
        Assert.That(list.ImageSize, Is.EqualTo(new Size(16, 16)));
    }

    [Test]
    public void Constructor_rejects_empty_sizes()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = new ImageList(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = new ImageList(new Size(16, -1)));
    }

    [Test]
    public void GetImage_materializes_lazily_and_caches_per_index()
    {
        var backend = new HeadlessBackend();
        using var list = new ImageList(new Size(2, 1));
        var index = list.Add(Pixels(2));

        var first = list.GetImage(index, backend);
        Assert.That(first.Width, Is.EqualTo(2));
        Assert.That(first.Height, Is.EqualTo(1));
        Assert.That(list.GetImage(index, backend), Is.SameAs(first), "second lookup must hit the cache");
    }

    [Test]
    public void GetImage_after_backend_swap_rebuilds_the_cache()
    {
        using var list = new ImageList(new Size(1, 1));
        var index = list.Add(Pixels(1));

        var first = list.GetImage(index, new HeadlessBackend());
        var second = list.GetImage(index, new HeadlessBackend());
        Assert.That(second, Is.Not.SameAs(first), "a different backend must not reuse foreign native images");
    }

    [Test]
    public void Clear_resets_count_and_pixel_storage_stays_usable_after_dispose()
    {
        var list = new ImageList(new Size(1, 1));
        list.Add(Pixels(1));
        list.Clear();
        Assert.That(list.Count, Is.Zero);

        var index = list.Add(Pixels(1));
        list.Dispose(); // drops realized images only
        Assert.That(list.GetImage(index, new HeadlessBackend()).Width, Is.EqualTo(1));
    }

    [Test]
    public void Keyed_add_resolves_through_IndexOfKey_case_insensitively()
    {
        using var list = new ImageList(1);
        list.Add(Pixels(1));               // 0, no key
        var keyed = list.Add("Save", Pixels(1)); // 1

        Assert.Multiple(() =>
        {
            Assert.That(keyed, Is.EqualTo(1));
            Assert.That(list.IndexOfKey("Save"), Is.EqualTo(1));
            Assert.That(list.IndexOfKey("save"), Is.EqualTo(1), "keys match case-insensitively");
            Assert.That(list.ContainsKey("SAVE"), Is.True);
            Assert.That(list.IndexOfKey("missing"), Is.EqualTo(-1));
            Assert.That(list.IndexOfKey(""), Is.EqualTo(-1));
            Assert.That(list.IndexOfKey(null), Is.EqualTo(-1));
        });
    }

    [Test]
    public void ResolveIndex_prefers_an_explicit_index_over_the_key()
    {
        using var list = new ImageList(1);
        list.Add("a", Pixels(1)); // 0
        list.Add("b", Pixels(1)); // 1

        Assert.Multiple(() =>
        {
            Assert.That(ImageList.ResolveIndex(list, 0, "b"), Is.EqualTo(0), "a non-negative index wins");
            Assert.That(ImageList.ResolveIndex(list, -1, "b"), Is.EqualTo(1), "the key resolves when the index is unset");
            Assert.That(ImageList.ResolveIndex(list, -1, "missing"), Is.EqualTo(-1));
            Assert.That(ImageList.ResolveIndex(null, -1, "b"), Is.EqualTo(-1), "no list resolves to nothing");
        });
    }

    [Test]
    public void Clear_drops_keys_too()
    {
        using var list = new ImageList(1);
        list.Add("x", Pixels(1));
        list.Clear();

        Assert.That(list.IndexOfKey("x"), Is.EqualTo(-1));
    }
}
