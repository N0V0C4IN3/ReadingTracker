namespace ReadingTracker.Web.Services;

/// <summary>How the page writes the numbers Library hands it.</summary>
public static class Figures
{
    /// <summary>A whole number as itself, and anything else to one decimal: "12", "12.5", never "12.0".</summary>
    public static string Trim(decimal value) =>
        value == decimal.Truncate(value) ? decimal.Truncate(value).ToString() : value.ToString("0.#");
}
