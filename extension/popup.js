let currentUrl = '';

document.getElementById('open-app').href = APP_URL;

// Get the active tab's URL
chrome.tabs.query({ active: true, currentWindow: true }, (tabs) => {
    const tab = tabs[0];
    const urlEl = document.getElementById('url');
    if (tab && tab.url && /^https?:\/\//.test(tab.url)) {
        currentUrl = tab.url;
        urlEl.textContent = tab.url;
    } else {
        currentUrl = '';
        urlEl.textContent = 'This page can\'t be analysed (not a web URL).';
        document.getElementById('analyze-btn').disabled = true;
    }
});

document.getElementById('analyze-btn').addEventListener('click', analyze);

async function analyze() {
    if (!currentUrl) return;

    const btn = document.getElementById('analyze-btn');
    const loading = document.getElementById('loading');
    const result = document.getElementById('result');
    const errorBox = document.getElementById('error');

    btn.classList.add('hidden');
    result.classList.add('hidden');
    errorBox.classList.add('hidden');
    loading.classList.remove('hidden');

    try {
        const res = await fetch(`${BACKEND_URL}/api/Analysis`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ type: 'url', content: currentUrl })
        });

        if (!res.ok) {
            const e = await res.json().catch(() => ({}));
            throw new Error(e.detail || `Server returned ${res.status}`);
        }

        const data = await res.json();
        renderResult(data.result || data);
    } catch (e) {
        loading.classList.add('hidden');
        btn.classList.remove('hidden');
        errorBox.textContent = 'Could not analyse: ' + e.message +
            '. Is the VerifyNews backend running?';
        errorBox.classList.remove('hidden');
    }
}

function renderResult(r) {
    document.getElementById('loading').classList.add('hidden');
    const result = document.getElementById('result');
    result.classList.remove('hidden');

    const verdictEl = document.getElementById('verdict');
    const map = { likely_true: ['Likely True', 'true'], likely_fake: ['Likely Fake', 'fake'], uncertain: ['Uncertain', 'uncertain'] };
    const [label, cls] = map[r.verdict] || ['Uncertain', 'uncertain'];
    verdictEl.textContent = label;
    verdictEl.className = 'verdict ' + cls;

    document.getElementById('score').textContent = Math.round(r.score ?? 0) + '%';
    document.getElementById('explanation').textContent = r.explanation || r.summary || '';

    document.getElementById('mock-warn').classList.toggle('hidden', !r.isMock);

    const flagsEl = document.getElementById('flags');
    flagsEl.innerHTML = '';
    (r.redFlags || []).slice(0, 4).forEach(f => {
        const li = document.createElement('li');
        li.textContent = f;
        flagsEl.appendChild(li);
    });
}
