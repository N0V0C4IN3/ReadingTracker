namespace ReadingTracker.Web.Components;

/// <summary>
/// A jacket in a cover flow (FlowStage): who it is, what the drawn jacket says, the photograph laid
/// over it if there is one, and which of the stylesheet's drawn jackets it is (cover--0 to 5).
/// CoverStyle is anything the flow's own stylesheet wants on the jacket: the shelf's flow hands it
/// the photograph again, to colour the book's spine with.
/// </summary>
public sealed record FlowJacket(string Key, string Title, string Author, string? Cover, int Tone, string? CoverStyle = null)
{
    /// <summary>How many jacket designs the stylesheet draws.</summary>
    public const int Tones = 6;
}
