namespace Modules.SpriteGenerator.ViewModels;

/// <summary>Kategoria sprite'a — z domyślnymi parametrami zgodnymi z konwencjami OTClient.</summary>
public sealed record SpriteCategoryPreset(
    string Name,
    int Width,
    int Height,
    int Directions,
    IReadOnlyList<string> AnimationTypes,
    int FramesPerAnimation,
    string PromptHint)
{
    /// <summary>Outfit (postać) — 64x64 base → down-sample do 32x32 pixel-art, 4 kierunki, idle/walk/attack/cast/die.</summary>
    public static readonly SpriteCategoryPreset Outfit = new(
        Name: "Outfit (postać)",
        Width: 64, Height: 64,
        Directions: 4,
        AnimationTypes: new[] { "idle", "walk", "attack", "cast", "die" },
        FramesPerAnimation: 4,
        PromptHint: "fantasy character, pixel-art, top-down view, transparent background");

    /// <summary>Item (przedmiot) — statyczny 32x32 lub 64x64, 1 klatka, 1 kierunek.</summary>
    public static readonly SpriteCategoryPreset Item = new(
        Name: "Item (przedmiot)",
        Width: 32, Height: 32,
        Directions: 1,
        AnimationTypes: new[] { "static" },
        FramesPerAnimation: 1,
        PromptHint: "fantasy item, pixel-art, isometric view, transparent background");

    /// <summary>Effect (efekt magiczny) — 32x32, 6 klatek loop, 1 kierunek.</summary>
    public static readonly SpriteCategoryPreset Effect = new(
        Name: "Effect (efekt)",
        Width: 32, Height: 32,
        Directions: 1,
        AnimationTypes: new[] { "loop" },
        FramesPerAnimation: 6,
        PromptHint: "magic effect animation, pixel-art, glowing, transparent background");

    /// <summary>Missile (pocisk) — 32x32, 4 kierunki, 4 klatki.</summary>
    public static readonly SpriteCategoryPreset Missile = new(
        Name: "Missile (pocisk)",
        Width: 32, Height: 32,
        Directions: 4,
        AnimationTypes: new[] { "flight" },
        FramesPerAnimation: 4,
        PromptHint: "flying projectile, pixel-art, motion blur, transparent background");

    public static readonly IReadOnlyList<SpriteCategoryPreset> All =
        new[] { Outfit, Item, Effect, Missile };
}
