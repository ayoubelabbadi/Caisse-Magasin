using System.IO;
using System.Text.Json;

namespace Caisse.Desktop;

public sealed class ShopSettings
{
    public string Name { get; set; } = "Mon magasin";
    public string Currency { get; set; } = "MAD";
    public string RegisterName { get; set; } = "Caisse · " + Environment.MachineName;
    public static ShopSettings Load(string directory)
    {
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "settings.json");
        if (File.Exists(file)) return JsonSerializer.Deserialize<ShopSettings>(File.ReadAllText(file)) ?? new();
        var settings = new ShopSettings();
        File.WriteAllText(file, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        return settings;
    }
}
