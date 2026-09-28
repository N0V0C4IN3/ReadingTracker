// The drum of shelves under the cover flow on a phone held on its side (Components/ShelfDeck.razor).
// While a finger is on it the shelves follow the finger exactly: how far it has moved, in shelves,
// is --drag on the drum, added to each shelf's own --off. Let go, the drum runs on by the flick and
// comes to rest on the nearest shelf with books on it, and the flow is turned there (TurnTo). The
// component re-renders every --off from the new shelf inside that call, and --drag goes in the
// same moment, so the drum carries on to where it was going without a jump.
//
// A tap on a shelf is left to the shelf's own button.

// How far a finger moves before it is turning the drum rather than tapping a shelf.
const DECIDE = 10;
// How far the drum carries on after a flick: the finger's speed (px/ms) times this, in px.
const FLING_MS = 220;

/** Starts reading the finger on this drum; the component calls detach() when it goes. */
export function attach(drum, dotnet) {
    let gesture = null;
    // A drag's own lift is not a tap on the shelf it ended over. Until the next finger lands.
    let dragged = false;

    function down(e) {
        dragged = false;
        if (e.button > 0) {
            return;
        }

        const words = [...drum.querySelectorAll('.deck__word')];
        const at = Math.max(0, words.findIndex(word => word.classList.contains('is-on')));
        const rem = parseFloat(getComputedStyle(document.documentElement).fontSize);
        const pitch = parseFloat(getComputedStyle(drum).getPropertyValue('--pitch')) * rem;
        gesture = { id: e.pointerId, words, at, pitch, x: e.clientX, moved: false, samples: [{ x: e.clientX, t: e.timeStamp }] };
    }

    function move(e) {
        if (!gesture || e.pointerId !== gesture.id) {
            return;
        }

        const dx = e.clientX - gesture.x;
        if (!gesture.moved) {
            if (Math.abs(dx) < DECIDE) {
                return;
            }
            gesture.moved = true;
            drum.setPointerCapture(e.pointerId);
            drum.classList.add('is-turning');
        }

        gesture.samples.push({ x: e.clientX, t: e.timeStamp });
        if (gesture.samples.length > 5) {
            gesture.samples.shift();
        }
        drum.style.setProperty('--drag', (dx / gesture.pitch).toFixed(3));
    }

    function up(e) {
        const ended = gesture;
        gesture = null;
        if (!ended || e.pointerId !== ended.id || !ended.moved) {
            return;
        }

        dragged = true;
        const first = ended.samples[0];
        const last = ended.samples[ended.samples.length - 1];
        const speed = (last.x - first.x) / Math.max(1, last.t - first.t);
        const dx = last.x - ended.x + speed * FLING_MS;
        const heading = -Math.sign(dx);
        const aim = Math.max(0, Math.min(ended.words.length - 1, Math.round(ended.at - dx / ended.pitch)));
        const rest = shelfNear(ended.words, aim, heading);

        drum.classList.remove('is-turning');
        if (rest !== ended.at) {
            dotnet.invokeMethod('TurnTo', ended.words[rest].dataset.status);
        }
        drum.style.removeProperty('--drag');
    }

    function clicked(e) {
        if (dragged) {
            dragged = false;
            e.stopPropagation();
            e.preventDefault();
        }
    }

    drum.addEventListener('pointerdown', down);
    drum.addEventListener('pointermove', move);
    drum.addEventListener('pointerup', up);
    drum.addEventListener('pointercancel', up);
    drum.addEventListener('click', clicked, true);

    return {
        detach() {
            drum.removeEventListener('pointerdown', down);
            drum.removeEventListener('pointermove', move);
            drum.removeEventListener('pointerup', up);
            drum.removeEventListener('pointercancel', up);
            drum.removeEventListener('click', clicked, true);
        },
    };
}

// A shelf with books on it: the one the drum came to rest on, or failing that the nearest one on
// in the way it was going, or failing that back the way it came.
function shelfNear(words, at, heading) {
    const way = heading || 1;
    for (const step of [way, -way]) {
        for (let i = at; i >= 0 && i < words.length; i += step) {
            if (!words[i].disabled) {
                return i;
            }
        }
    }
    return at;
}
