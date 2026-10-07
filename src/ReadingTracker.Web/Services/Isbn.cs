namespace ReadingTracker.Web.Services;

/// <summary>
/// ISBNs as other sites' exports write them and as this app's export writes them. Shape only, as
/// Catalog's own check is: ten characters ending in a digit or an X, or thirteen digits, with the
/// hyphens and spaces people write them with stripped. And the two forms from each other: every
/// ISBN-10 has an ISBN-13, under 978; only a 978 ISBN-13 has an ISBN-10.
/// </summary>
public static class Isbn
{
    /// <summary>The bare ISBN, if the text holds one; null if it does not.</summary>
    public static string? Normalise(string? text)
    {
        var bare = text?.Replace("-", "").Replace(" ", "").ToUpperInvariant() ?? "";

        var looksLikeOne = bare.Length switch
        {
            13 => bare.All(char.IsAsciiDigit),
            10 => bare[..9].All(char.IsAsciiDigit) && (char.IsAsciiDigit(bare[9]) || bare[9] == 'X'),
            _ => false,
        };

        return looksLikeOne ? bare : null;
    }

    /// <summary>An ISBN's thirteen-digit form; null if the text is not an ISBN.</summary>
    public static string? ThirteenOf(string? text) => Normalise(text) switch
    {
        { Length: 10 } ten => "978" + ten[..9] + Check13("978" + ten[..9]),
        var thirteen => thirteen,
    };

    /// <summary>An ISBN's ten-character form; null if the text is not an ISBN or is a 979 ISBN-13, which has none.</summary>
    public static string? TenOf(string? text) => Normalise(text) switch
    {
        { Length: 13 } thirteen when thirteen.StartsWith("978", StringComparison.Ordinal) => thirteen[3..12] + Check10(thirteen[3..12]),
        { Length: 13 } => null,
        var ten => ten,
    };

    /// <summary>Weights 1 and 3 in turn; the check brings the sum to a multiple of ten.</summary>
    private static char Check13(string twelve)
    {
        var sum = twelve.Select((digit, at) => (digit - '0') * (at % 2 == 0 ? 1 : 3)).Sum();
        return (char)('0' + (10 - sum % 10) % 10);
    }

    /// <summary>Weights 10 down to 2; the check brings the sum to a multiple of eleven, and ten is X.</summary>
    private static char Check10(string nine)
    {
        var sum = nine.Select((digit, at) => (digit - '0') * (10 - at)).Sum();
        var check = (11 - sum % 11) % 11;
        return check == 10 ? 'X' : (char)('0' + check);
    }
}
