// The shelf on a phone as a deck, the welcome screen's cover flow with the shelf's own cards in
// it: the front card face-on, the rest turned away to either side and bunching as they recede.
// A sideways drag turns it a card at a time, and a tap either side of the front card brings the
// next one round. Under it the shelves sit on a drum of their own, turned the same way: it follows
// the finger, a flick carries it several shelves, and it comes to rest on one.
//
// Blazor renders the cards and the drum as it always has; this lays them out and never adds or
// takes a card away itself. A card that leaves its shelf (its status changed) is put back for its
// exit only, the way motion.js does for panels, and flies down into the shelf it went to.

const phone = window.matchMedia('(max-width: 40rem)');

// How far a finger travels to turn the deck one card: the welcome deck's.
const STEP = 48;
// How far from the front a card is still drawn. Past it a card is not seen (its opacity is 0),
// so it is not drawn at all and stands in one place, and a turn costs it nothing: on a shelf of
// fifty books, the deck moves seven cards rather than fifty.
const DRAWN = 3;
// How far before a drag is decided, and how much more sideways than down it must be to turn.
const DECIDE = 10;
const SLANT = 1.2;
// How far the drum carries on after a flick: the finger's speed (px/ms) times this, in px.
const FLING_MS = 220;

let shelf = null;
let observer = null;
const front = new Map();   // shelf → the id of the card at its front
const waiting = new Map(); // id → a card that has just left, waiting to hear where it went
let drag = null;
let swallowClick = false;
let measured = false;      // whether the deck's height is still right for the cards in it
// Cards to be made inert once the deck has stopped moving. Making a card inert restyles every
// element in it, which at the start of a turn is a dropped frame; once the turn has played out,
// nobody sees it. The card coming to the front is made reachable at once.
const later = new Set();
let laterTimer = 0;
const SETTLED_MS = 500;

/** Takes the shelf on, or lets it go. Safe to call after every render. */
export function sync(on) {
    const found = on ? document.querySelector('.shelf') : null;
    if (found === shelf) {
        return;
    }

    release();
    shelf = found;
    measured = false;
    if (!shelf) {
        return;
    }

    observer = new MutationObserver(changed);
    watch();
    shelf.addEventListener('pointerdown', down);
    shelf.addEventListener('click', clicked, true);
    window.addEventListener('pointermove', track);
    window.addEventListener('pointerup', up);
    window.addEventListener('pointercancel', up);
    phone.addEventListener('change', layout);
    layout();
}

/** A card has moved to another status (Home, after the change lands). */
export function moved(id, status) {
    const left = waiting.get(id);
    if (!left) {
        return;
    }

    waiting.delete(id);
    clearTimeout(left.timer);
    const to = shelf?.querySelector(`.deck__word[data-status="${status}"]`);
    to ? flyInto(left.card, to) : sink(left.card);
}

function release() {
    observer?.disconnect();
    observer = null;
    if (shelf) {
        shelf.removeEventListener('pointerdown', down);
        shelf.removeEventListener('click', clicked, true);
        for (const card of shelf.querySelectorAll('.entries > .entry')) {
            clearCard(card);
        }
        shelf.querySelector('.entries')?.style.removeProperty('--deck-h');
    }
    window.removeEventListener('pointermove', track);
    window.removeEventListener('pointerup', up);
    window.removeEventListener('pointercancel', up);
    phone.removeEventListener('change', layout);
    shelf = null;
}

function watch() {
    observer?.observe(shelf, { childList: true, subtree: true, attributes: true, attributeFilter: ['style', 'class'] });
}

// Blazor has added, taken away or re-rendered something. A card taken away is put back for its
// exit; everything else is laid out again before the next paint, so a card whose style Blazor
// has just rewritten never shows without its place in the deck.
function changed(records) {
    measured = false;
    const list = entries();
    for (const record of records) {
        if (record.target !== list) {
            continue;
        }
        for (const node of record.removedNodes) {
            if (node instanceof Element && node.matches('.entry.is-front') && !node.classList.contains('is-leaving')) {
                leave(node, record.target, record.nextSibling);
            }
        }
        for (const node of record.addedNodes) {
            if (node instanceof Element && node.matches('.entry') && !node.classList.contains('is-leaving')) {
                node.classList.add('is-dealt');
                node.addEventListener('animationend', () => node.classList.remove('is-dealt'), { once: true });
            }
        }
    }
    layout();
}

function entries() {
    return shelf?.querySelector('.entries:not(.entries--pending)') ?? null;
}

function cards() {
    const list = entries();
    return list ? [...list.children].filter(card => card.matches('.entry') && !card.classList.contains('is-leaving')) : [];
}

function place() {
    return shelf?.querySelector('.deck__word.is-on')?.dataset.status ?? 'All';
}

function frontIndex(all) {
    const at = all.findIndex(card => card.dataset.entry === front.get(place()));
    return at >= 0 ? at : 0;
}

function turn(by) {
    const all = cards();
    if (all.length === 0) {
        return;
    }
    front.set(place(), all[Math.max(0, Math.min(all.length - 1, frontIndex(all) + by))].dataset.entry);
    layout();
}

// Where a card stands, from how far it is from the front: the welcome deck's arithmetic.
function stand(offset) {
    const away = Math.min(Math.abs(offset), DRAWN + 1);
    const lean = Math.sign(offset);
    return {
        x: lean * (away === 0 ? 0 : 56 + (away - 1) * 12),
        z: away * 7,
        r: lean * -60,
        s: 1 - Math.min(away, 5) * 0.05,
        o: away <= 2 ? 1 : Math.max(0, 1 - (away - 2) * 0.6),
    };
}

function layout() {
    if (!shelf) {
        return;
    }

    observer?.disconnect();
    try {
        const all = cards();
        const list = entries();
        if (!phone.matches || !list) {
            all.forEach(clearCard);
            list?.style.removeProperty('--deck-h');
            return;
        }

        const at = frontIndex(all);
        if (all[at]) {
            front.set(place(), all[at].dataset.entry);
        }

        all.forEach((card, i) => {
            const off = i - at;
            const p = stand(off);
            set(card, '--deck-x', `${p.x}%`);
            set(card, '--deck-z', `${-p.z}rem`);
            set(card, '--deck-r', `${p.r}deg`);
            set(card, '--deck-s', p.s);
            set(card, '--deck-o', p.o);
            set(card, '--deck-layer', off === 0 ? 'auto' : -1 - Math.abs(off));
            set(card, '--deal-delay', `${Math.min(Math.abs(off), 4) * 50}ms`);
            toggle(card, 'is-front', off === 0);
            toggle(card, 'is-far', Math.abs(off) > DRAWN);
            if (off === 0) {
                card.inert = false;
            } else if (!card.inert) {
                later.add(card);
            }
        });
        settleLater();

        // Room for the tallest card on the shelf rather than the one in front, so what is under
        // the deck stays put as it turns. Measured when the cards change, not on every turn:
        // reading a height makes the browser lay the page out there and then.
        if (!measured) {
            measured = true;
            set(list, '--deck-h', `${Math.max(0, ...all.map(card => card.offsetHeight))}px`);
        }
    } finally {
        watch();
    }
}

function settleLater() {
    clearTimeout(laterTimer);
    if (later.size === 0) {
        return;
    }
    laterTimer = setTimeout(() => {
        for (const card of later) {
            if (card.isConnected && !card.classList.contains('is-front')) {
                card.inert = true;
            }
        }
        later.clear();
    }, SETTLED_MS);
}

function toggle(el, name, on) {
    if (el.classList.contains(name) !== on) {
        el.classList.toggle(name, on);
    }
}

function set(el, name, value) {
    const text = String(value);
    if (el.style.getPropertyValue(name) !== text) {
        el.style.setProperty(name, text);
    }
}

function clearCard(card) {
    for (const name of ['--deck-x', '--deck-z', '--deck-r', '--deck-s', '--deck-o', '--deck-layer', '--deal-delay']) {
        card.style.removeProperty(name);
    }
    card.classList.remove('is-front', 'is-far');
    card.inert = false;
    later.delete(card);
}

// A card Blazor has just taken away: put back where it was, out of reach, to fly down into the
// shelf its move says — or, if nothing says, to sink away.
function leave(card, parent, next) {
    if (!phone.matches) {
        return;
    }

    parent.insertBefore(card, next && next.parentNode === parent ? next : null);
    card.inert = true;
    card.classList.add('is-leaving');

    const id = card.dataset.entry;
    waiting.set(id, { card, timer: setTimeout(() => { waiting.delete(id); sink(card); }, 400) });
}

function flyInto(card, target) {
    const from = card.getBoundingClientRect();
    const to = target.getBoundingClientRect();
    const dx = to.left + to.width / 2 - (from.left + from.width / 2);
    const dy = to.top + to.height / 2 - (from.top + from.height / 2);
    card.style.setProperty('--leave-to', `translate(${dx}px, ${dy}px) scale(0.1)`);
    card.classList.add('is-flying');
    target.classList.add('is-landed');
    setTimeout(() => target.classList.remove('is-landed'), 700);
    gone(card);
}

function sink(card) {
    card.style.setProperty('--leave-to', 'translateY(3rem) scale(0.9)');
    card.classList.add('is-flying');
    gone(card);
}

function gone(card) {
    const end = () => card.remove();
    card.addEventListener('transitionend', e => { if (e.target === card && e.propertyName === 'opacity') end(); });
    setTimeout(end, 800);
}

// Pointer: a sideways drag on the deck turns it; a drag on the drum turns the drum.

function down(e) {
    swallowClick = false;
    drag = null;
    const target = e.target instanceof Element ? e.target : null;
    if (!phone.matches || !target || e.button > 0) {
        return;
    }

    const drum = target.closest('.deck__drum');
    if (drum) {
        drag = drumStart(drum, e);
        return;
    }

    const list = entries();
    if (list?.contains(target) && !target.closest('.sheet, .sheet__scrim, .status__menu, .status__scrim, input, textarea, select')) {
        drag = { kind: 'deck', x: e.clientX, y: e.clientY, from: e.clientX, decided: null };
    }
}

function track(e) {
    if (!drag) {
        return;
    }

    if (drag.kind === 'drum') {
        drumMove(drag, e);
        return;
    }

    const dx = e.clientX - drag.x;
    const dy = e.clientY - drag.y;
    if (drag.decided === null) {
        if (Math.hypot(dx, dy) < DECIDE) {
            return;
        }
        swallowClick = true;
        drag.decided = Math.abs(dx) > Math.abs(dy) * SLANT ? 'turn' : 'none';
    }

    if (drag.decided === 'turn') {
        const steps = Math.trunc((e.clientX - drag.from) / STEP);
        if (steps) {
            drag.from += steps * STEP;
            turn(-steps);
        }
    }
}

function up() {
    const gesture = drag;
    drag = null;
    if (gesture?.kind === 'drum') {
        drumEnd(gesture);
    }
}

// Taps either side of the front card turn the deck. A drag that has just happened is not a tap.
function clicked(e) {
    const target = e.target instanceof Element ? e.target : null;
    if (!phone.matches || !target) {
        return;
    }

    if (swallowClick) {
        swallowClick = false;
        if (entries()?.contains(target) || target.closest('.deck__drum')) {
            e.preventDefault();
            e.stopPropagation();
        }
        return;
    }

    const list = entries();
    const frontCard = list?.querySelector('.entry.is-front');
    if (target === list && frontCard) {
        const box = frontCard.getBoundingClientRect();
        if (e.clientX < box.left) {
            turn(-1);
        } else if (e.clientX > box.right) {
            turn(1);
        }
    }
}

// The drum. While a finger is on it the shelves follow it exactly (each word's place is written
// on it as --o and --a, its offset and distance from the middle, fractional); let go, it runs on
// by the flick, comes to rest on the nearest shelf, and that shelf's button is pressed. Blazor
// then writes the same places back as whole numbers, so nothing jumps.

function drumStart(drum, e) {
    const words = [...drum.querySelectorAll('.deck__word')];
    const at = words.findIndex(word => word.classList.contains('is-on'));
    const rem = parseFloat(getComputedStyle(document.documentElement).fontSize) || 16;
    const pitch = (parseFloat(getComputedStyle(drum).getPropertyValue('--pitch')) || 9.5) * rem;
    return { kind: 'drum', drum, words, at: Math.max(at, 0), pitch, x: e.clientX, moved: false, samples: [{ x: e.clientX, t: e.timeStamp }] };
}

function drumMove(gesture, e) {
    const dx = e.clientX - gesture.x;
    if (!gesture.moved) {
        if (Math.abs(dx) < DECIDE) {
            return;
        }
        gesture.moved = true;
        swallowClick = true;
        gesture.drum.classList.add('is-turning');
    }

    gesture.samples.push({ x: e.clientX, t: e.timeStamp });
    if (gesture.samples.length > 5) {
        gesture.samples.shift();
    }
    paintDrum(gesture, gesture.at - dx / gesture.pitch);
}

function drumEnd(gesture) {
    if (!gesture.moved) {
        return;
    }

    const first = gesture.samples[0];
    const last = gesture.samples[gesture.samples.length - 1];
    const speed = (last.x - first.x) / Math.max(1, last.t - first.t);
    const dx = last.x - gesture.x + speed * FLING_MS;
    const rest = Math.max(0, Math.min(gesture.words.length - 1, Math.round(gesture.at - dx / gesture.pitch)));

    gesture.drum.classList.remove('is-turning');
    paintDrum(gesture, rest);
    if (rest !== gesture.at) {
        // Pressed here rather than by the finger: the lift that follows is not a tap on a shelf.
        swallowClick = false;
        gesture.words[rest].click();
        swallowClick = true;
    }
}

function paintDrum(gesture, centre) {
    gesture.words.forEach((word, i) => {
        const off = i - centre;
        word.style.setProperty('--o', off.toFixed(3));
        word.style.setProperty('--a', Math.abs(off).toFixed(3));
    });
}
