// The finger on the shelf's cover flow, on a phone held on its side (Components/ShelfDeck.razor).
// The component decides where every jacket stands and the stylesheet places it; this only reads
// gestures and asks for turns.
//
// A sideways drag on the flow turns it a jacket at a time, a tap on a jacket either side brings
// it to the front, and so do the arrow keys. A drag on the drum turns it with the finger, a flick
// carries it several shelves, and it comes to rest on one, whose button it presses: the flow
// turns to that shelf's first book.

// How far a finger travels to turn the flow one jacket: the welcome deck's.
const STEP = 48;
// How far before a drag is decided, and how much more sideways than down it must be to turn.
const DECIDE = 10;
const SLANT = 1.2;
// How far the drum carries on after a flick: the finger's speed (px/ms) times this, in px.
const FLING_MS = 220;
// How long after a drag the click its lift makes is taken for part of the drag, not a tap.
const SWALLOW_MS = 400;

// What a key pressed in is typing, not turning the flow.
const ITS_OWN = 'input, textarea, select';

/** Starts reading the finger on this deck; the component calls detach() when it goes. */
export function attach(root, dotnet) {
    let drag = null;
    let swallowUntil = 0;

    const cards = () => root.querySelector('.deck__cards');
    const turn = by => dotnet.invokeMethod('Turn', by);

    function down(e) {
        drag = null;
        const target = e.target instanceof Element ? e.target : null;
        if (!target || e.button > 0) {
            return;
        }

        const drum = target.closest('.deck__drum');
        if (drum && root.contains(drum)) {
            drag = drumStart(drum, e);
            return;
        }

        if (cards()?.contains(target)) {
            drag = { kind: 'deck', x: e.clientX, y: e.clientY, from: e.clientX, decided: null };
        }
    }

    function move(e) {
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
        if (!gesture) {
            return;
        }

        if (gesture.kind === 'drum') {
            drumEnd(gesture);
        }
        if (gesture.decided === 'turn' || gesture.moved) {
            swallowUntil = performance.now() + SWALLOW_MS;
        }
    }

    // A drag's own lift is not a tap on whatever it ended over. A click a script makes (the drum
    // pressing the shelf it came to rest on) is never a lift.
    function clicked(e) {
        if (e.isTrusted && performance.now() < swallowUntil) {
            swallowUntil = 0;
            e.preventDefault();
            e.stopPropagation();
            return;
        }

        // A jacket either side of the front one is turned to the front, not opened.
        const side = e.target instanceof Element ? e.target.closest('.deck__slot:not(.is-front)') : null;
        if (side && root.contains(side)) {
            e.preventDefault();
            e.stopPropagation();
            turn(Number(side.dataset.off));
            return;
        }

        // Past the jackets either side: the tap lands on the flow itself.
        const list = cards();
        const front = list?.querySelector('.deck__slot.is-front');
        if (e.target === list && front) {
            const box = front.getBoundingClientRect();
            if (e.clientX < box.left) {
                turn(-1);
            } else if (e.clientX > box.right) {
                turn(1);
            }
        }
    }

    function keyed(e) {
        const target = e.target instanceof Element ? e.target : null;
        if (!target || target.closest(ITS_OWN) || e.altKey || e.ctrlKey || e.metaKey) {
            return;
        }
        if (e.key === 'ArrowLeft' || e.key === 'ArrowRight') {
            e.preventDefault();
            turn(e.key === 'ArrowLeft' ? -1 : 1);
        }
    }

    // The drum. While a finger is on it the shelves follow it exactly: how far it has moved, in
    // shelves, is --drag on the drum, added to each shelf's own --off. Let go, it runs on by the
    // flick and comes to rest on the nearest shelf, and that shelf's button is pressed; Blazor
    // re-renders every --off at once from the new shelf, and --drag goes in the same moment, so
    // the drum carries on to where it was going without a jump.

    function drumStart(drum, e) {
        const words = [...drum.querySelectorAll('.deck__word')];
        const at = Math.max(0, words.findIndex(word => word.classList.contains('is-on')));
        const rem = parseFloat(getComputedStyle(document.documentElement).fontSize);
        const pitch = parseFloat(getComputedStyle(drum).getPropertyValue('--pitch')) * rem;
        return { kind: 'drum', drum, words, at, pitch, x: e.clientX, moved: false, samples: [{ x: e.clientX, t: e.timeStamp }] };
    }

    function drumMove(gesture, e) {
        const dx = e.clientX - gesture.x;
        if (!gesture.moved) {
            if (Math.abs(dx) < DECIDE) {
                return;
            }
            gesture.moved = true;
            gesture.drum.classList.add('is-turning');
        }

        gesture.samples.push({ x: e.clientX, t: e.timeStamp });
        if (gesture.samples.length > 5) {
            gesture.samples.shift();
        }
        gesture.drum.style.setProperty('--drag', (dx / gesture.pitch).toFixed(3));
    }

    function drumEnd(gesture) {
        if (!gesture.moved) {
            return;
        }

        const first = gesture.samples[0];
        const last = gesture.samples[gesture.samples.length - 1];
        const speed = (last.x - first.x) / Math.max(1, last.t - first.t);
        const dx = last.x - gesture.x + speed * FLING_MS;
        const rest = shelfNear(gesture.words, Math.max(0, Math.min(gesture.words.length - 1, Math.round(gesture.at - dx / gesture.pitch))), -Math.sign(dx));

        gesture.drum.classList.remove('is-turning');
        if (rest !== gesture.at) {
            gesture.words[rest].click();
        }
        gesture.drum.style.removeProperty('--drag');
    }

    document.addEventListener('pointerdown', down, true);
    window.addEventListener('pointermove', move);
    window.addEventListener('pointerup', up);
    window.addEventListener('pointercancel', up);
    document.addEventListener('click', clicked, true);
    root.addEventListener('keydown', keyed);

    return {
        detach() {
            document.removeEventListener('pointerdown', down, true);
            window.removeEventListener('pointermove', move);
            window.removeEventListener('pointerup', up);
            window.removeEventListener('pointercancel', up);
            document.removeEventListener('click', clicked, true);
            root.removeEventListener('keydown', keyed);
        },
    };
}

// A shelf with books on it: the one the drum came to rest on, or failing that the nearest one on
// in the way it was going, or failing that back the way it came.
function shelfNear(words, at, heading) {
    for (const way of [heading || 1, -(heading || 1)]) {
        for (let i = at; i >= 0 && i < words.length; i += way) {
            if (!words[i].disabled) {
                return i;
            }
        }
    }
    return at;
}
