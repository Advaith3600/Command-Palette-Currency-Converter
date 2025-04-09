using System.IO;
using CurrencyConverterExtension.Properties;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace CurrencyConverterExtension.Helpers;

public class SettingsManager : JsonSettingsManager
{
    private static readonly string _namespace = "currency-converter";

    private static string Namespaced(string propertyName) => $"{_namespace}.{propertyName}";

    private readonly ChoiceSetSetting _outputStyle = new(
        Namespaced(nameof(OutputStyle)),
        Resources.output_style,
        Resources.output_style_description,
        new()
        {
            new(Resources.output_style_short_text, "0"),
            new(Resources.output_style_full_text, "1"),
        })
    { Value = "1" };

    public int OutputStyle => int.Parse(_outputStyle.Value);

    internal static string SettingsJsonPath()
    {
        var dir = Utilities.BaseSettingsPath("Microsoft.CmdPal");
        Directory.CreateDirectory(dir);

        return Path.Combine(dir, "currency-converter.json");
    }

    public SettingsManager()
    {
        FilePath = SettingsJsonPath();

        Settings.Add(_outputStyle);

        // Load settings from file upon initialization
        LoadSettings();

        Settings.SettingsChanged += (s, a) => this.SaveSettings();
    }
}
