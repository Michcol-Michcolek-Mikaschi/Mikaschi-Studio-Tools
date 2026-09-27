using System.Reflection;
using System.Text.Json;

namespace Modules.AssetsEditor.Services;

/// <summary>
/// Ładuje presety serwerów z embeddowanego pliku JSON (zasobu wbudowanego w assembly).
/// Jeśli obok pliku .exe istnieje plik presets.json, nadpisuje domyślne presety.
/// </summary>
public sealed class PresetService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas         = true,
        ReadCommentHandling         = JsonCommentHandling.Skip,
    };

    private const string EmbeddedResourceName =
        "Modules.AssetsEditor.Assets.presets.json";

    private const string ExternalFileName = "presets.json";

    public IReadOnlyList<ServerPreset> Load()
    {
        // Próba zewnętrznego pliku obok .exe (umożliwia customizację przez użytkownika)
        var externalPath = Path.Combine(AppContext.BaseDirectory, ExternalFileName);
        if (File.Exists(externalPath))
        {
            var json = File.ReadAllText(externalPath);
            var presets = JsonSerializer.Deserialize<List<ServerPreset>>(json, JsonOpts);
            if (presets is { Count: > 0 })
                return presets;
        }

        // Fallback: embeddowany domyślny plik
        return LoadEmbedded();
    }

    private static IReadOnlyList<ServerPreset> LoadEmbedded()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(EmbeddedResourceName);
        if (stream is null)
            return DefaultPresets();

        var presets = JsonSerializer.Deserialize<List<ServerPreset>>(stream, JsonOpts);
        return presets ?? DefaultPresets();
    }

    private static IReadOnlyList<ServerPreset> DefaultPresets() =>
    [
        new ServerPreset
        {
            Name               = "Tibia 13.30+",
            ClientVersion      = "13.30",
            ShiftMin           = -512,
            ShiftMax           = 512,
            MaxLevel           = 999,
            ImbueableSlotCountMax = 3,
            Notes              = "Aktualny klient CipSoft / OTClient mehah",
        },
        new ServerPreset
        {
            Name               = "Local Dev",
            ClientVersion      = "dev",
            ShiftMin           = -32768,
            ShiftMax           = 32767,
            MaxLevel           = 9999,
            ImbueableSlotCountMax = 10,
            Notes              = "Bez ograniczeń — środowisko deweloperskie",
        },
    ];
}
