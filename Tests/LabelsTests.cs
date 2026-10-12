using ResoundServer.Services;

namespace ResoundServer.Tests;

/// <summary>Every calibre the 4.1 item table carries on a weapon, as the pages show it.</summary>
public class LabelsTests
{
    [Theory]
    [InlineData("Caliber762x39", "7.62x39")]
    [InlineData("Caliber545x39", "5.45x39")]
    [InlineData("Caliber556x45NATO", "5.56x45")]
    [InlineData("Caliber762x54R", "7.62x54R")]
    [InlineData("Caliber762x25TT", "7.62x25")]
    [InlineData("Caliber9x19PARA", "9x19")]
    [InlineData("Caliber9x18PMM", "9x18")]
    [InlineData("Caliber9x18PM", "9x18")]
    [InlineData("Caliber9x39", "9x39")]
    [InlineData("Caliber46x30", "4.6x30")]
    [InlineData("Caliber57x28", "5.7x28")]
    [InlineData("Caliber68x51", "6.8x51")]
    [InlineData("Caliber127x55", "12.7x55")]
    [InlineData("Caliber1143x23ACP", ".45 ACP")]
    [InlineData("Caliber762x35", ".300 BLK")]
    [InlineData("Caliber86x70", ".338 LM")]
    [InlineData("Caliber12g", "12ga")]
    [InlineData("Caliber23x75", "23x75mm")]
    [InlineData("", "-")]
    public void Calibres_read_the_way_people_say_them(string raw, string shown)
    {
        Assert.Equal(shown, Labels.Calibre(raw));
    }

    [Theory]
    [InlineData("assaultCarbine", "Assault carbine")]
    [InlineData("machinegun", "Machine gun")]
    [InlineData("smg", "SMG")]
    [InlineData("someModdedClass", "someModdedClass")]
    public void Classes_read_as_words_and_unknown_ones_pass_through(string raw, string shown)
    {
        Assert.Equal(shown, Labels.Class(raw));
    }
}
