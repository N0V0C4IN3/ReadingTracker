// Screenshots of the running app, signed in as the dev reader, at a phone's size and a desktop's.
//
// For seeing a change rather than reading it: it needs only the local stack (tools/dev/up.sh)
// and the Chrome installed on this machine, not the Chrome DevTools server, so it works when
// that server will not connect. Read the PNGs it prints.
//
//   npm ci --prefix tools/dev                      (once)
//   node tools/dev/shot.mjs [page] [--click <selector>]... [--wait <selector>] [--name <label>]
//                           [--theme dark|light|both] [--only phone|desktop] [--full]
//
// page        where to open under http://localhost:5200/, without a leading slash (default: the
//             shelf). Git Bash rewrites an argument that starts with / into a Windows path.
// --wait      a selector to wait for before anything else (default: a shelf card, on the shelf).
// --click     a selector to click, in order, each waited for first; repeat it for a flow, e.g.
//             --click ".entry__title a" --click ".days__all" opens a book and its whole history.
// --theme     dark (the app's own default, what most readers see), light, or both.
// --only      one of the two sizes.
// --full      the whole page rather than the window. The window is centred on the last thing
//             clicked; a whole-page shot paints the sticky header and the backdrop wherever the
//             window happened to be, so it reads oddly.
//
// CHROME_PATH runs a Chrome other than the installed one.
//
// The shots go to artifacts/shots/<commit>/ (artifacts/ is gitignored), named for the commit
// that is checked out, with -dirty when the tree has other changes than the user's own
// appsettings.Development.json (which dev-sign-in.js overrides anyway). A run that fails saves
// what was on screen as <name>-...-failed.png. Someone asking "was this commit looked at" asks
// that folder; a -dirty folder, or a -failed shot, does not answer for the commit.

import { chromium } from 'playwright-core';
import { execSync } from 'node:child_process';
import { mkdirSync, readFileSync } from 'node:fs';
import { join, resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const root = resolve(here, '..', '..');
const APP = 'http://localhost:5200/';

// After a click, long enough for Blazor to render what it asked for. Animations are off
// (reducedMotion), so this is render time, not motion.
const AFTER_CLICK_MS = 600;
// Before the shot: covers and late images loading in.
const BEFORE_SHOT_MS = 400;

function fail(message) {
    console.error(message);
    process.exit(2);
}

const args = process.argv.slice(2);
const clicks = [];
let path = null, wait = null, name = 'shot', theme = 'dark', only = null, full = false;

for (let i = 0; i < args.length; i++) {
    const a = args[i];
    const value = () => {
        if (i + 1 >= args.length) fail(`${a} needs a value.`);
        return args[++i];
    };
    if (a === '--click') clicks.push(value());
    else if (a === '--wait') wait = value();
    else if (a === '--name') name = value();
    else if (a === '--theme') theme = value();
    else if (a === '--only') only = value();
    else if (a === '--full') full = true;
    else if (a.startsWith('--')) fail(`Unknown flag ${a}. The flags are in the header of tools/dev/shot.mjs.`);
    else if (path !== null) fail(`Two pages given: "${path}" and "${a}".`);
    else path = a;
}

if (!['dark', 'light', 'both'].includes(theme)) fail(`--theme is dark, light or both, not "${theme}".`);
if (only !== null && !['phone', 'desktop'].includes(only)) fail(`--only is phone or desktop, not "${only}".`);

path ??= '';
if (/^[A-Za-z]:[\\/]/.test(path)) {
    fail(`"${path}" is a Windows path: give the page without its leading slash (shelf/<id>, not /shelf/<id>).`);
}
path = path.replace(/^\/+/, '');

if (!wait && path === '') {
    wait = '.entry:not(.entry--pending)';
}

const commit = (() => {
    const git = cmd => execSync(cmd, { cwd: root }).toString().trim();
    try {
        const changes = git('git status --porcelain -- . ":(exclude)src/ReadingTracker.Web/wwwroot/appsettings.Development.json"');
        return git('git rev-parse --short HEAD') + (changes ? '-dirty' : '');
    } catch {
        return 'unknown';
    }
})();

const out = join(root, 'artifacts', 'shots', commit);
mkdirSync(out, { recursive: true });

const signIn = readFileSync(join(here, 'dev-sign-in.js'), 'utf8');

const sizes = [
    { label: 'phone', viewport: { width: 390, height: 844 }, deviceScaleFactor: 3, isMobile: true, hasTouch: true },
    { label: 'desktop', viewport: { width: 1280, height: 900 }, deviceScaleFactor: 1, isMobile: false, hasTouch: false },
].filter(size => only === null || size.label === only);

const themes = theme === 'both' ? ['dark', 'light'] : [theme];

const browser = await chromium.launch(process.env.CHROME_PATH
    ? { executablePath: process.env.CHROME_PATH }
    : { channel: 'chrome' });
let failed = false;

for (const size of sizes) {
    for (const shade of themes) {
        const { label, ...options } = size;
        // One language for the dates whoever runs it, so shots compare across machines.
        const context = await browser.newContext({ ...options, reducedMotion: 'reduce', locale: 'en-GB' });
        const page = await context.newPage();
        // The theme is chosen before the first paint, as index.html reads it.
        await page.addInitScript(`try { localStorage.setItem('readingtracker.theme', ${JSON.stringify(shade)}); } catch {}`);
        await page.addInitScript(signIn);
        const file = join(out, `${name}-${label}-${shade}.png`);

        try {
            await page.goto(new URL(path, APP).href, { waitUntil: 'networkidle', timeout: 60000 });
            // Dev sign-in asks for the reader by a form (Layout/DevSignInForm.razor).
            const devSignIn = page.getByRole('button', { name: 'Sign in (dev)' });
            if (await devSignIn.isVisible().catch(() => false)) {
                await devSignIn.click();
                await page.waitForLoadState('networkidle');
                if (path !== '') {
                    await page.goto(new URL(path, APP).href, { waitUntil: 'networkidle', timeout: 60000 });
                }
            }
            if (wait) {
                await page.waitForSelector(wait, { timeout: 30000 });
            }
            for (const selector of clicks) {
                await page.locator(selector).first().click({ timeout: 30000 });
                await page.waitForLoadState('networkidle');
                await page.waitForTimeout(AFTER_CLICK_MS);
            }
            if (clicks.length > 0 && !full) {
                await page.locator(clicks[clicks.length - 1]).first()
                    .evaluate(el => el.scrollIntoView({ block: 'center' }), null, { timeout: 1000 })
                    .catch(() => {});   // the click left the page, and the selector with it
            }
            await page.waitForTimeout(BEFORE_SHOT_MS);
            await page.screenshot({ path: file, fullPage: full });
            console.log(file);
        } catch (error) {
            failed = true;
            const seen = file.replace(/\.png$/, '-failed.png');
            await page.screenshot({ path: seen, fullPage: true }).catch(() => {});
            console.error(`${label} ${shade}: ${error.message.split('\n')[0]} (what was on screen: ${seen})`);
        }

        await context.close();
    }
}

await browser.close();
process.exit(failed ? 1 : 0);
