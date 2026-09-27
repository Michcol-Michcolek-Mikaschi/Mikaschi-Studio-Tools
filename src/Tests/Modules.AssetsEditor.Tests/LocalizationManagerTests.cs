using FluentAssertions;
using Narzedzia.Contracts.Localization;
using System.Globalization;

namespace Modules.AssetsEditor.Tests;

[Collection("Localization")]
public sealed class LocalizationManagerTests
{
    [Theory]
    [InlineData("Edytor Assetów", "Assets Editor")]
    [InlineData("Nowy OTB", "New OTB")]
    [InlineData("PALETA ITEMÓW", "ITEM PALETTE")]
    [InlineData("Otwórz DAT+SPR", "Open DAT+SPR")]
    [InlineData("w budowie", "under construction")]
    [InlineData("🪄 Generator Sprite'ów AI (ComfyUI)", "🪄 AI Sprite Generator (ComfyUI)")]
    [InlineData("Skaluj batch", "Batch resize")]
    [InlineData("Wytnij klatki", "Cut frames")]
    [InlineData("Blokuje przejście", "Blocks movement")]
    [InlineData("Czytanie z dystansu", "Readable from distance")]
    [InlineData("Wymuś use", "Force use")]
    [InlineData("Folder docelowy", "Target folder")]
    [InlineData("Eksportuj minimapę RME", "Export RME minimap")]
    [InlineData("Importuj mapę OTBM", "Import OTBM map")]
    [InlineData("Obraz PNG", "PNG image")]
    [InlineData("Eksport obiektów", "Export objects")]
    [InlineData("Szukaj nazwy lub Server ID…", "Search by name or Server ID…")]
    [InlineData("Wyczyść kafelek", "Clear tile")]
    [InlineData("Publikowanie mapy, assets i indeksów…", "Publishing the map, assets, and indexes…")]
    [InlineData(
        "Przerwij wczytywanie bez podmieniania aktualnej mapy i assets",
        "Cancel loading without replacing the current map and assets")]
    public void TranslateToEnglish_CoversEveryModule(string polish, string english)
    {
        LocalizationManager.TranslateToEnglish(polish).Should().Be(english);
    }

    [Fact]
    public void TranslateToEnglish_HandlesDynamicStatusText()
    {
        LocalizationManager.TranslateToEnglish("Zapisano Tibia.dat.")
            .Should().Be("Saved Tibia.dat.");
        LocalizationManager.TranslateToEnglish("Błąd: connection refused")
            .Should().Be("Error: connection refused");
        LocalizationManager.TranslateToEnglish(
                "Format DAT: 8.55–9.86. Sprite ID 32-bit, animacje stare, frame groups nie.")
            .Should().Be(
                "DAT format: 8.55–9.86. Sprite ID 32-bit, old animations, frame groups disabled.");
    }

    [Theory]
    [InlineData(
        "Wczytano assets: 10 przedm., 2 strojów, 3 efektów, 4 pocisków, 5 arkuszy sprite.",
        "Loaded assets: 10 items, 2 outfits, 3 effects, 4 missiles, 5 sprite sheets.")]
    [InlineData(
        "Mapa: 100×100, 50 kafelków, 2 miast, 1 waypoint-ów, items 3.57.",
        "Map: 100×100, 50 tiles, 2 towns, 1 waypoints, items 3.57.")]
    [InlineData(
        "✓ ComfyUI dostępny pod http://127.0.0.1:8188",
        "✓ ComfyUI available at http://127.0.0.1:8188")]
    [InlineData(
        "Gotowe! Przetworzono: 4/5, Błędy: 1",
        "Ready! Processed: 4/5, Errors: 1")]
    [InlineData(
        "Wycięto 12 klatek z: sheet.png",
        "Cut 12 frames from: sheet.png")]
    public void TranslateToEnglish_TranslatesFormattedModuleMessages(string polish, string english)
    {
        LocalizationManager.TranslateToEnglish(polish).Should().Be(english);
    }

    [Theory]
    [InlineData("modułowe narzędzia OTS", "herramientas OTS modulares")]
    [InlineData("Edytor Assetów", "Editor de assets")]
    [InlineData("Old Assets Editor", "Editor de assets antiguos")]
    [InlineData("Item Editor", "Editor de objetos")]
    [InlineData("Old Item Editor", "Editor de objetos clásico")]
    [InlineData("Converter", "Conversor")]
    [InlineData("Sprite Resizer", "Redimensionador de sprites")]
    [InlineData("Sprite Sheet Cutter", "Cortador de hojas de sprites")]
    [InlineData("Map Editor", "Editor de mapas")]
    [InlineData("Generator Sprite'ów AI", "Generador de sprites con IA")]
    [InlineData("Otwórz folder", "Abrir carpeta")]
    [InlineData("Wyczyść", "Limpiar")]
    [InlineData("Brak mapy.", "No hay ningún mapa.")]
    public void TranslateToSpanish_CoversShellModulesAndSharedMessages(string source, string spanish)
    {
        LocalizationManager.TranslateToSpanish(source).Should().Be(spanish);
    }

    [Fact]
    public void SetLanguage_ChangesTranslationAndCultureForEverySupportedLanguage()
    {
        try
        {
            LocalizationManager.SetLanguage(AppLanguage.Polish);
            LocalizationManager.Translate("Otwórz").Should().Be("Otwórz");
            CultureInfo.CurrentCulture.Name.Should().Be("pl-PL");

            LocalizationManager.SetLanguage(AppLanguage.English);
            LocalizationManager.Translate("Otwórz").Should().Be("Open");
            CultureInfo.CurrentCulture.Name.Should().Be("en-US");

            LocalizationManager.SetLanguage(AppLanguage.Spanish);
            LocalizationManager.Translate("Otwórz").Should().Be("Abrir");
            CultureInfo.CurrentCulture.Name.Should().Be("es-ES");
        }
        finally
        {
            LocalizationManager.SetLanguage(AppLanguage.Polish);
        }
    }

    [Fact]
    public void TranslateToSpanish_UsesSpanishForSharedFallbackText()
    {
        LocalizationManager.TranslateToSpanish("w budowie").Should().Be("en desarrollo");
    }

    [Theory]
    [InlineData("Show all floors", "Pokaż wszystkie piętra")]
    [InlineData("Map Properties", "Właściwości mapy")]
    [InlineData("House Editor", "Edytor domów")]
    [InlineData("Reload data (F5)", "Przeładuj dane (F5)")]
    [InlineData("Animate Always", "Zawsze animuj")]
    [InlineData("Tile Flags", "Flagi pola")]
    [InlineData("CurrencyName", "Nazwa waluty")]
    [InlineData("Waypoint Manager", "Menedżer waypointów")]
    [InlineData("Mount head/body", "Głowa/tułów wierzchowca")]
    [InlineData("Trade As", "Handluj jako")]
    [InlineData("Walking / ruch", "Chodzenie / ruch")]
    public void TranslateToPolish_NormalizesLegacyEnglishLabels(string source, string polish)
    {
        LocalizationManager.TranslateToPolish(source).Should().Be(polish);
    }

    [Theory]
    [InlineData("Show all floors", "Mostrar todos los pisos")]
    [InlineData("Map Properties", "Propiedades del mapa")]
    [InlineData("Odczytywanie mapy: 1 234 kafelków…", "Leyendo el mapa: 1 234 casillas…")]
    [InlineData("Budowanie minimapy Z=7 w tle…", "Creando minimapa Z=7 en segundo plano…")]
    [InlineData("Wczytano 25 obiektów.", "Cargado 25 objetos.")]
    [InlineData("Animate Always", "Animar siempre")]
    [InlineData("Tile Flags", "Propiedades de la casilla")]
    [InlineData("CurrencyName", "Nombre de moneda")]
    [InlineData("Cleanup invalid items", "Limpiar objetos no válidos")]
    [InlineData("Eksportuj minimapę RME", "Exportar minimapa RME")]
    [InlineData("Importuj mapę OTBM", "Importar mapa OTBM")]
    [InlineData("Publikowanie mapy, assets i indeksów…", "Publicando el mapa, los assets y los índices…")]
    [InlineData(
        "Przerwij wczytywanie bez podmieniania aktualnej mapy i assets",
        "Cancelar la carga sin reemplazar el mapa ni los assets actuales")]
    [InlineData("Mount legs/feet", "Piernas/pies de la montura")]
    [InlineData("Walking / ruch", "Caminar / movimiento")]
    [InlineData("Przełącz Protection Zone", "Alternar zona de protección")]
    public void TranslateToSpanish_CoversLegacyEnglishAndFormattedStatuses(string source, string spanish)
    {
        LocalizationManager.TranslateToSpanish(source).Should().Be(spanish);
    }
}
