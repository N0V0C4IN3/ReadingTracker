// The reading calendar's whole year on a phone (Pages/Reading.razor): a strip of weeks wider
// than the screen, scrolled so the latest weeks are the ones in view — where the reader just was.

export function showLatest(weeks) {
    weeks.scrollLeft = weeks.scrollWidth;
}
