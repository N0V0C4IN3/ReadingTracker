// Where the reader is on the shelf, for going back to it after a visit to another page
// (Services/ShelfPlaceKeeper.cs): how far the page is scrolled, how far the results are scrolled
// inside the search dock, and the dock's height where it is a sheet (js/sheet.js's data-sheet).

export function where(dock) {
    return {
        page: window.scrollY,
        results: dock?.querySelector('.search__found')?.scrollTop ?? 0,
        size: dock?.dataset.sheet ?? null,
    };
}

// Puts the reader back where where() found them. Called once the shelf and its results are
// rendered again, and the dock attached at its old height, so there is something to scroll.
export function returnTo(dock, place) {
    const found = dock?.querySelector('.search__found');
    if (found) {
        found.scrollTop = place.results;
    }
    window.scrollTo({ top: place.page, behavior: 'instant' });
}
