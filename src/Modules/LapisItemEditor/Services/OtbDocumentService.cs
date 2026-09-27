using Narzedzia.Core.Models;
using Narzedzia.Core.Parsers;

namespace Modules.LapisItemEditor.Services;

public sealed class OtbDocumentService
{
    private readonly OtbParser _parser = new();

    public OtbFile Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Ścieżka OTB nie może być pusta.", nameof(path));
        }

        return _parser.Parse(path);
    }

    public void Save(OtbFile file, string path)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Ścieżka OTB nie może być pusta.", nameof(path));
        }

        _parser.Save(file, path);
    }

    public OtbFile CreateEmpty() => new()
    {
        MajorVersion = 3,
        MinorVersion = 0,
        BuildNumber = 0,
        Description = "OTB created in Mikaschi Studio Tools — Item Editor"
    };
}
