using ReadingTracker.Library.Entries;

namespace ReadingTracker.Library.Tests;

/// <summary>
/// Saying a reading in the other TrackingMethod, and in pages. A conversion is an approximation
/// of where the reader is, so each direction rounds to what a person says; adding a day or a
/// year up is not, so pages read stay unrounded.
/// </summary>
public sealed class UnitConversionTests
{
    [Fact]
    public void A_position_in_the_unit_asked_for_is_returned_as_it_is()
    {
        Assert.Equal(17.5m, UnitConversion.Convert(17.5m, TrackingMethod.Percentage, TrackingMethod.Percentage, null));
        Assert.Equal(42m, UnitConversion.Convert(42m, TrackingMethod.Pages, TrackingMethod.Pages, null));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public void A_different_unit_with_no_length_has_no_honest_answer(int? length)
    {
        Assert.Null(UnitConversion.Convert(120m, TrackingMethod.Pages, TrackingMethod.Percentage, length));
        Assert.Null(UnitConversion.Convert(20m, TrackingMethod.Percentage, TrackingMethod.Pages, length));
    }

    [Fact]
    public void Pages_become_a_percentage_to_a_tenth()
    {
        // 350 of 705 is 49.645390070921985...%, which is noise, not precision.
        Assert.Equal(49.6m, UnitConversion.Convert(350m, TrackingMethod.Pages, TrackingMethod.Percentage, 705));
    }

    [Fact]
    public void A_percentage_becomes_a_whole_page_half_away_from_nought()
    {
        // A reader is on a page, not four fifths of the way into one: 17.5% of 300 is 52.5.
        Assert.Equal(53m, UnitConversion.Convert(17.5m, TrackingMethod.Percentage, TrackingMethod.Pages, 300));
    }

    [Fact]
    public void Pages_read_in_pages_are_the_amount()
    {
        Assert.Equal(22m, UnitConversion.PagesRead(22m, TrackingMethod.Pages, null));
        Assert.Equal(22m, UnitConversion.PagesRead(22m, TrackingMethod.Pages, 300));
    }

    [Fact]
    public void A_percentage_read_is_pages_unrounded_so_the_small_ones_a_device_reports_add_up()
    {
        // A tenth of a percent of 300 pages is 0.3 of one, which rounding each report first
        // would lose to nothing.
        Assert.Equal(37.5m, UnitConversion.PagesRead(12.5m, TrackingMethod.Percentage, 300));
        Assert.Equal(0.3m, UnitConversion.PagesRead(0.1m, TrackingMethod.Percentage, 300));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public void A_percentage_of_a_book_of_no_known_length_has_no_pages(int? length)
    {
        Assert.Null(UnitConversion.PagesRead(12.5m, TrackingMethod.Percentage, length));
    }
}
