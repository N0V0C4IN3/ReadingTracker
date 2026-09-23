// A sheet pinned to the foot of the screen (the log form on a phone: SessionForm.razor) kept above
// the keyboard. On iOS the keyboard comes up over the layout viewport without shrinking it, so a
// sheet pinned to its foot is pinned under the keyboard, minutes box and all; the sheet is lifted
// by however much of the layout viewport lies below the part that can be seen. On Android the page
// asks the keyboard to shrink the viewport instead (interactive-widget in index.html), and the
// lift comes out as nothing. The search dock does the same for itself: see sheet.js.
//
// Worked out a frame after the event that asked, when the window and the visual viewport have
// both settled: measured between the two, the lift is a keyboard's worth of nothing.

const lifts = new WeakMap();

export function attach(el) {
    const vv = window.visualViewport;
    if (!el || !vv || lifts.has(el)) {
        return;
    }

    let frame = 0;
    const follow = () => {
        frame ||= requestAnimationFrame(() => {
            frame = 0;
            const lift = Math.max(0, Math.round(window.innerHeight - vv.height - vv.offsetTop));
            el.style.bottom = lift ? lift + 'px' : '';
        });
    };

    vv.addEventListener('resize', follow);
    vv.addEventListener('scroll', follow);
    lifts.set(el, { follow, cancel: () => cancelAnimationFrame(frame) });
    follow();
}

export function detach(el) {
    const lift = el && lifts.get(el);
    if (!lift) {
        return;
    }

    window.visualViewport?.removeEventListener('resize', lift.follow);
    window.visualViewport?.removeEventListener('scroll', lift.follow);
    lift.cancel();
    el.style.bottom = '';
    lifts.delete(el);
}
