// On a phone the shelf's status tabs can be swiped through as well as tapped: a sideways swipe
// across the shelf moves to the tab beside the chosen one, left for the next, right for the one
// before. The list follows the finger while it is down — held back at either end, where there is
// no tab to go to — and on letting go either slides away and brings the next tab's list in from
// the other side, or springs back. The change itself is a press of that tab's button, so Blazor
// hears it the same way it hears a tap, and the strip is scrolled to keep the new tab in view.
//
// Listened for on the document rather than on the shelf, so it is attached once and outlives the
// shelf coming and going with sign-in; a touch that does not start on the shelf is ignored. The
// listeners are passive: the list is marked touch-action: pan-y in app.css, so a drag that
// starts sideways is never also a scroll, and nothing here has to hold the page's scrolling up.

const phone = window.matchMedia('(max-width: 40rem)');
const still = window.matchMedia('(prefers-reduced-motion: reduce)');

// Things a sideways drag already means something to, or that sit over the shelf: a touch that
// starts in one of these is theirs, not the shelf's.
const theirs = 'input, textarea, select, .filters, .sheet, .sheet__scrim, .status__menu, .status__scrim';

// The list that moves: the cards, or the line saying there are none.
const listSelector = '.shelf > .entries, .shelf > .empty';

// How far before the direction is decided, how much more sideways than down a drag must be to
// be a swipe, and how far (or how fast, in px/ms) it must go to change the tab when let go.
const decide = 10;
const slant = 1.2;
const distance = 70;
const flick = 0.45;

// How much of the finger's travel the list follows where there is no tab to go to.
const resistance = 0.25;

let attached = false;
let drag = null;

export function attach() {
    if (attached) {
        return;
    }

    attached = true;
    document.addEventListener('touchstart', began, { passive: true });
    document.addEventListener('touchmove', moved, { passive: true });
    document.addEventListener('touchend', ended, { passive: true });
    document.addEventListener('touchcancel', cancelled, { passive: true });
}

function began(e) {
    drag = null;
    if (!phone.matches || e.touches.length !== 1) {
        return;
    }

    const target = e.target instanceof Element ? e.target : null;
    if (!target?.closest('.shelf') || target.closest(theirs)) {
        return;
    }

    const on = document.querySelector('.shelf .filters .chip--on');
    if (!on || on.disabled) {
        return;
    }

    const touch = e.touches[0];
    drag = {
        x: touch.clientX,
        y: touch.clientY,
        t: e.timeStamp,
        dx: 0,
        sideways: null,
        on,
        list: null,
    };
}

function moved(e) {
    if (!drag || e.touches.length !== 1) {
        return;
    }

    const touch = e.touches[0];
    const dx = touch.clientX - drag.x;
    const dy = touch.clientY - drag.y;

    if (drag.sideways === null) {
        if (Math.hypot(dx, dy) < decide) {
            return;
        }

        drag.sideways = Math.abs(dx) > Math.abs(dy) * slant;
        if (!drag.sideways) {
            drag = null;
            return;
        }

        drag.list = document.querySelector(listSelector);
        if (!drag.list) {
            drag = null;
            return;
        }

        drag.list.style.transition = 'none';
        drag.list.style.willChange = 'transform, opacity';
    }

    drag.dx = dx;
    drag.lastT = e.timeStamp;
    const toward = neighbour(drag.on, dx);
    const shown = toward ? dx : dx * resistance;
    paint(drag.list, shown, toward ? Math.abs(dx) : 0);
}

function ended(e) {
    const gesture = drag;
    drag = null;
    if (!gesture?.sideways || !gesture.list) {
        return;
    }

    const elapsed = Math.max(1, (gesture.lastT ?? e.timeStamp) - gesture.t);
    const fast = Math.abs(gesture.dx) / elapsed > flick && Math.abs(gesture.dx) > distance / 2;
    const next = neighbour(gesture.on, gesture.dx);

    if (next && (Math.abs(gesture.dx) >= distance || fast)) {
        turn(gesture.list, next, Math.sign(gesture.dx));
    } else {
        settle(gesture.list);
    }
}

function cancelled() {
    const gesture = drag;
    drag = null;
    if (gesture?.list) {
        settle(gesture.list);
    }
}

// The tab a drag this way would move to: left (negative) for the next, right for the one before.
function neighbour(on, dx) {
    const next = dx < 0 ? on.nextElementSibling : on.previousElementSibling;
    return next && !next.disabled ? next : null;
}

// Where the list is while the finger is down: moved with it, fading a little the further it goes.
function paint(list, x, travelled) {
    const fade = Math.min(0.45, travelled / window.innerWidth);
    list.style.transform = `translateX(${x}px)`;
    list.style.opacity = String(1 - fade);
}

// Back to where it was, and every inline style cleared once it is there: a transform left on the
// list, even a translate of nothing, would make it the containing block of the sheets the cards
// open, and they would be pinned to the list rather than to the screen.
function settle(list) {
    if (still.matches) {
        clear(list);
        return;
    }

    list.style.transition = 'transform 240ms cubic-bezier(0.22, 1, 0.36, 1), opacity 240ms ease';
    list.style.transform = 'translateX(0)';
    list.style.opacity = '1';
    afterTransition(list, 260, () => clear(list));
}

// Away the way it was going, the tab pressed, and the next tab's list in from the other side.
async function turn(list, next, direction) {
    reveal(next);

    if (still.matches) {
        clear(list);
        next.click();
        return;
    }

    const width = window.innerWidth;
    list.style.transition = 'transform 170ms cubic-bezier(0.4, 0, 1, 1), opacity 170ms ease-in';
    list.style.transform = `translateX(${direction * width * 0.6}px)`;
    list.style.opacity = '0';
    await new Promise(done => afterTransition(list, 190, done));

    // Kept out of sight, not cleared, while Blazor swaps the list: a card both tabs share (a
    // Reading card, going to All) keeps its element, and a cleared list would show it back in
    // place for the frames in between — there, then gone, then sliding in. The transform goes
    // now, for the sheets' sake; the opacity is the arrival's to take over below.
    list.style.transition = 'none';
    list.style.transform = '';
    list.style.willChange = '';
    next.click();

    // Blazor has the new list in the page by the frame after next; it may be the same element or,
    // where one tab has cards and the other none, a different one.
    await frame();
    await frame();
    const arriving = document.querySelector(listSelector);
    if (!arriving) {
        return;
    }

    arriving.style.transition = 'none';
    arriving.style.transform = `translateX(${-direction * 48}px)`;
    arriving.style.opacity = '0';
    arriving.getBoundingClientRect();
    arriving.style.transition = 'transform 300ms cubic-bezier(0.22, 1, 0.36, 1), opacity 220ms ease-out';
    arriving.style.transform = 'translateX(0)';
    arriving.style.opacity = '1';
    afterTransition(arriving, 320, () => clear(arriving));
}

function clear(list) {
    list.style.transition = '';
    list.style.transform = '';
    list.style.opacity = '';
    list.style.willChange = '';
}

// Waits for the list's transform to finish, or for the time it should take, whichever is first:
// a transition that never starts (nothing to change) never ends either.
function afterTransition(el, fallback, done) {
    let finished = false;
    const finish = () => {
        if (finished) {
            return;
        }

        finished = true;
        el.removeEventListener('transitionend', onEnd);
        done();
    };
    const onEnd = e => {
        if (e.target === el && e.propertyName === 'transform') {
            finish();
        }
    };

    el.addEventListener('transitionend', onEnd);
    setTimeout(finish, fallback);
}

function frame() {
    return new Promise(resolve => requestAnimationFrame(resolve));
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
