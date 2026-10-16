// Brings one thing inside a scrolling box into view: the Quote a pin on the Quotes sheet's map was
// tapped for. Only as far as it takes to see the whole of it — the row's scroll-margin keeps a
// little room past its edge — and gliding there unless the reader asked for stillness.

export function reveal(el) {
    if (!el?.isConnected) {
        return;
    }

    const still = window.readingTracker?.prefersStillness?.() ?? false;
    el.scrollIntoView({ block: 'nearest', behavior: still ? 'auto' : 'smooth' });
}
