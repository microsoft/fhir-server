function setExample(url) {
    document.getElementById('fhir-url').value = url;
    document.getElementById('continuation-token').value = '';
    parseUrl();
}

async function parseUrl() {
    const url = document.getElementById('fhir-url').value.trim();
    const ct = document.getElementById('continuation-token').value.trim();
    if (!url) return;

    try {
        const resp = await fetch('/api/parse', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ url, continuationToken: ct || null })
        });
        const data = await resp.json();

        if (data.error) {
            document.getElementById('sql-output').innerHTML = `<div class="error"><strong>Error:</strong>\n${escapeHtml(data.error)}\n\n${escapeHtml(data.stackTrace || '')}</div>`;
            document.getElementById('params-output').innerHTML = '';
            document.getElementById('info-output').innerHTML = '';
        } else {
            renderParams(data.queryParameters);
            renderInfo(data);
            renderSql(data.formattedSql || data.generatedSql);
        }
    } catch (e) {
        document.getElementById('sql-output').innerHTML = `<div class="error">${escapeHtml(e.message)}</div>`;
    }
}

function renderParams(params) {
    if (!params) { document.getElementById('params-output').innerHTML = '<pre>No parameters</pre>'; return; }
    let html = '<table class="params-table"><tr><th>Parameter</th><th>Value(s)</th></tr>';
    for (const [key, values] of Object.entries(params)) {
        const badges = values.map(v => `<span class="badge">${escapeHtml(v)}</span>`).join(' ');
        html += `<tr><td>${escapeHtml(key)}</td><td>${badges}</td></tr>`;
    }
    html += '</table>';
    document.getElementById('params-output').innerHTML = html;
}

function renderInfo(data) {
    let html = '<table class="params-table">';
    html += `<tr><td>Resource Type</td><td><span class="badge type">${escapeHtml(data.resourceType)}</span> <span class="badge id">TypeId: ${data.resourceTypeId}</span></td></tr>`;
    if (data.continuationTokenParsed) {
        html += `<tr><td>Continuation Token</td><td><code>${escapeHtml(data.continuationTokenParsed)}</code></td></tr>`;
    }
    html += '</table>';
    document.getElementById('info-output').innerHTML = html;
}

function renderSql(sql) {
    if (!sql) {
        document.getElementById('sql-output').innerHTML = '<pre>(no SQL generated)</pre>';
        return;
    }
    document.getElementById('sql-output').innerHTML = `<pre>${highlightSql(escapeHtml(sql))}</pre>`;
}

function highlightSql(sql) {
    const keywords = ['WITH', 'AS', 'SELECT', 'FROM', 'WHERE', 'AND', 'OR', 'INNER JOIN', 'LEFT JOIN',
        'ON', 'ORDER BY', 'GROUP BY', 'HAVING', 'UNION ALL', 'UNION', 'TOP', 'DISTINCT',
        'EXISTS', 'NOT EXISTS', 'IN', 'NOT IN', 'CASE', 'WHEN', 'THEN', 'ELSE', 'END',
        'ASC', 'DESC', 'IS NULL', 'IS NOT NULL', 'LIKE', 'BETWEEN', 'OPTION', 'INTERSECT'];
    const functions = ['ROW_NUMBER', 'OVER', 'count_big', 'COALESCE', 'CAST', 'CONVERT', 'ISNULL'];

    let result = sql;
    result = result.replace(/&#x27;([^&#]*(?:&#[^x][^;]*;[^&#]*)*)&#x27;/g, '<span class="sql-string">\'$1\'</span>');
    result = result.replace(/\b(\d+)\b/g, '<span class="sql-number">$1</span>');
    for (const fn of functions) {
        result = result.replace(new RegExp(`\\b(${fn})\\b`, 'gi'), '<span class="sql-function">$1</span>');
    }
    for (const kw of keywords) {
        result = result.replace(new RegExp(`\\b(${kw.replace(' ', '\\s+')})\\b`, 'gi'), '<span class="sql-keyword">$1</span>');
    }
    result = result.replace(/\b(dbo\.\w+)\b/g, '<span class="sql-table">$1</span>');
    result = result.replace(/\b(cte\d+\w*)\b/g, '<span class="sql-column">$1</span>');
    return result;
}

function escapeHtml(text) {
    if (!text) return '';
    return text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;').replace(/'/g, '&#x27;');
}

document.addEventListener('DOMContentLoaded', () => {
    document.getElementById('fhir-url').addEventListener('keypress', e => { if (e.key === 'Enter') parseUrl(); });
    document.getElementById('continuation-token').addEventListener('keypress', e => { if (e.key === 'Enter') parseUrl(); });
});
