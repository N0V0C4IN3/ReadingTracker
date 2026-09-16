// The phone's search dock as a bottom sheet you can drag between three heights: a peek (just the
// field and its handle), a default half, and nearly full. Blazor owns whether the dock is open;
// this owns only how tall it is while it is, so a drag never round-trips to .NET — the height is
// written straight to the element on every frame of a drag and snapped on release.
//
// Everything here is a no-op where it cannot apply: called on a wide screen, or where pointer
// events are missing, it attaches nothing and the dock keeps whatever height the stylesheet gave
// it. The sheet is only ever this on a phone.

const SNAP = { default: 0.55, full: 0.92 };
const controllers = new WeakMap();

function heights(el) {
    const h = window.innerHeight;
    // The peek is the stylesheet's: it is also the height the dock first paints at, before this
    // script has said anything, and the two must agree or the dock jumps on opening.
    const peek = parseFloat(getComputedStyle(el).getPropertyValue('--sheet-peek')) || 132;
    return { peek, default: Math.round(h * SNAP.default), full: Math.round(h * SNAP.full) };
}

function nearest(el, value) {
    const h = heights(el);
    return Object.entries(h).reduce((best, [name, px]) =>
        Math.abs(px - value) < Math.abs(h[best] - value) ? name : best, 'default');
}

export function attach(el, startState = 'default') {
    if (!el || controllers.has(el) || !window.PointerEvent) {
        return;
    }

    const grip = el.querySelector('.search__grip');
    if (!grip) {
        return;
    }

    const setState = name => {
        el.dataset.sheet = name;
        el.style.height = heights(el)[name] + 'px';
    };

    let dragging = false, startY = 0, startH = 0, moved = false;
    // The height the last pointer move asked for, and the frame that will write it. Pointer
    // events can arrive faster than the screen repaints; writing the height on each one is
    // layout work nobody sees. One write per frame is what tracks a finger.
    let wanted = 0, frame = 0;

    const write = () => {
        frame = 0;
        el.style.height = wanted + 'px';
    };

    const down = e => {
        dragging = true;
        moved = false;
        startY = e.clientY;
        startH = el.getBoundingClientRect().height;
        el.style.transition = 'none';
        // The dock arrives with a short slide; a drag begun inside it would be fighting that
        // slide for the same element. The finger wins.
        el.style.animation = 'none';
        grip.setPointerCapture(e.pointerId);
    };

    const move = e => {
        if (!dragging) {
            return;
        }
        const h = heights(el);
        wanted = Math.min(h.full, Math.max(h.peek, startH + (startY - e.clientY)));
        if (Math.abs(wanted - startH) > 3) {
            moved = true;
        }
        frame ||= requestAnimationFrame(write);
    };

    const up = () => {
        if (!dragging) {
            return;
        }
        dragging = false;
        if (frame) {
            cancelAnimationFrame(frame);
            write();
        }
        el.style.transition = '';
        // A tap on the handle with no drag toggles between peek and default, so the sheet can be
        // got out of the way and back without a deliberate drag.
        const state = moved
            ? nearest(el, el.getBoundingClientRect().height)
            : (el.dataset.sheet === 'peek' ? 'default' : 'peek');
        setState(state);
    };

    grip.addEventListener('pointerdown', down);
    grip.addEventListener('pointermove', move);
    grip.addEventListener('pointerup', up);
    grip.addEventListener('pointercancel', up);

    // The scroll area and the drag must not fight: a drag that starts on the results should scroll
    // them, not resize the sheet, so the handle is the only drag surface and this is left alone.
    const onResize = () => {
        if (el.dataset.sheet) {
            el.style.height = heights(el)[el.dataset.sheet] + 'px';
        }
    };
    window.addEventListener('resize', onResize);

    controllers.set(el, { grip, down, move, up, onResize });
    setState(startState);
}

/**
 * Move the sheet to one of its heights from outside a drag. Silent where the sheet was never
 * attached.
 */
export function snap(el, name) {
    if (!el || !controllers.has(el)) {
        return;
    }

    el.dataset.sheet = name;
    el.style.height = heights(el)[name] + 'px';
}

/**
 * The search has something to show: a sheet that was only a field to type in comes up to the
 * half so it can be seen. A sheet the reader has already sized — dragged to full, or left at the
 * half — is theirs, and stays where they put it; a page turn is not a reason to take it back.
 */
export function grow(el) {
    if (el?.dataset.sheet === 'peek') {
        snap(el, 'default');
    }
}

/**
 * Start a new page of results from its first result. On a phone the results scroll inside the
 * sheet, so that is what goes back to the top; on a wide screen they are part of the page, and
 * the page is brought back to them only if it has been scrolled past them — results that are
 * already in view are left alone rather than nudged.
 */
export function showTop(el) {
    const found = el?.querySelector('.search__found');
    if (!found) {
        return;
    }

    if (controllers.has(el)) {
        found.scrollTop = 0;
        return;
    }

    const header = document.querySelector('header')?.getBoundingClientRect().bottom ?? 0;
    if (found.getBoundingClientRect().top < header) {
        found.scrollIntoView({ block: 'start' });
    }
}

export function detach(el) {
    const c = el && controllers.get(el);
    if (!c) {
        return;
    }
    c.grip.removeEventListener('pointerdown', c.down);
    c.grip.removeEventListener('pointermove', c.move);
    c.grip.removeEventListener('pointerup', c.up);
    c.grip.removeEventListener('pointercancel', c.up);
    window.removeEventListener('resize', c.onResize);
    el.style.height = '';
    el.style.transition = '';
    el.style.animation = '';
    delete el.dataset.sheet;
    controllers.delete(el);
}
