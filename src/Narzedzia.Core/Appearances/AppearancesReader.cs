using Google.Protobuf;
using AppearancesProto = Narzedzia.Core.Tibia12.Appearances;

namespace Narzedzia.Core.Appearances;

/// <summary>
/// Reader pliku appearances.dat [plik assetow Tibii w formacie Protobuf].
/// </summary>
public sealed class AppearancesReader
{
    public AppearancesProto Read(string path)
    {
        using var stream = File.OpenRead(path);
        return Read(stream);
    }

    public AppearancesProto Read(Stream stream) => AppearancesProto.Parser.ParseFrom(stream);

    public void Write(string path, AppearancesProto data)
    {
        using var stream = File.Create(path);
        data.WriteTo(stream);
    }

    /// <summary>
    /// Atomic write: serialize to *.tmp, then File.Replace(tmp, target).
    /// Plik docelowy pozostaje nienaruszony jeśli WriteTo rzuci.
    /// </summary>
    public void WriteAtomic(string path, AppearancesProto data)
    {
        var tmpPath = path + ".tmp";

        using (var stream = File.Create(tmpPath))
        {
            data.WriteTo(stream);
        }

        if (File.Exists(path))
        {
            File.Replace(tmpPath, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(tmpPath, path);
        }
    }
}
