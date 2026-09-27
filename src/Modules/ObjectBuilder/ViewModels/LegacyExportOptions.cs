namespace Modules.ObjectBuilder.ViewModels;

public enum LegacyExportFormat
{
    Png,
    Bmp,
    Jpg,
    Obd,
    Aec
}

public sealed record LegacyExportOptions(
    string BaseName,
    string OutputDirectory,
    LegacyExportFormat Format,
    bool TransparentBackground)
{
    public string Extension => Format switch
    {
        LegacyExportFormat.Png => ".png",
        LegacyExportFormat.Bmp => ".bmp",
        LegacyExportFormat.Jpg => ".jpg",
        LegacyExportFormat.Obd => ".obd",
        LegacyExportFormat.Aec => ".aec",
        _ => throw new ArgumentOutOfRangeException()
    };
}
