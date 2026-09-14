namespace ReadingTracker.Catalog.Books;

/// <summary>
/// Recognises an ISBN by its shape, so a reader can type one into the same box as a title and
/// be looked up exactly rather than searched for as words. Shape only: ten characters ending in
/// a digit or an X, or thirteen digits, with the hyphens and spaces people write them with
/// allowed and stripped. No checksum — a mistyped ISBN should still go to the ISBN lookup and
/// come back as "not found", not be searched for as a thirteen-digit title.
/// </summary>
public static class Isbn
{
    public static bool TryNormalise(string? text, out string isbn)
    {
        isbn = string.Empty;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var bare = text.Replace("-", "").Replace(" ", "").ToUpperInvariant();

        var looksLikeOne = bare.Length switch
        {
            13 => bare.All(char.IsAsciiDigit),
            10 => bare[..9].All(char.IsAsciiDigit) && (char.IsAsciiDigit(bare[9]) || bare[9] == 'X'),
            _ => false,
        };

        if (!looksLikeOne)
        {
            return false;
        }

        isbn = bare;
        return true;
    }
}
