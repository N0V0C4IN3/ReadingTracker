using ReadingTracker.Catalog.Books;

namespace ReadingTracker.Catalog.Tests;

/// <summary>
/// A provider's description made into the plain paragraphs a Book stores. The text here is taken
/// from descriptions providers really sent: the mess is the publisher's jacket copy pasted in with
/// whatever formatting it had on its way through a retailer's feed.
/// </summary>
public sealed class DescriptionTextTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n  ")]
    [InlineData("<p> </p>")]
    [InlineData("__________")]
    public void Nothing_to_say_is_no_description(string? description) =>
        Assert.Null(DescriptionText.Clean(description));

    [Fact]
    public void Html_becomes_paragraphs_of_plain_text() =>
        Assert.Equal(
            "Darrow is a Red.\n\nHe works all day & believes. Yet he lives.",
            DescriptionText.Clean("<p>Darrow is a Red.</p><p>He works <b>all day</b> &amp; believes.<br>Yet he lives.</p>"));

    [Fact]
    public void A_rule_of_underscores_between_sections_is_a_paragraph_break() =>
        Assert.Equal(
            "Out of the author of JONATHAN STRANGE & MR NORRELL.\n\nPiranesi lives in the House. Perhaps he always has.",
            DescriptionText.Clean(
                "Out of the author of JONATHAN STRANGE & MR NORRELL. __________________________________ Piranesi lives in the House. Perhaps he always has."));

    [Theory]
    [InlineData("First part. * * * Second part.")]
    [InlineData("First part.\n\n-----\n\nSecond part.")]
    [InlineData("First part. ~~~~~~ Second part.")]
    [InlineData("First part. ====== Second part.")]
    public void Any_rule_of_three_or_more_is_a_paragraph_break(string description) =>
        Assert.Equal("First part.\n\nSecond part.", DescriptionText.Clean(description));

    [Fact]
    public void A_double_dash_is_punctuation_not_a_rule() =>
        Assert.Equal(
            "A mentally challenged man -- he hopes -- is loved.",
            DescriptionText.Clean("A mentally challenged man -- he hopes -- is loved."));

    [Theory]
    [InlineData("**Out October 2026**", "Out October 2026")]
    [InlineData("A __long-lost__ chapter", "A long-lost chapter")]
    [InlineData("The sequel to *Jonathan Strange* is here.", "The sequel to Jonathan Strange is here.")]
    [InlineData("The sequel to _Jonathan Strange_, at last.", "The sequel to Jonathan Strange, at last.")]
    public void Markdown_emphasis_loses_its_markers(string description, string expected) =>
        Assert.Equal(expected, DescriptionText.Clean(description));

    [Theory]
    [InlineData("snake_case_name stays")]
    [InlineData("5 * 3 * 2 is thirty")]
    public void Underscores_and_stars_inside_words_or_sums_are_left_alone(string description) =>
        Assert.Equal(description, DescriptionText.Clean(description));

    [Fact]
    public void A_retailer_tag_stuck_to_the_end_of_a_cut_off_sentence_is_dropped() =>
        Assert.Equal(
            "Harry must navigate each treacherous test alone, push.",
            DescriptionText.Clean("Harry must navigate each treacherous test alone, push[Bokinfo]."));

    [Fact]
    public void A_bracket_with_a_space_before_it_is_the_author_s_own_and_stays() =>
        Assert.Equal("He wrote it [sic] twice.", DescriptionText.Clean("He wrote it [sic] twice."));

    [Theory]
    [InlineData("\"The final adventure in the series\"--Provided by publisher.", "The final adventure in the series.")]
    [InlineData("The final adventure in the series. -- Publisher's description", "The final adventure in the series.")]
    [InlineData("The final adventure.\n\n--Provided by publisher.", "The final adventure.")]
    public void A_library_catalogue_s_attribution_is_dropped(string description, string expected) =>
        Assert.Equal(expected, DescriptionText.Clean(description));

    [Fact]
    public void Sentences_run_together_get_their_space_back() =>
        Assert.Equal(
            "Harry is running out of time. These new editions feature new jackets.",
            DescriptionText.Clean("Harry is running out of time.These new editions feature new jackets."));

    [Theory]
    [InlineData("J.K. Rowling wrote it.")]
    [InlineData("Visit Amazon.com today.")]
    [InlineData("It is 3.14 exactly.")]
    public void Initials_domains_and_decimals_are_not_run_together_sentences(string description) =>
        Assert.Equal(description, DescriptionText.Clean(description));

    [Fact]
    public void A_space_before_a_full_stop_or_comma_goes() =>
        Assert.Equal("Roke, Perilane, Osskil.", DescriptionText.Clean("Roke , Perilane, Osskil ."));

    [Fact]
    public void A_space_before_french_punctuation_stays() =>
        Assert.Equal("Qui est-ce ? Personne !", DescriptionText.Clean("Qui est-ce ? Personne !"));

    [Fact]
    public void A_spaced_out_ellipsis_is_one_character() =>
        Assert.Equal(
            "A glorious account … All those who love a tale.",
            DescriptionText.Clean("A glorious account . . . All those who love a tale."));

    [Theory]
    [InlineData("**PRE-ORDER NOW - THE BISHOP OF DURHAM ATTEMPTS TO SURRENDER THE CITY. Out October 2026**")]
    [InlineData("Pre-order the sequel today!")]
    [InlineData("At the Publisher's request, this title is being sold without Digital Rights Management Software (DRM) applied.")]
    public void A_retailer_s_notice_is_dropped_as_a_whole_paragraph(string notice) =>
        Assert.Equal("The story.", DescriptionText.Clean($"The story.\n\n{notice}"));

    [Fact]
    public void A_notice_s_words_inside_the_story_are_the_story() =>
        Assert.Equal(
            "She could pre-order her fate, and did.",
            DescriptionText.Clean("She could pre-order her fate, and did."));

    [Fact]
    public void Clean_text_comes_out_as_it_went_in()
    {
        const string clean = "Harry August is on his deathbed. Again.\n\n“I nearly missed you, Doctor August,” she says.";

        Assert.Equal(clean, DescriptionText.Clean(clean));
        Assert.Equal(clean, DescriptionText.Clean(DescriptionText.Clean(clean)));
    }

    [Fact]
    public void Piranesi_as_google_books_sent_it()
    {
        const string sent =
            "Winner of the 2021 Women's Prize for Fiction A SUNDAY TIMES & NEW YORK TIMES BESTSELLER\n\n" +
            "The spectacular new novel from the bestselling author of JONATHAN STRANGE & MR NORRELL, 'one of our greatest living authors' NEW YORK MAGAZINE __________________________________ Piranesi lives in the House. Perhaps he always has.\n\n" +
            "The Beauty of the House is immeasurable; its Kindness infinite. __________________________________ 'Piranesi astonished me.' MADELINE MILLER\n\n" +
            "**PRE-ORDER NOW - THE BISHOP OF DURHAM ATTEMPTS TO SURRENDER THE CITY, a long-lost chapter in the lore of Jonathan Strange & Mr Norrell. Out October 2026**";

        Assert.Equal(
            "Winner of the 2021 Women's Prize for Fiction A SUNDAY TIMES & NEW YORK TIMES BESTSELLER\n\n" +
            "The spectacular new novel from the bestselling author of JONATHAN STRANGE & MR NORRELL, 'one of our greatest living authors' NEW YORK MAGAZINE\n\n" +
            "Piranesi lives in the House. Perhaps he always has.\n\n" +
            "The Beauty of the House is immeasurable; its Kindness infinite.\n\n" +
            "'Piranesi astonished me.' MADELINE MILLER",
            DescriptionText.Clean(sent));
    }
}
