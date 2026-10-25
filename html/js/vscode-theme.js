// vscode.html only: the chat takes VS Code's theme (colours and fonts). The Phoenix extension's webview reads VS Code's colours and fonts
// and posts them here (it is a different origin, so only postMessage can carry them); they are re-sent when the theme
// changes. Only messages from a VS Code webview are accepted.
window.PHX_VSCODE = true;   // markdown.js: file edits show as activity lines, not code boxes
(function () {
  let theme = null;
  const MAP = {
    bg: ['--bg', '--panel-solid', '--settings-bg'],
    panel: ['--panel'],
    fg: ['--text'],
    soft: ['--soft'],
    dim: ['--dim', '--status-text'],
    line: ['--line', '--line-strong'],
    // the accent stays Phoenix gold (the client's accent setting), not the theme's link colour
    code: ['--code-bg'],
    ok: ['--ok'],
    err: ['--err'],
    font: ['--sans', '--chat'],
    mono: ['--mono'],
    size: ['--chat-size'],
  };
  window.applyVscodeTheme = function () {
    if (!theme) return;
    const root = document.documentElement.style;
    for (const k in MAP) if (theme[k]) for (const v of MAP[k]) root.setProperty(v, theme[k]);
    document.documentElement.classList.toggle('vscode-light', theme.kind === 'light');
    if (typeof drawBackground === 'function') try { drawBackground(); } catch (e) { /* not ready yet */ }
  };
  window.addEventListener('message', (ev) => {
    if (typeof ev.origin !== 'string' || !ev.origin.startsWith('vscode-webview://')) return;
    const d = ev.data;
    if (!d || d.type !== 'phoenix-theme' || typeof d.theme !== 'object') return;
    theme = d.theme;
    window.applyVscodeTheme();
  });
})();
