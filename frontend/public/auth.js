// Shared auth utility — included by all HTML pages

function getAuth() {
    try {
        const token = localStorage.getItem('auth_token');
        const user = JSON.parse(localStorage.getItem('auth_user') || 'null');
        return { token, user };
    } catch { return { token: null, user: null }; }
}

function getRefreshToken() {
    return localStorage.getItem('auth_refresh') || null;
}

function setAuth(resp) {
    localStorage.setItem('auth_token', resp.token);
    if (resp.refreshToken) localStorage.setItem('auth_refresh', resp.refreshToken);
    localStorage.setItem('auth_user', JSON.stringify({
        id: resp.userId,
        email: resp.email,
        name: resp.name,
        emailVerified: resp.emailVerified
    }));
}

function clearAuth() {
    localStorage.removeItem('auth_token');
    localStorage.removeItem('auth_refresh');
    localStorage.removeItem('auth_user');
}

function authHeaders() {
    const { token } = getAuth();
    return {
        'Content-Type': 'application/json',
        ...(token ? { 'Authorization': 'Bearer ' + token } : {})
    };
}

function isLoggedIn() {
    return !!getAuth().token;
}

// Exchange the refresh token for a fresh access+refresh pair. Returns true on success.
let _refreshInFlight = null;
async function tryRefreshToken() {
    const refreshToken = getRefreshToken();
    if (!refreshToken) return false;

    // De-dupe concurrent refreshes
    if (_refreshInFlight) return _refreshInFlight;

    _refreshInFlight = (async () => {
        try {
            const resp = await fetch('/api/auth/refresh', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ refreshToken })
            });
            if (!resp.ok) return false;
            const data = await resp.json();
            setAuth(data);
            return true;
        } catch {
            return false;
        } finally {
            _refreshInFlight = null;
        }
    })();

    return _refreshInFlight;
}

// Drop-in fetch wrapper: injects auth headers, transparently refreshes on 401,
// and only forces logout if the refresh also fails.
async function authFetch(url, options = {}) {
    let resp = await fetch(url, {
        ...options,
        headers: { ...authHeaders(), ...(options.headers || {}) }
    });

    if (resp.status === 401 && isLoggedIn()) {
        const refreshed = await tryRefreshToken();
        if (refreshed) {
            // Retry the original request once with the new token
            resp = await fetch(url, {
                ...options,
                headers: { ...authHeaders(), ...(options.headers || {}) }
            });
        }
        if (resp.status === 401) {
            const rt = getRefreshToken();
            if (rt) { try { await fetch('/api/auth/logout', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ refreshToken: rt }) }); } catch {} }
            clearAuth();
            window.dispatchEvent(new CustomEvent('auth:expired'));
        }
    }
    return resp;
}

// Logout helper — revokes the refresh token server-side then clears local state
async function logout(redirect = '/') {
    const rt = getRefreshToken();
    if (rt) {
        try {
            await fetch('/api/auth/logout', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ refreshToken: rt })
            });
        } catch {}
    }
    clearAuth();
    if (redirect) window.location.href = redirect;
}

// Global 401 handler — show toast and redirect to home after 3s
window.addEventListener('auth:expired', () => {
    const toast = document.createElement('div');
    toast.textContent = 'Session expired. Please sign in again.';
    toast.style.cssText = [
        'position:fixed', 'top:1rem', 'right:1rem', 'z-index:9999',
        'background:#93000a', 'color:#ffdad6', 'padding:.5rem 1.25rem',
        'border-radius:.75rem', 'font-size:.875rem', 'font-family:Inter,sans-serif',
        'box-shadow:0 4px 20px rgba(0,0,0,.5)'
    ].join(';');
    document.body.appendChild(toast);
    setTimeout(() => { toast.remove(); window.location.href = '/'; }, 3000);
});
