export {};

/// <reference path="globals.d.ts" />

// ── Types ───────────────────────────────────────────────────────────
interface ClientPrincipal {
    identityProvider: string;
    userId: string;
    userDetails: string;
    userRoles: string[];
    claims?: Array<{ typ: string; val: string }>;
}

interface AuthMeResponse {
    clientPrincipal: ClientPrincipal | null;
}

interface UserInfo {
    identityProvider: string;
    userId: string;
    userDetails: string;
    userRoles: string[];
    fullName: string;
    email: string;
}

/**
 * Fetch user authentication information from /.auth/me.
 * Uses Azure AD Easy Auth — works in both local dev (SWA CLI emulator) and production.
 */
async function getUserInfo(): Promise<UserInfo | null> {
    try {
        const response = await fetch('/.auth/me');
        if (!response.ok) return null;

        const data: AuthMeResponse = await response.json();
        const principal = data.clientPrincipal;
        if (!principal) return null;

        // Extract display name from claims (set by Azure AD)
        const nameClaim = principal.claims?.find(c => c.typ === 'name');
        const displayName = nameClaim?.val || principal.userDetails;

        return {
            identityProvider: principal.identityProvider,
            userId: principal.userId,
            userDetails: principal.userDetails,
            userRoles: principal.userRoles,
            fullName: displayName,
            email: principal.userDetails
        };
    } catch (error) {
        console.error('Error fetching user info:', error);
        return null;
    }
}

/**
 * Display user information on the dashboard
 */
async function displayUserInfo(user: UserInfo | null): Promise<void> {
    const welcomeTitleElement = document.getElementById('welcomeTitle');
    const welcomeMessageElement = document.getElementById('welcomeMessage');

    if (!user) {
        // User not authenticated — SWA will handle the redirect via 401 override.
        // If we somehow got here without auth, redirect to the home page.
        window.location.href = '/';
        return;
    }

    // Extract first name or use full name
    const userName = user.fullName || user.userDetails || 'User';
    const firstName = userName.split(' ')[0] || userName.split('@')[0];

    // Update navigation user info (desktop + mobile)
    ['userInfo', 'userInfoMobile'].forEach(id => {
        const el = document.getElementById(id);
        if (el) el.textContent = userName;
    });

    // Update welcome title and message
    if (welcomeTitleElement) {
        welcomeTitleElement.textContent = `Welcome, ${firstName}`;
    }
    if (welcomeMessageElement) {
        welcomeMessageElement.textContent = `Overview of packaging activity`;
    }
}

/**
 * Initialize the application
 */
async function init(): Promise<void> {
    const user = await getUserInfo();
    displayUserInfo(user);

    // Signal that the app shell is ready and the user is authenticated.
    // Page-level scripts should wait for this before making any API calls.
    // Use: await waitForAppShell()
    if (user) {
        window.userRoles = user.userRoles;
        window.appShellReady = true;
        window.dispatchEvent(new CustomEvent('app-shell-ready', { detail: { user } }));
    }
}

// Run initialization when DOM is loaded
if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
} else {
    init();
}
