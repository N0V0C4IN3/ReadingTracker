// Records why a sign-in callback failed, because the authentication library cannot report it.
//
// AuthenticationService.completeSignIn decides whether a failure is worth reporting by asking
// stateExists(), and stateExists() looks for the state parameter in the URL's *query string*:
//
//     new URLSearchParams(new URL(url).search).get("state")
//
// This application uses the implicit id_token flow (see Program.cs), where Google returns
// everything in the *fragment* — #state=...&id_token=... — and never in the query string. So
// stateExists() is always false here, every failure takes the `operationCompleted()` branch
// instead of the `error()` one, and RemoteAuthenticatorView is handed "nothing happened" rather
// than a reason. The page then sits on "Completing login..." for ever: no message, no error UI,
// nothing in the console.
//
// Catching the rejection from signinCallback is the smallest place to learn the real reason.
// Nothing here changes behaviour — the rejection is rethrown untouched — it only remembers what
// went past.

let lastError = null;

/// Null when no sign-in callback has failed in this browser session.
window.readingTrackerLastSignInError = () => lastError;

export function watchSignInCallback() {
    const service = window.AuthenticationService;

    if (!service || service.__readingTrackerWatched) {
        return;
    }

    // The static entry point, not the user manager underneath it. This module is imported while
    // the application starts, and AuthenticationService.instance does not exist until the library
    // initialises itself — so wrapping the user manager directly here would find nothing to wrap
    // and quietly do nothing. The static exists immediately, and by the time it is called the
    // instance beneath it is ready.
    const completeSignIn = service.completeSignIn.bind(service);

    service.completeSignIn = async (url) => {
        watchUserManager();
        return await completeSignIn(url);
    };

    service.__readingTrackerWatched = true;
}

function watchUserManager() {
    const userManager = window.AuthenticationService?.instance?._userManager;

    if (!userManager || userManager.__readingTrackerWatched) {
        return;
    }

    const signinCallback = userManager.signinCallback.bind(userManager);

    userManager.signinCallback = async (url) => {
        try {
            return await signinCallback(url);
        } catch (error) {
            // Silent renewal runs in a hidden iframe and fails routinely once the Google session
            // is gone; that is expected, and is not what the reader is looking at.
            if (window.self === window.top) {
                lastError = error?.message ?? String(error);
            }

            throw error;
        }
    };

    userManager.__readingTrackerWatched = true;
}
