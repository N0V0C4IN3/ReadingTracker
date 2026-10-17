// Dev sign-in without editing config, as a browser init script: run before the page's own.
//
// wwwroot/appsettings.Development.json may have DevSignIn.Enabled false locally, and that file is
// the user's. This turns it on in the response instead, so the page offers the dev sign-in form
// (press "Sign in (dev)"; the reader id defaults to dev-reader). Only a successful response is
// rewritten, so a missing file still fails as a missing file.
//
// tools/dev/shot.mjs loads it; with Chrome DevTools, pass its text as the navigation's initScript
// (.claude/skills/verify/SKILL.md, Handle).
(() => {
    const of = window.fetch.bind(window);
    window.fetch = async (input, init) => {
        const url = typeof input === 'string' ? input : (input.url || String(input));
        const r = await of(input, init);
        if (!url.includes('appsettings.Development.json') || !r.ok) return r;
        const t = await r.text();
        return new Response(t.replace(/"Enabled"\s*:\s*false/, '"Enabled": true'),
            { status: 200, headers: { 'content-type': 'application/json' } });
    };
})();
