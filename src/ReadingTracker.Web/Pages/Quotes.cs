namespace ReadingTracker.Web.Pages;

/// <summary>Something someone said about reading, and who.</summary>
public sealed record Quote(string Text, string Author);

/// <summary>
/// What the welcome screen says under the way in: not what the app does — the card above
/// already says that in a line — but something a reader would want to have read. One each
/// visit, at random, so the page is never quite the same twice.
/// </summary>
public static class Quotes
{
    public static readonly Quote[] All =
    [
        new("Reading is to the mind what exercise is to the body.", "Joseph Addison"),
        new("Some books are to be tasted, others to be swallowed, and some few to be chewed and digested.", "Francis Bacon"),
        new("I have always imagined that Paradise will be a kind of library.", "Jorge Luis Borges"),
        new("A classic is a book that has never finished saying what it has to say.", "Italo Calvino"),
        new("The reading of all good books is like a conversation with the finest minds of past centuries.", "René Descartes"),
        new("Once you learn to read, you will be forever free.", "Frederick Douglass"),
        new("Books are the quietest and most constant of friends; they are the most accessible and wisest of counsellors, and the most patient of teachers.", "Charles W. Eliot"),
        new("When I have a little money, I buy books; and if I have any left, I buy food and clothes.", "Erasmus"),
        new("I cannot live without books.", "Thomas Jefferson"),
        new("A book must be the axe for the frozen sea within us.", "Franz Kafka"),
        new("Books are a uniquely portable magic.", "Stephen King"),
        new("Until I feared I would lose it, I never loved to read. One does not love breathing.", "Harper Lee"),
        new("A reader lives a thousand lives before he dies. The man who never reads lives only one.", "George R. R. Martin"),
        new("The person, be it gentleman or lady, who has not pleasure in a good novel, must be intolerably stupid.", "Jane Austen"),
        new("Reading gives us someplace to go when we have to stay where we are.", "Mason Cooley"),
        new("One glance at a book and you're inside the mind of another person, maybe somebody dead for thousands of years.", "Carl Sagan"),
        new("The more that you read, the more things you will know. The more that you learn, the more places you'll go.", "Dr. Seuss"),
        new("Read the best books first, or you may not have a chance to read them at all.", "Henry David Thoreau"),
        new("Books are mirrors: you only see in them what you already have inside you.", "Carlos Ruiz Zafón"),
        new("A house without books is like a room without windows.", "Horace Mann"),
    ];

    public static Quote Pick(Random random) => All[random.Next(All.Length)];
}
