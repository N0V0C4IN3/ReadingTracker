namespace ReadingTracker.Library.Entries;

/// <summary>
/// How many books a reader means to finish in a year. Theirs alone, one per year, and only the
/// target: how many they have finished is worked out from their entries' FinishedOn, never
/// kept here, so the two can never disagree.
/// </summary>
public sealed class ReadingGoal
{
    public required string ReaderId { get; init; }

    public int Year { get; init; }

    public int Books { get; set; }
}
