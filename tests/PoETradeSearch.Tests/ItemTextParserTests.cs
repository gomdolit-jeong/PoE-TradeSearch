using PoETradeSearch.Core.Parsing;

namespace PoETradeSearch.Tests;

public class ItemTextParserTests
{
    private const string RareRing = """
        Item Class: Rings
        Rarity: Rare
        Storm Loop
        Amethyst Ring
        --------
        Requirements:
        Level: 45
        --------
        Item Level: 84
        --------
        +17% to Chaos Resistance (implicit)
        --------
        +52 to maximum Life
        +38% to Fire Resistance
        Adds 3 to 7 Physical Damage to Attacks
        +25% to Cold Resistance (crafted)
        --------
        Corrupted
        """;

    private const string RareBodyArmour = """
        Item Class: Body Armours
        Rarity: Rare
        Dusk Shell
        Vaal Regalia
        --------
        Quality: +20% (augmented)
        Energy Shield: 412 (augmented)
        --------
        Requirements:
        Level: 68
        Int: 194
        --------
        Sockets: B-B-B-B-B B
        --------
        Item Level: 86
        --------
        +98 to maximum Energy Shield
        84% increased Energy Shield
        +41% to Lightning Resistance (fractured)
        --------
        Shaper Item
        Elder Item
        """;

    private const string UniqueBelt = """
        Item Class: Belts
        Rarity: Unique
        Headhunter
        Leather Belt
        --------
        Requirements:
        Level: 40
        --------
        Item Level: 83
        --------
        +32 to maximum Life (implicit)
        --------
        +50 to Strength
        +45 to Dexterity
        +60 to maximum Life
        20% increased Damage with Hits against Rare monsters
        When you Kill a Rare monster, you gain its Modifiers for 60 seconds
        --------
        "A man's soul rules from a cavern of bone, learns and
        judges through flesh-born windows."
        """;

    private const string AdvancedCopy = """
        Item Class: Rings
        Rarity: Rare
        Storm Loop
        Amethyst Ring
        --------
        Item Level: 84
        --------
        { Implicit Modifier — Chaos, Resistance }
        +17(17-23)% to Chaos Resistance (implicit)
        --------
        { Prefix Modifier "Rotund" (Tier: 3) — Life }
        +52(50-59) to maximum Life
        """;

    [Fact]
    public void Parses_header_of_rare_ring()
    {
        var item = ItemTextParser.Parse(RareRing);

        Assert.Equal("Rings", item.ItemClass);
        Assert.Equal(ItemRarity.Rare, item.Rarity);
        Assert.Equal("Storm Loop", item.Name);
        Assert.Equal("Amethyst Ring", item.BaseType);
        Assert.Equal(84, item.ItemLevel);
        Assert.True(item.Corrupted);
    }

    [Fact]
    public void Parses_mods_with_types_and_templates()
    {
        var item = ItemTextParser.Parse(RareRing);

        Assert.Equal(5, item.Mods.Count);

        Assert.Equal(new ItemMod("+17% to Chaos Resistance", "+#% to Chaos Resistance", [17], ModType.Implicit), item.Mods[0], ModComparer.Instance);
        Assert.Equal("+# to maximum Life", item.Mods[1].Template);
        Assert.Equal([52.0], item.Mods[1].Values);
        Assert.Equal("Adds # to # Physical Damage to Attacks", item.Mods[3].Template);
        Assert.Equal([3.0, 7.0], item.Mods[3].Values);
        Assert.Equal(ModType.Crafted, item.Mods[4].Type);
    }

    [Fact]
    public void Parses_properties_sockets_and_influences()
    {
        var item = ItemTextParser.Parse(RareBodyArmour);

        Assert.Equal(20, item.Quality);
        Assert.Equal("412 (augmented)", item.Properties["Energy Shield"]);
        Assert.Equal("B-B-B-B-B B", item.Sockets);
        Assert.Equal(86, item.ItemLevel);
        Assert.Equal(["shaper", "elder"], item.Influences);
        Assert.Equal(3, item.Mods.Count);
        Assert.Equal(ModType.Fractured, item.Mods[2].Type);
        Assert.False(item.Corrupted);
    }

    [Fact]
    public void Skips_unique_flavour_text()
    {
        var item = ItemTextParser.Parse(UniqueBelt);

        Assert.Equal(ItemRarity.Unique, item.Rarity);
        Assert.Equal("Headhunter", item.Name);
        Assert.Equal(6, item.Mods.Count);
        Assert.DoesNotContain(item.Mods, m => m.Text.Contains("cavern"));
    }

    [Fact]
    public void Handles_advanced_copy_format()
    {
        var item = ItemTextParser.Parse(AdvancedCopy);

        Assert.Equal(2, item.Mods.Count);
        Assert.Equal("+#% to Chaos Resistance", item.Mods[0].Template);
        Assert.Equal([17.0], item.Mods[0].Values);
        Assert.Equal(ModType.Implicit, item.Mods[0].Type);
        Assert.Equal("+# to maximum Life", item.Mods[1].Template);
        Assert.Equal([52.0], item.Mods[1].Values);
    }

    [Theory]
    [InlineData("")]
    [InlineData("hello world")]
    [InlineData("Rarity: Rare")]
    public void Rejects_non_item_text(string text)
    {
        Assert.False(ItemTextParser.LooksLikeItem(text));
    }

    private sealed class ModComparer : IEqualityComparer<ItemMod>
    {
        public static readonly ModComparer Instance = new();
        public bool Equals(ItemMod? x, ItemMod? y) =>
            x is not null && y is not null &&
            x.Text == y.Text && x.Template == y.Template && x.Type == y.Type && x.Values.SequenceEqual(y.Values);
        public int GetHashCode(ItemMod obj) => obj.Template.GetHashCode();
    }
}
