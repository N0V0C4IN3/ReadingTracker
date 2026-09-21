// The book page in its jacket's colours.
//
// Two things are taken from the cover. The washes — the three broad fields of colour in the
// mesh behind the page (see body's background-image in app.css) — become the cover's three
// most present colours, at the tints the theme already uses. And the accent — buttons, the
// status, the progress bar, the monogram — becomes the cover's most saturated colour, brought
// to the lightness the theme keeps its own accent at, so it contrasts the same on either
// ground. Everything else is left to the theme; the ink never changes.
//
// The colours are read here rather than in Catalog because the reading is cheap — forty pixels
// across — and putting it there would mean a migration and a library to decode images for what
// is a tint. The bytes come from Blazor, which fetched them through the Gateway: a browser may
// not read the pixels of a cross-origin image, and Google Books sends no CORS headers.
//
// What was read is kept in localStorage by book, so a page revisited is painted from the first
// frame and only re-read afterwards; on a first visit the colours glide in a beat after the
// page appears, once the cover arrives (the tokens are registered in app.css so that they can,
// and data-jacket on <html> is what switches the transition on). Both are per theme: the same
// jacket gives a pale accent on dark and a deep one on light.

const STORAGE = "readingtracker.palette.";
const root = document.documentElement;
const washNames = ["--wash-accent", "--wash-warm", "--wash-cool"];
const accentNames = ["--accent", "--accent-hover", "--accent-soft", "--accent-ink"];

let current = null;   // the colours read from the cover on screen, or null
let observer = null;

// --- colour arithmetic -----------------------------------------------------------------------

function rgbToHsl(r, g, b) {
    r /= 255; g /= 255; b /= 255;
    const max = Math.max(r, g, b), min = Math.min(r, g, b), l = (max + min) / 2;
    let h = 0, s = 0;
    if (max !== min) {
        const d = max - min;
        s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        if (max === r) { h = (g - b) / d + (g < b ? 6 : 0); }
        else if (max === g) { h = (b - r) / d + 2; }
        else { h = (r - g) / d + 4; }
        h *= 60;
    }
    return { h, s, l };
}

function hslToRgb(h, s, l) {
    const c = (1 - Math.abs(2 * l - 1)) * s, x = c * (1 - Math.abs((h / 60) % 2 - 1)), m = l - c / 2;
    let r, g, b;
    if (h < 60) { r = c; g = x; b = 0; } else if (h < 120) { r = x; g = c; b = 0; }
    else if (h < 180) { r = 0; g = c; b = x; } else if (h < 240) { r = 0; g = x; b = c; }
    else if (h < 300) { r = x; g = 0; b = c; } else { r = c; g = 0; b = x; }
    return [Math.round((r + m) * 255), Math.round((g + m) * 255), Math.round((b + m) * 255)];
}

function hueGap(a, b) {
    const d = Math.abs(a - b) % 360;
    return d > 180 ? 360 - d : d;
}

const clamp = (value, low, high) => Math.min(high, Math.max(low, value));

// --- reading the cover -----------------------------------------------------------------------

// Draws the cover forty pixels wide, counts its pixels into coarse buckets, and picks three
// washes that are colours and unlike one another, and the one colour most fit to be an accent.
function read(bitmap) {
    const w = 40, h = Math.max(1, Math.round(40 * bitmap.height / bitmap.width));
    const canvas = document.createElement("canvas");
    canvas.width = w; canvas.height = h;
    const context = canvas.getContext("2d", { willReadFrequently: true });
    context.drawImage(bitmap, 0, 0, w, h);
    const data = context.getImageData(0, 0, w, h).data;

    const buckets = new Map();
    for (let i = 0; i < data.length; i += 4) {
        if (data[i + 3] < 128) { continue; }
        const key = ((data[i] >> 4) << 8) | ((data[i + 1] >> 4) << 4) | (data[i + 2] >> 4);
        let bucket = buckets.get(key);
        if (!bucket) { bucket = { n: 0, r: 0, g: 0, b: 0 }; buckets.set(key, bucket); }
        bucket.n++; bucket.r += data[i]; bucket.g += data[i + 1]; bucket.b += data[i + 2];
    }

    const colours = [...buckets.values()].map(bucket => {
        const { h, s, l } = rgbToHsl(bucket.r / bucket.n, bucket.g / bucket.n, bucket.b / bucket.n);
        return { n: bucket.n, h, s, l };
    });

    // Black and white grounds are most of many jackets and tint nothing, so they are left out
    // of the washes unless there is nothing else.
    const coloured = colours.filter(c => c.l > 0.12 && c.l < 0.94 && c.s > 0.12);
    const byPresence = (coloured.length >= 3 ? coloured : colours)
        .sort((a, b) => b.n * (0.15 + b.s) - a.n * (0.15 + a.s));

    const washes = [];
    for (const c of byPresence) {
        if (washes.length === 3) { break; }
        if (!washes.some(o => hueGap(o.h, c.h) < 28 && Math.abs(o.l - c.l) < 0.25)) { washes.push(c); }
    }
    while (washes.length < 3) { washes.push(washes[washes.length - 1] ?? { h: 0, s: 0, l: 0.5 }); }

    // Saturated, mid-toned and present: a stripe of red on a beige jacket beats the beige.
    const accent = colours
        .filter(c => c.s > 0.3 && c.l > 0.18 && c.l < 0.82)
        .sort((a, b) => b.n * b.s - a.n * a.s)[0] ?? null;

    const trim = ({ h, s, l }) => ({ h: Math.round(h), s: +s.toFixed(3), l: +l.toFixed(3) });

    return { washes: washes.map(trim), accent: accent ? trim(accent) : null };
}

// --- painting the page -----------------------------------------------------------------------

const isDark = () => root.getAttribute("data-theme") !== "light";

function paint() {
    if (!current) { return; }
    const dark = isDark();

    // The washes at the theme's own tints — the mesh is a tint of the page, not a picture —
    // with their lightness and saturation brought to where the theme keeps its own: pale on
    // dark, and on light both deeper and more vivid, since a tint on a pale ground is not seen
    // otherwise.
    const tints = dark ? [17, 13, 13] : [17, 15, 13];
    current.washes.forEach((c, i) => {
        const l = dark ? clamp(c.l, 0.58, 0.78) : clamp(c.l, 0.45, 0.6);
        const s = dark ? clamp(c.s, 0.35, 0.7) : clamp(c.s, 0.6, 0.9);
        root.style.setProperty(washNames[i], `rgb(${hslToRgb(c.h, s, l).join(" ")} / ${tints[i]}%)`);
    });

    if (!current.accent) {
        accentNames.forEach(name => root.style.removeProperty(name));
        return;
    }

    // The accent's job is contrast: pale on dark, deep on light, whatever the jacket's own
    // lightness. The hue is the jacket's; on light the saturation is pushed, since a deep
    // colour at the theme's quiet saturation reads as mud on a pale ground.
    const { h } = current.accent;
    const s = dark ? clamp(current.accent.s, 0.35, 0.55) : clamp(current.accent.s, 0.6, 0.85);
    const base = hslToRgb(h, s, dark ? 0.68 : 0.38);
    const hover = hslToRgb(h, s, dark ? 0.74 : 0.33);
    const ink = hslToRgb(h, s, dark ? 0.12 : 1);
    root.style.setProperty("--accent", `rgb(${base.join(" ")})`);
    root.style.setProperty("--accent-hover", `rgb(${hover.join(" ")})`);
    root.style.setProperty("--accent-soft", `rgb(${base.join(" ")} / ${dark ? 15 : 11}%)`);
    root.style.setProperty("--accent-ink", `rgb(${ink.join(" ")})`);
    root.style.setProperty("--wash-accent", `rgb(${base.join(" ")} / 17%)`);
}

function remember(bookId, palette) {
    try { localStorage.setItem(STORAGE + bookId, JSON.stringify(palette)); } catch { /* then next time is a first time */ }
}

function recall(bookId) {
    try {
        const kept = localStorage.getItem(STORAGE + bookId);
        return kept ? JSON.parse(kept) : null;
    } catch {
        return null;
    }
}

// --- what Book.razor calls -------------------------------------------------------------------

/**
 * Paints the page from what was read of this book's cover last time, if anything was, and
 * watches the theme so the colours are worked out again when it is switched.
 */
export function begin(bookId) {
    current = recall(bookId);
    paint();

    // The transition is switched on a frame after what was remembered has been painted, so
    // that is on screen at once and only what comes after it glides.
    requestAnimationFrame(() => requestAnimationFrame(() => root.setAttribute("data-jacket", "")));

    observer ??= new MutationObserver(paint);
    observer.observe(root, { attributes: true, attributeFilter: ["data-theme"] });
}

/** Reads the cover's bytes, paints the page from them, and keeps what was read for next time. */
export async function apply(bookId, bytes) {
    let bitmap;

    try {
        bitmap = await createImageBitmap(new Blob([bytes]));
    } catch {
        return;   // not an image after all; whatever was painted stays
    }

    try {
        current = read(bitmap);
    } finally {
        bitmap.close();
    }

    remember(bookId, current);
    paint();
}

/** The page is over: the theme's own colours come back for whatever is next. */
export function end() {
    current = null;
    root.removeAttribute("data-jacket");
    washNames.concat(accentNames).forEach(name => root.style.removeProperty(name));
    observer?.disconnect();
    observer = null;
}
