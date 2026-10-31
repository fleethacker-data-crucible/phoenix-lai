// Shared modal behavior for every .modal window (settings, model, providers, client stopped, skill builder).
// Markup carries the semantics (role="dialog", aria-modal, aria-labelledby on the .window); this adds the behavior:
// focus moves into the window on open, Tab stays inside, Esc calls the window's close function, the page behind is
// inert and aria-hidden, the page cannot scroll, and focus goes back to whatever opened the window.
// Usage: Modal.open(modalEl, { onClose: fn, focus: elOrFn }); Modal.close(modalEl).
(function () {
  const stack = [];   // open modals, topmost last: { el, opener, onClose, hidden: [[node, prevInert, prevAriaHidden]], overflow }
  const FOCUSABLE = 'a[href], button, input, select, textarea, summary, [tabindex], [contenteditable=""], [contenteditable="true"]';

  function usable(e) {
    if (e.disabled || e.hidden || e.getAttribute('tabindex') === '-1' || e.type === 'hidden') return false;
    return e.getClientRects().length > 0 && getComputedStyle(e).visibility !== 'hidden';
  }
  // Tab stops inside el, in order. A radio group is one stop (its checked button, else its first).
  function tabbables(el) {
    const seen = {};
    return Array.prototype.filter.call(el.querySelectorAll(FOCUSABLE), e => {
      if (!usable(e)) return false;
      if (e.type === 'radio' && e.name) {
        if (seen[e.name]) return false;
        const checked = el.querySelector('input[type=radio][name="' + e.name + '"]:checked');
        if (checked && checked !== e) return false;
        seen[e.name] = true;
      }
      return true;
    });
  }
  function windowOf(el) { return el.querySelector('.window') || el; }

  // Everything outside the modal (siblings along its path to <body>) becomes inert and aria-hidden.
  function hideBackground(el) {
    const hidden = [];
    for (let n = el; n && n !== document.body && n.parentNode; n = n.parentNode) {
      Array.prototype.forEach.call(n.parentNode.children, s => {
        if (s === n || /^(SCRIPT|STYLE|LINK)$/.test(s.tagName)) return;
        hidden.push([s, s.inert, s.getAttribute('aria-hidden')]);
        s.inert = true; s.setAttribute('aria-hidden', 'true');
      });
    }
    return hidden;
  }
  function restoreBackground(hidden) {
    hidden.forEach(h => {
      h[0].inert = !!h[1];
      if (h[2] === null) h[0].removeAttribute('aria-hidden'); else h[0].setAttribute('aria-hidden', h[2]);
    });
  }

  function open(el, opts) {
    opts = opts || {};
    if (stack.some(m => m.el === el)) return;
    const a = document.activeElement;
    const entry = { el: el, opener: a && a !== document.body ? a : null, onClose: opts.onClose, hidden: hideBackground(el), overflow: document.body.style.overflow };
    stack.push(entry);
    document.body.style.overflow = 'hidden';
    el.classList.add('show');
    const win = windowOf(el);
    if (!win.hasAttribute('tabindex')) win.setAttribute('tabindex', '-1');
    let first = typeof opts.focus === 'function' ? opts.focus() : opts.focus;
    if (!first || !el.contains(first)) first = tabbables(win.querySelector('.body') || win)[0] || tabbables(win)[0] || win;
    first.focus();
  }

  function close(el) {
    const i = stack.findIndex(m => m.el === el);
    if (i < 0) { el.classList.remove('show'); return; }
    const entry = stack.splice(i, 1)[0];
    el.classList.remove('show');
    restoreBackground(entry.hidden);
    if (!stack.length) document.body.style.overflow = entry.overflow;
    else if (i < stack.length) return;   // a lower window closed under a higher one: the top keeps focus
    const back = entry.opener && entry.opener.isConnected && usable(entry.opener) ? entry.opener : document.getElementById('input');
    if (back && back.isConnected && !back.inert) back.focus();
  }

  const top = () => stack[stack.length - 1];

  document.addEventListener('keydown', e => {
    const m = top(); if (!m) return;
    if (e.key === 'Escape') {
      e.stopImmediatePropagation(); e.preventDefault();   // closes the window, not the generation running behind it
      if (m.onClose) m.onClose();
    } else if (e.key === 'Tab') {
      const win = windowOf(m.el), t = tabbables(win), a = document.activeElement;
      if (!t.length) { e.preventDefault(); win.focus(); return; }
      const f = t[0], l = t[t.length - 1];
      if (!win.contains(a) || a === win) { e.preventDefault(); (e.shiftKey ? l : f).focus(); }
      else if (e.shiftKey && a === f) { e.preventDefault(); l.focus(); }
      else if (!e.shiftKey && a === l) { e.preventDefault(); f.focus(); }
    }
  });   // bubble phase: a popup inside the window (a dropdown) handles its own Esc first

  // Safety net: if focus ever lands outside the top window (a script, a stray click), pull it back in.
  document.addEventListener('focusin', e => {
    const m = top();
    if (m && !m.el.contains(e.target)) { const t = tabbables(windowOf(m.el)); (t[0] || windowOf(m.el)).focus(); }
  });

  function closeAll() { stack.slice().reverse().forEach(m => close(m.el)); }

  window.Modal = { open: open, close: close, closeAll: closeAll, isOpen: () => stack.length > 0 };
})();
