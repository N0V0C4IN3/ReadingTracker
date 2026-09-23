// Menus, sheets and panels come and go smoothly, at any width.
//
// Going: menus, sheets and panels leave the way they came. Blazor takes an element out of
// the page the moment the render that drops it lands, so there is no frame in which a closing
// animation could play. This watches for those removals and puts the element straight back,
// inert, with .is-leaving on it, for the stylesheet to play out (app.css, "Leaving"); it goes for
// good when that animation ends. Blazor has already forgotten the element by then, so the one it
// renders in its place (the next panel, say) is never waiting on it.
//
// Coming: a panel in a card is marked .is-entering as it arrives, so it grows its height open
// rather than the card jumping to its full size while the panel fades in; the mark comes off
// when that is done, and with it the clipping the growth needs.
//
// The observer runs before the next paint, so the element is never seen gone and come back,
// nor seen at its full height before it grows.

const LEAVES = [
    '.panel',
    '.sheet',
    '.sheet__scrim',
    '.status__menu',
    '.status__scrim',
    '.account__menu',
    '.goal__menu',
].join(',');

const GROWS = '.panel, .sheet';

// Longer than any entrance or exit in app.css: if an animation never reports its end, the element still goes.
const FALLBACK_MS = 600;

function leave(node, parent, next) {
    if (!parent.isConnected) {
        return;
    }

    // Put back where it was, if where it was is still there.
    parent.insertBefore(node, next && next.parentNode === parent ? next : null);
    node.inert = true;
    // A dragged sheet (sheet.js) is left with its animation switched off inline, which would
    // outrank the exit below.
    node.style.animation = '';
    node.classList.add('is-leaving');

    const animation = getComputedStyle(node).animationName;
    if (!animation || animation === 'none') {
        node.remove();
        return;
    }

    let timer = 0;
    const done = e => {
        if (e && e.target !== node) {
            return;
        }
        clearTimeout(timer);
        node.removeEventListener('animationend', done);
        node.remove();
    };
    node.addEventListener('animationend', done);
    timer = setTimeout(done, FALLBACK_MS);
}

function enter(node) {
    node.classList.add('is-entering');

    let timer = 0;
    const done = e => {
        if (e && e.target !== node) {
            return;
        }
        clearTimeout(timer);
        node.removeEventListener('animationend', done);
        node.classList.remove('is-entering');
    };
    node.addEventListener('animationend', done);
    timer = setTimeout(done, FALLBACK_MS);
}

new MutationObserver(records => {
    for (const record of records) {
        for (const node of record.removedNodes) {
            if (node.nodeType === Node.ELEMENT_NODE && !node.classList.contains('is-leaving') && node.matches(LEAVES)) {
                leave(node, record.target, record.nextSibling);
            }
        }
        for (const node of record.addedNodes) {
            if (node.nodeType === Node.ELEMENT_NODE && !node.classList.contains('is-leaving') && node.matches(GROWS)) {
                enter(node);
            }
        }
    }
}).observe(document.body, { childList: true, subtree: true });

/**
 * For something that closes by changing a class rather than by leaving the page (the phone's
 * search dock): plays its exit and resolves when it is done, so the change can come after.
 * .is-leaving stays on until then — taken off first, the element would be back at full for a
 * frame — and goes with the render that closes it, which writes the class attribute afresh.
 */
export function playOut(el) {
    return new Promise(resolve => {
        if (!el || !el.isConnected) {
            resolve();
            return;
        }

        el.style.animation = '';
        el.classList.add('is-leaving');
        const animation = getComputedStyle(el).animationName;
        if (!animation || animation === 'none') {
            el.classList.remove('is-leaving');
            resolve();
            return;
        }

        let timer = 0;
        const done = e => {
            if (e && e.target !== el) {
                return;
            }
            clearTimeout(timer);
            el.removeEventListener('animationend', done);
            resolve();
        };
        el.addEventListener('animationend', done);
        timer = setTimeout(done, FALLBACK_MS);
    });
}
