namespace Modules.ObjectBuilder.Services;

/// <summary>
/// Ustawienia klienta zapisane przez OTClient/Object Builder w pliku tibia.otfi.
/// Wartości null oznaczają, że dana opcja nie została podana.
/// </summary>
public sealed record LegacyClientConfiguration(
    bool? ExtendedSprites,
    bool? Transparency,
    bool? ImprovedAnimations,
    bool? FrameGroups,
    string? MetadataFileName,
    string? SpritesFileName,
    string? SourcePath)
{
    public static LegacyClientConfiguration LoadFromDirectory(string directory)
    {
        var path = Directory.EnumerateFiles(directory, "*.otfi", SearchOption.TopDirectoryOnly)
            .OrderBy(file => string.Equals(Path.GetFileName(file), "tibia.otfi", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(file => file, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (path is null)
        {
            return new(null, null, null, null, null, null, null);
        }

        bool? extended = null;
        bool? transparency = null;
        bool? improvedAnimations = null;
        bool? frameGroups = null;
        string? metadataFile = null;
        string? spritesFile = null;

        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim().Trim('"', '\'');
            switch (key.ToLowerInvariant())
            {
                case "extended":
                    extended = ParseBoolean(value);
                    break;
                case "transparency":
                    transparency = ParseBoolean(value);
                    break;
                case "frame-durations":
                    improvedAnimations = ParseBoolean(value);
                    break;
                case "frame-groups":
                    frameGroups = ParseBoolean(value);
                    break;
                case "metadata-file":
                    metadataFile = EmptyToNull(value);
                    break;
                case "sprites-file":
                    spritesFile = EmptyToNull(value);
                    break;
            }
        }

        return new(extended, transparency, improvedAnimations, frameGroups, metadataFile, spritesFile, path);
    }

    private static bool? ParseBoolean(string value) => value.ToLowerInvariant() switch
    {
        "true" or "yes" or "1" or "on" => true,
        "false" or "no" or "0" or "off" => false,
        _ => null
    };

    private static string? EmptyToNull(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
