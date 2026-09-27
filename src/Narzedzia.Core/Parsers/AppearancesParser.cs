using Narzedzia.Core.Models;

namespace Narzedzia.Core.Parsers;

public class AppearancesParser
{
    public AppearancesFile Parse(string filePath)
    {
        return new AppearancesFile();
    }

    public void Save(AppearancesFile file, string filePath)
    {
        File.WriteAllBytes(filePath, Array.Empty<byte>());
    }
}
