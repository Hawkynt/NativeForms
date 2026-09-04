using System.Collections.Generic;
using System.Linq;

namespace Hawkynt.NativeForms.Tests;

/// <summary>A model the generator gives a compile-time member lookup.</summary>
[Bindable]
internal partial class BindablePerson {
  public string Name { get; set; } = string.Empty;

  public int Id { get; set; }

  public string? Nickname { get; set; }

  /// <summary>Get-only members are readable, so they are bindable.</summary>
  public string Initial => this.Name.Length > 0 ? this.Name[..1] : string.Empty;

  /// <summary>Not a property, so not in the lookup.</summary>
  public int Field = 0;

  /// <summary>Not public, so not in the lookup.</summary>
  private string Secret { get; set; } = "hidden";

  /// <summary>Static, so not a member of an item.</summary>
  public static string Kind => "person";
}

/// <summary>
/// PRD §6: <c>DataSource</c> plus a member <em>name</em>, resolved at compile time.
///
/// The delegate surfaces are still the primary way to bind here, and nothing about them changes. This is
/// for the case a delegate cannot serve: code ported from Windows Forms, and configuration that genuinely
/// arrives as a string. The generated lookup is what keeps that reflection-free — every accessor is an
/// ordinary property read, so it survives trimming and NativeAOT, and a name the type does not have fails
/// at the call rather than rendering blank rows.
/// </summary>
[TestFixture]
internal sealed class BindableMemberGeneratorTests {
  private static readonly BindablePerson[] _People =
  [
      new() { Name = "Ada", Id = 1, Nickname = "A" },
        new() { Name = "Grace", Id = 2 },
    ];

  // --- The generated lookup ----------------------------------------------------------------------

  [TestCase("Name")]
  [TestCase("Id")]
  [TestCase("Nickname")]
  [TestCase("Initial")]
  public void Every_public_readable_property_is_in_the_lookup(string member)
      => Assert.That(BindablePerson.GetMemberAccessor(member), Is.Not.Null);

  [TestCase("Field", TestName = "a field is not a property")]
  [TestCase("Secret", TestName = "a private property is not public")]
  [TestCase("Kind", TestName = "a static property belongs to the type, not the item")]
  [TestCase("name", TestName = "the match is case sensitive")]
  [TestCase("Missing", TestName = "a member that does not exist")]
  public void Anything_else_is_absent(string member)
      => Assert.That(BindablePerson.GetMemberAccessor(member), Is.Null);

  [Test]
  public void An_accessor_reads_the_property_off_the_item() {
    var accessor = BindablePerson.GetMemberAccessor("Name")!;

    Assert.That(accessor(_People[0]), Is.EqualTo("Ada"));
  }

  [Test]
  public void An_accessor_reads_a_computed_property_too()
      => Assert.That(BindablePerson.GetMemberAccessor("Initial")!(_People[1]), Is.EqualTo("G"));

  // --- The control surface ------------------------------------------------------------------------

  [Test]
  public void A_ComboBox_displays_the_named_member() {
    var combo = new ComboBox();

    combo.SetDataSource(_People, displayMember: nameof(BindablePerson.Name));

    Assert.Multiple(() => {
      Assert.That(combo.Items, Has.Count.EqualTo(2));
      Assert.That(combo.Items.Select(combo.DisplaySelector), Is.EqualTo(new[] { "Ada", "Grace" }));
    });
  }

  [Test]
  public void A_ComboBox_takes_its_value_from_the_named_member() {
    var combo = new ComboBox();
    combo.SetDataSource(_People, nameof(BindablePerson.Name), nameof(BindablePerson.Id));

    combo.SelectedIndex = 1;

    Assert.That(combo.SelectedValue, Is.EqualTo(2));
  }

  [Test]
  public void Assigning_SelectedValue_finds_the_item_with_that_member_value() {
    var combo = new ComboBox();
    combo.SetDataSource(_People, nameof(BindablePerson.Name), nameof(BindablePerson.Id));

    combo.SelectedValue = 1;

    Assert.That(combo.SelectedIndex, Is.Zero);
  }

  [Test]
  public void A_null_member_value_displays_as_empty_rather_than_throwing() {
    var combo = new ComboBox();

    combo.SetDataSource(_People, displayMember: nameof(BindablePerson.Nickname));

    Assert.That(combo.Items.Select(combo.DisplaySelector), Is.EqualTo(new[] { "A", string.Empty }));
  }

  [Test]
  public void A_ListBox_displays_the_named_member() {
    var list = new ListBox();

    list.SetDataSource(_People, nameof(BindablePerson.Name));

    Assert.That(list.Items.Select(list.DisplaySelector), Is.EqualTo(new[] { "Ada", "Grace" }));
  }

  [Test]
  public void Replacing_the_data_source_replaces_the_items() {
    var combo = new ComboBox();
    combo.SetDataSource(_People, nameof(BindablePerson.Name));

    combo.SetDataSource(new List<BindablePerson> { new() { Name = "Solo" } }, nameof(BindablePerson.Name));

    Assert.That(combo.Items, Has.Count.EqualTo(1));
  }

  // --- A typo fails where it was made -------------------------------------------------------------

  [Test]
  public void A_member_the_type_does_not_have_throws_at_the_call() {
    var combo = new ComboBox();

    var thrown = Assert.Throws<ArgumentException>(() => combo.SetDataSource(_People, displayMember: "Nmae"));

    Assert.Multiple(() => {
      Assert.That(thrown!.Message, Does.Contain("Nmae"), "the message has to name the typo");
      Assert.That(thrown.Message, Does.Contain(nameof(BindablePerson)));
      Assert.That(thrown.ParamName, Is.EqualTo("displayMember"));
    });
  }

  [Test]
  public void Omitting_the_members_leaves_the_existing_selectors_alone() {
    var combo = new ComboBox { DisplaySelector = static o => $"<{((BindablePerson)o!).Name}>" };

    combo.SetDataSource(_People);

    Assert.That(combo.Items.Select(combo.DisplaySelector), Is.EqualTo(new[] { "<Ada>", "<Grace>" }));
  }
}
