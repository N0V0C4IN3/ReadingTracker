// Screenshots of the running app, signed in as the dev reader, at a phone's size and a desktop's.
//
// For seeing a change rather than reading it: it needs only the local stack (tools/dev/up.sh)
// and the Chrome installed on this machine, not the Chrome DevTools server, so it works when
// that server will not connect. Read the PNGs it prints.
//
//   npm ci --prefix tools/dev                      (once)
//   node tools/dev/shot.mjs [page] [--click <selector>]... [--wait <selector>] [--name <label>]
//                           [--theme light|dark] [--only phone|desktop] [--full]
//
// page        where to open under http://localhost:5200/, without a leading slash (default: the
//             shelf). Git Bash rewrites an argument that starts with / into a Windows path.
// --wait      a selector to wait for before anything else (default: a shelf card, on /).
// --click     a selector to click, in order, each waited for first; repeat it for a flow, e.g.
//             --click ".entry__title a" --click ".days__all" opens a book and its whole history.
// --theme     the theme to load in (default light).
// --full      the whole page rather than the window. The window is centred on the last thing
//             clicked; a whole-page shot paints the sticky header and the backdrop wherever the
//             window happened to be, so it reads oddly.
//
// The shots go to artifacts/shots/<commit>/ (artifacts/ is gitignored), named for the commit
// that is checked out, with -dirty when the tree has changes. Someone asking "was this looked
// at" asks that folder.

import { chromium } from 'playwright-core';
import { execSync } from 'node:child_process';
import { mkdirSync } from 'node:fs';
import { join, resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..');
const args = process.argv.slice(2);
const clicks = [];
let path = '', wait = null, name = 'shot', theme = 'light', only = null, full = false;

for (let i = 0; i < args.length; i++) {
    const a = args[i];
    if (a === '--click') clicks.push(args[++i]);
    else if (a === '--wait') wait = args[++i];
    else if (a === '--name') name = args[++i];
    else if (a === '--theme') theme = args[++i];
    else if (a === '--only') only = args[++i];
    else if (a === '--full') full = true;
    else path = a;
}

if (/^[A-Za-z]:[\/]/.test(path)) {
    console.error(`"${path}" is a Windows path: give the page without its leading slash (shelf/<id>, not /shelf/<id>).`);
    process.exit(2);
}

path = path.replace(/^\/+/, '');

if (!wait && path === '') {
    wait = '.entry:not(.entry--pending)';
}

const commit = (() => {
    const git = cmd => execSync(cmd, { cwd: root }).toString().trim();
    try {
        return git('git rev-parse --short HEAD') + (git('git status --porcelain') ? '-dirty' : '');
    } catch {
        return 'unknown';
    }
})();

const out = join(root, 'artifacts', 'shots', commit);
mkdirSync(out, { recursive: true });

// The same as .claude/skills/verify/SKILL.md, Handle step 3: dev sign-in without touching the
// user's appsettings.Development.json, and the theme chosen before the first paint.
const initScript = `
  try { localStorage.setItem('readingtracker.theme', ${JSON.stringify(theme)}); } catch {}
  const of = window.fetch.bind(window);
  window.fetch = async (input, init) => {
    const url = typeof input === 'string' ? input : (input.url || String(input));
    const r = await of(input, init);
    if (!url.includes('appsettings.Development.json') || !r.ok) return r;
    const t = await r.text();
    return new Response(t.replace(/"Enabled"\\s*:\\s*false/, '"Enabled": true'),
      { status: 200, headers: { 'content-type': 'application/json' } });
  };`;

const sizes = [
    { label: 'phone', viewport: { width: 390, height: 844 }, deviceScaleFactor: 2, isMobile: true, hasTouch: true },
    { label: 'desktop', viewport: { width: 1280, height: 900 }, deviceScaleFactor: 1, isMobile: false, hasTouch: false },
].filter(size => !only || size.label === only);

const browser = await chromium.launch({ channel: process.env.CHROME_PATH ? undefined : 'chrome', executablePath: process.env.CHROME_PATH });
let failed = false;

for (const size of sizes) {
    const { label, ...options } = size;
    const context = await browser.newContext({ ...options, reducedMotion: 'reduce' });
    const page = await context.newPage();
    await page.addInitScript(initScript);
    const file = join(out, `${name}-${label}.png`);

    try {
        await page.goto(new URL(path, 'http://localhost:5200/').href, { waitUntil: 'networkidle', timeout: 60000 });
        // Dev sign-in asks for the reader by a form (Layout/DevSignInForm.razor); its default is
        // the dev reader. Signed in already (the session survives in the context), it is not there.
        const devSignIn = page.getByRole('button', { name: 'Sign in (dev)' });
        if (await devSignIn.isVisible().catch(() => false)) {
            await devSignIn.click();
            await page.waitForLoadState('networkidle');
            if (path !== '') {
                await page.goto(new URL(path, 'http://localhost:5200/').href, { waitUntil: 'networkidle', timeout: 60000 });
            }
        }
        if (wait) {
            await page.waitForSelector(wait, { timeout: 30000 });
        }
        for (const selector of clicks) {
            await page.locator(selector).first().click({ timeout: 30000 });
            await page.waitForLoadState('networkidle');
            await page.waitForTimeout(600);
        }
        if (clicks.length > 0 && !full) {
            await page.locator(clicks[clicks.length - 1]).first()
                .evaluate(el => el.scrollIntoView({ block: 'center' })).catch(() => {});
        }
        await page.waitForTimeout(400);
        await page.screenshot({ path: file, fullPage: full });
        console.log(file);
    } catch (error) {
        failed = true;
        await page.screenshot({ path: file, fullPage: true }).catch(() => {});
        console.error(`${label}: ${error.message.split('\n')[0]} (what was on screen: ${file})`);
    }

    await context.close();
}

await browser.close();
process.exit(failed ? 1 : 0);
