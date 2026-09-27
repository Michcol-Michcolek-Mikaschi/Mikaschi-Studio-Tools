namespace Modules.OldItemEditor.Services;

/// <summary>
/// Profile klienta pochodzące z trzech wtyczek oryginalnego OTTools ItemEditor.
/// MinorVersion nagłówka OTB wskazuje numer profilu, a nie numer klienta Tibii.
/// </summary>
public sealed record OldItemEditorClientProfile(
    uint OtbVersion,
    int ClientVersion,
    string DisplayName,
    uint DatSignature,
    uint SprSignature)
{
    public override string ToString() => $"Tibia {DisplayName} — OTB {OtbVersion}";
}

public static class OldItemEditorClientCatalog
{
    public static IReadOnlyList<OldItemEditorClientProfile> All { get; } =
    [
        new(7, 800, "8.00", 0x467FD7E6, 0x467F9E74),
        new(8, 810, "8.10", 0x475D3747, 0x475D0B01),
        new(9, 811, "8.11", 0x47F60E37, 0x47EBB9B2),
        new(10, 820, "8.20", 0x486905AA, 0x4868ECC9),
        new(11, 830, "8.30", 0x48DA1FB6, 0x48C8E712),
        new(12, 840, "8.40", 0x493D607A, 0x493D4E7C),
        new(13, 841, "8.41", 0x49B7CC19, 0x49B140EA),
        new(14, 842, "8.42", 0x49C233C9, 0x49B140EA),
        new(15, 850, "8.50 v1", 0x4A49C5EB, 0x4A44FD4E),
        new(15, 850, "8.50 v2", 0x4A4CC0DC, 0x4A44FD4E),
        new(15, 850, "8.50 v3", 0x4AE97492, 0x4ACB5230),
        new(16, 854, "8.54 v1", 0x4B1E2CAA, 0x4B1E2C87),
        new(16, 854, "8.54 v2", 0x4B0D46A9, 0x4B0D3AFF),
        new(17, 854, "8.54 v3", 0x4B28B89E, 0x4B1E2C87),
        new(18, 855, "8.55", 0x4B98FF53, 0x4B913871),
        new(19, 860, "8.60 v1", 0x4C28B721, 0x4C220594),
        new(20, 860, "8.60 v2", 0x4C2C7993, 0x4C220594),
        new(21, 861, "8.61", 0x4C6A4CBC, 0x4C63F145),
        new(22, 862, "8.62", 0x4C973450, 0x4C63F145),
        new(23, 870, "8.70", 0x4CFE22C5, 0x4CFD078A),
        new(24, 871, "8.71", 0x4D41979E, 0x4D3D65D0),
        new(25, 872, "8.72", 0x4DAD1A1A, 0x4DAD1A32),
        new(26, 873, "8.73", 0x4DBAA20B, 0x4DAD1A32),
        new(27, 900, "9.00", 0x4DBAA20B, 0x4DAD1A32),
        new(28, 910, "9.10", 0x4E12DAFF, 0x4E12DB27),
        new(29, 920, "9.20", 0x4E807C08, 0x4E807C23),
        new(30, 940, "9.40", 0x4EE71DE5, 0x4EE71E06),
        new(31, 944, "9.44 old", 0x4F0EEFBB, 0x4F0EEFEF),
        new(32, 944, "9.44 v1", 0x4F105168, 0x4F1051D7),
        new(33, 944, "9.44 v2", 0x4F16C0D7, 0x4F1051D7),
        new(34, 944, "9.44 v3", 0x4F3131CF, 0x4F3131F6),
        new(35, 946, "9.46", 0x4F6B341F, 0x4F5DCEF7),
        new(36, 950, "9.50", 0x4F75B7AB, 0x4F75B7CD),
        new(37, 952, "9.52", 0x4F857F6C, 0x4F857F8E),
        new(38, 953, "9.53", 0x4FA11252, 0x4FA11282),
        new(39, 954, "9.54", 0x4FD5956B, 0x4FD595B7),
        new(40, 960, "9.60", 0x4FFA74CC, 0x4FFA74F9),
        new(41, 961, "9.61", 0x50226F9D, 0x50226FBD),
        new(42, 963, "9.63", 0x503CB933, 0x503CB954),
        new(43, 970, "9.70", 0x5072A490, 0x5072A567),
        new(44, 980, "9.80", 0x50C70674, 0x50C70753),
        new(45, 981, "9.81", 0x50D1C5B6, 0x50D1C685),
        new(46, 982, "9.82", 0x512CAD09, 0x512CAD68),
        new(47, 983, "9.83", 0x51407B67, 0x51407BC7),
        new(48, 985, "9.85", 0x51641A1B, 0x51641A84),
        new(49, 986, "9.86", 0x5170E904, 0x5170E96F),
        new(50, 1010, "10.10", 0x51E3F8C3, 0x51E3F8E9),
        new(51, 1020, "10.20", 0x5236F129, 0x5236F14F),
        new(52, 1021, "10.21", 0x526A5068, 0x526A5090),
        new(53, 1030, "10.30", 0x52A59036, 0x52A5905F),
        new(54, 1031, "10.31", 0x52AED581, 0x52AED5A7),
        new(55, 1041, "10.41", 0x5383504E, 0x53835077),
        new(56, 1077, "10.77", 0x000038DE, 0x5525213D),
        new(57, 1098, "10.98", 0x000042A3, 0x57BBD603)
    ];

    public static OldItemEditorClientProfile? FindByOtbVersion(uint version) =>
        All.FirstOrDefault(profile => profile.OtbVersion == version);

    public static OldItemEditorClientProfile? FindBySignatures(uint dat, uint spr) =>
        All.FirstOrDefault(profile => profile.DatSignature == dat && profile.SprSignature == spr);
}
