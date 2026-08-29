using CurrencyConverterExtension.Helpers;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace CurrencyConverterExtension.Tests;

public class CurrencyIconManagerTests
{
    [Fact]
    public void ToRelativeCryptoPath_UsesCryptoFolder()
    {
        Assert.Equal(@"Assets\Crypto\btc.webp", CurrencyIconManager.ToRelativeCryptoPath("BTC"));
    }

    [Fact]
    public void ToRelativeFlagPath_UsesCurrencyIsoCode()
    {
        Assert.Equal(@"Assets\Flags\usd.webp", CurrencyIconManager.ToRelativeFlagPath("USD"));
        Assert.Equal(@"Assets\Flags\eur.webp", CurrencyIconManager.ToRelativeFlagPath("eur"));
    }

    [Fact]
    public void For_ReturnsAppLogo_ForNullOrEmpty()
    {
        Assert.Same(IconManager.Icon, CurrencyIconManager.For(null));
        Assert.Same(IconManager.Icon, CurrencyIconManager.For(""));
        Assert.Same(IconManager.Icon, CurrencyIconManager.For("   "));
    }

    [Fact]
    public void For_ReturnsCachedIcon()
    {
        IconInfo first = CurrencyIconManager.For("usd");
        IconInfo second = CurrencyIconManager.For("USD");

        Assert.Same(first, second);
    }

    [Fact]
    public void For_ReturnsAppLogo_WhenNoAssetExists()
    {
        Assert.Same(IconManager.Icon, CurrencyIconManager.For("zzzznotacurrency"));
    }
}
