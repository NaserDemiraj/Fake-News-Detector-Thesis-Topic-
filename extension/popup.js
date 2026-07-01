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

document.getElementById('analyze-btn').addEventListener('click', () => {
    if (currentUrl) analyze('url', currentUrl);
});

// Analyse whatever text the user has selected on the page.
document.getElementById('analyze-selection-btn').addEventListener('click', async () => {
    try {
        const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
        const [{ result }] = await chrome.scripting.executeScript({
            target: { tabId: tab.id },
            func: () => window.getSelection().toString()
        });
        const text = (result || '').trim();
        if (text.length < 15) {
            showError('Select at least a sentence of text on the page first, then click again.');
            return;
        }
        analyze('text', text);
    } catch (e) {
        showError('Could not read the page selection: ' + e.message);
    }
});

function showError(msg) {
    document.getElementById('loading').classList.add('hidden');
    document.getElementById('analyze-btn').classList.remove('hidden');
    document.getElementById('analyze-selection-btn').classList.remove('hidden');
    const errorBox = document.getElementById('error');
    errorBox.textContent = msg;
    errorBox.classList.remove('hidden');
}

async function analyze(type, content) {
    const btn = document.getElementById('analyze-btn');
    const selBtn = document.getElementById('analyze-selection-btn');
    const loading = document.getElementById('loading');
    const result = document.getElementById('result');
    const errorBox = document.getElementById('error');

    btn.classList.add('hidden');
    selBtn.classList.add('hidden');
    result.classList.add('hidden');
    errorBox.classList.add('hidden');
    loading.classList.remove('hidden');

    try {
        const res = await fetch(`${BACKEND_URL}/api/Analysis`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ type, content })
        });

        if (res.status === 429) throw new Error('Rate limit reached — wait a minute and try again.');
        if (!res.ok) {
            const e = await res.json().catch(() => ({}));
            throw new Error(e.detail || `Server returned ${res.status}`);
        }

        const data = await res.json();
        renderResult(data.result || data);
    } catch (e) {
        showError('Could not analyse: ' + e.message);
    }
}

function renderResult(r) {
    document.getElementById('loading').classList.add('hidden');
    document.getElementById('analyze-btn').classList.remove('hidden');
    document.getElementById('analyze-selection-btn').classList.remove('hidden');
    const result = document.getElementById('result');
    result.classList.remove('hidden');

    const verdictEl = document.getElementById('verdict');
    const map = { likely_true: ['Likely True', 'true'], likely_fake: ['Likely Fake', 'fake'], uncertain: ['Uncertain', 'uncertain'] };
    const [label, cls] = map[r.verdict] || ['Uncertain', 'uncertain'];
    verdictEl.textContent = label;
    verdictEl.className = 'verdict ' + cls;

    document.getElementById('score').textContent = Math.round(r.score ?? 0) + '%';
    document.getElementById('explanation').textContent = r.explanation || r.summary || '';

    // Calibrated confidence (Platt-scaled) preferred; fall back to raw confidence.
    const confEl = document.getElementById('confidence');
    const conf = (r.calibratedConfidence && r.calibratedConfidence > 0) ? r.calibratedConfidence : r.confidence;
    if (!r.isMock && conf > 0) {
        const pct = Math.round(conf <= 1 ? conf * 100 : conf);
        const calibrated = r.calibratedConfidence && r.calibratedConfidence > 0;
        confEl.textContent = `Confidence: ${pct}%${calibrated ? ' (calibrated)' : ''}`;
    } else {
        confEl.textContent = '';
    }

    document.getElementById('mock-warn').classList.toggle('hidden', !r.isMock);

    const flagsEl = document.getElementById('flags');
    flagsEl.innerHTML = '';
    (r.redFlags || []).slice(0, 4).forEach(f => {
        const li = document.createElement('li');
        li.textContent = f;
        flagsEl.appendChild(li);
    });

    const reportLink = document.getElementById('open-report');
    if (reportLink) { reportLink.href = APP_URL; reportLink.style.display = 'inline-block'; }
}
