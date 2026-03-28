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
 * Handles both SWA format ({ clientPrincipal }) and App Service format (array).
 */
async function getUserInfo(): Promise<UserInfo | null> {
    try {
        const response = await fetch('/.auth/me');
        if (!response.ok) return null;

        const data = await response.json();

        // App Service Easy Auth returns an array
        if (Array.isArray(data) && data.length > 0) {
            const provider = data[0];
            const claims: Array<{ typ: string; val: string }> = provider.user_claims ?? [];
            const findClaim = (typ: string) => claims.find(c => c.typ === typ)?.val;

            const userId = findClaim('http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier')
                ?? findClaim('http://schemas.microsoft.com/identity/claims/objectidentifier')
                ?? provider.user_id ?? '';
            const email = findClaim('http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress')
                ?? provider.user_id ?? '';
            const fullName = findClaim('name') ?? email;

            // Extract roles from group claims (enriched by middleware)
            const groupClaims = claims.filter(c => c.typ === 'groups').map(c => c.val);
            const roleClaims = claims.filter(c => c.typ === 'roles' || c.typ === 'http://schemas.microsoft.com/ws/2008/06/identity/claims/role').map(c => c.val);
            const userRoles = roleClaims.length > 0 ? roleClaims : ['viewer'];

            return {
                identityProvider: provider.provider_name ?? 'aad',
                userId,
                userDetails: email,
                userRoles,
                fullName,
                email
            };
        }

        // SWA format: { clientPrincipal: { ... } }
        const principal = data?.clientPrincipal;
        if (!principal) return null;

        const nameClaim = principal.claims?.find((c: { typ: string; val: string }) => c.typ === 'name');
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
