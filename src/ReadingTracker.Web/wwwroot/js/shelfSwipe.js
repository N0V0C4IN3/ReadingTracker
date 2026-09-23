// On a phone the shelf's status tabs can be swiped through as well as tapped: a sideways swipe
// across the shelf moves to the tab beside the chosen one, left for the next, right for the one
// before. The swipe presses that tab's button, so Blazor hears it the same way it hears a tap,
// and the strip is scrolled to keep the new tab in view.
//
// Listened for on the document rather than on the shelf, so it is attached once and outlives the
// shelf coming and going with sign-in; a touch that does not start on the shelf is ignored.

const phone = window.matchMedia('(max-width: 40rem)');

// Things a sideways drag already means something to, or that sit over the shelf: a touch that
// starts in one of these is theirs, not the shelf's.
const theirs = 'input, textarea, select, .filters, .sheet, .sheet__scrim, .status__menu, .status__scrim';

// Far enough sideways to be meant, and more sideways than down, so a scroll that drifts is still
// a scroll.
const distance = 60;
const slant = 1.5;

let attached = false;
let start = null;

export function attach() {
    if (attached) {
        return;
    }

    attached = true;
    document.addEventListener('touchstart', began, { passive: true });
    document.addEventListener('touchend', ended, { passive: true });
    document.addEventListener('touchcancel', () => start = null, { passive: true });
}

function began(e) {
    start = null;
    if (!phone.matches || e.touches.length !== 1) {
        return;
    }

    const target = e.target instanceof Element ? e.target : null;
    if (!target?.closest('.shelf') || target.closest(theirs)) {
        return;
    }

    const touch = e.touches[0];
    start = { x: touch.clientX, y: touch.clientY };
}

function ended(e) {
    if (!start || e.changedTouches.length !== 1) {
        start = null;
        return;
    }

    const touch = e.changedTouches[0];
    const dx = touch.clientX - start.x;
    const dy = touch.clientY - start.y;
    start = null;

    if (Math.abs(dx) < distance || Math.abs(dx) < Math.abs(dy) * slant) {
        return;
    }

    const on = document.querySelector('.shelf .filters .chip--on');
    const next = dx < 0 ? on?.nextElementSibling : on?.previousElementSibling;
    if (!next || next.disabled) {
        return;
    }

    next.click();
    reveal(next);
}

// Only the strip moves: scrollIntoView would also scroll the page to the strip, and a reader
// halfway down the shelf wants to stay where they are.
function reveal(tab) {
    const strip = tab.parentElement;
    const inset = 16;
    const s = strip.getBoundingClientRect();
    const t = tab.getBoundingClientRect();

    if (t.left < s.left + inset) {
        strip.scrollBy({ left: t.left - s.left - inset, behavior: 'smooth' });
    } else if (t.right > s.right - inset) {
        strip.scrollBy({ left: t.right - s.right + inset, behavior: 'smooth' });
    }
}
