using System.IO;
using System.Text.Json;
using System.Windows.Media;

internal static partial class Program
{
    private static JsonDocument? brandTokens;

    // Reads the vendored brand v2 colors.json, so palette checks follow a re-vendored brand.
    private static Color BrandColor(string theme, string token)
    {
        brandTokens ??= JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "colors.json")));
        var value = brandTokens.RootElement.GetProperty("themes").GetProperty(theme).GetProperty(token).GetString()!;
        return (Color)ColorConverter.ConvertFromString(value);
    }
}
