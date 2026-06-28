// Shared auth utility — included by all HTML pages

function getAuth() {
    try {
        const token = localStorage.getItem('auth_token');
        const user = JSON.parse(localStorage.getItem('auth_user') || 'null');
        return { token, user };
    } catch { return { token: null, user: null }; }
}

function setAuth(resp) {
    localStorage.setItem('auth_token', resp.token);
    localStorage.setItem('auth_user', JSON.stringify({
        id: resp.userId,
        email: resp.email,
        name: resp.name
    }));
}

function clearAuth() {
    localStorage.removeItem('auth_token');
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

// Drop-in fetch wrapper: automatically injects auth headers and handles 401 expiry
async function authFetch(url, options = {}) {
    const resp = await fetch(url, {
        ...options,
        headers: { ...authHeaders(), ...(options.headers || {}) }
    });
    if (resp.status === 401 && isLoggedIn()) {
        clearAuth();
        window.dispatchEvent(new CustomEvent('auth:expired'));
    }
    return resp;
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
