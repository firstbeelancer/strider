namespace Strider.Core.Domain;

/// <summary>
/// Colour of the flag a message carries. The colour is what the user sees in
/// the message list: a colour bar on the row and the subject painted in the
/// same colour. Values are stored as lowercase strings in SQLite.
/// </summary>
public enum FlagColor
{
    /// <summary>No flag.</summary>
    None = 0,
    Red,
    Orange,
    Yellow,
    Green,
    Blue,
    Purple,
    Grey,
}

/// <summary>
/// The colours offered in the UI, in display order. <see cref="FlagColor.None"/>
/// is handled separately ("clear flag").
/// </summary>
public static class FlagColors
{
    public static readonly IReadOnlyList<FlagColor> All = new[]
    {
        FlagColor.Red,
        FlagColor.Orange,
        FlagColor.Yellow,
        FlagColor.Green,
        FlagColor.Blue,
        FlagColor.Purple,
        FlagColor.Grey,
    };

    /// <summary>Hex used for the flag bar and the coloured subject.</summary>
    public static string ToHex(FlagColor color) => color switch
    {
        FlagColor.Red => "#E5484D",
        FlagColor.Orange => "#F76B15",
        FlagColor.Yellow => "#E2A336",
        FlagColor.Green => "#30A46C",
        FlagColor.Blue => "#3E63DD",
        FlagColor.Purple => "#8E4EC6",
        FlagColor.Grey => "#8D8D8D",
        _ => "#000000",
    };

    public static string ToDisplayName(FlagColor color) => color switch
    {
        FlagColor.None => "No flag",
        FlagColor.Red => "Red",
        FlagColor.Orange => "Orange",
        FlagColor.Yellow => "Yellow",
        FlagColor.Green => "Green",
        FlagColor.Blue => "Blue",
        FlagColor.Purple => "Purple",
        FlagColor.Grey => "Grey",
        _ => color.ToString(),
    };

    /// <summary>Parses a stored value; unknown or empty becomes <see cref="FlagColor.None"/>.</summary>
    public static FlagColor Parse(string? value) =>
        Enum.TryParse<FlagColor>(value, ignoreCase: true, out var color) && Enum.IsDefined(color)
            ? color
            : FlagColor.None;
}
