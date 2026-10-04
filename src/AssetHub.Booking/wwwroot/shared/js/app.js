/*
 * AssetHub shared UI toolkit. Every module page loads this file, then calls App.start({...}).
 * It provides: sign-in (against the Identity module), the layout shell, hash routing,
 * API helpers, modal forms, tables, toasts and small formatting utilities.
 */
(function () {
  'use strict';

  const cfg = window.APP_CONFIG || {};
  const TOKEN_KEY = 'assethub_token';
  const USER_KEY = 'assethub_user';

  const state = {
    token: localStorage.getItem(TOKEN_KEY),
    user: safeParse(localStorage.getItem(USER_KEY)),
    views: [],
    navBadges: {},
    options: {}
  };

  function safeParse(s) { try { return JSON.parse(s); } catch { return null; } }

  const esc = (v) => String(v ?? '').replace(/[&<>"']/g, (c) =>
    ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

  const $ = (sel, el = document) => el.querySelector(sel);

  // ---------------------------------------------------------------- formatting
  const pad = (n) => String(n).padStart(2, '0');
  const fmtDate = (v) => (v ? String(v).slice(0, 10) : '—');
  const fmtDateTime = (v) => (v ? new Date(v).toLocaleString([], { dateStyle: 'medium', timeStyle: 'short' }) : '—');
  const toLocalInput = (v) => {
    if (!v) return '';
    const d = new Date(v);
    return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
  };
  const todayInput = () => {
    const d = new Date();
    return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
  };
  const money = (v) => (v == null ? '—' : Number(v).toLocaleString(undefined, { maximumFractionDigits: 2 }));
  const badge = (text) => `<span class="badge ${esc(String(text).toLowerCase().replace(/\s+/g, ''))}">${esc(text)}</span>`;

  // ---------------------------------------------------------------- http
  async function request(base, path, opts = {}) {
    const { method = 'GET', body, auth = true } = opts;
    const headers = {};
    if (body !== undefined) headers['Content-Type'] = 'application/json';
    if (auth && state.token) headers['Authorization'] = 'Bearer ' + state.token;

    let res;
    try {
      res = await fetch(base + path, {
        method, headers, body: body !== undefined ? JSON.stringify(body) : undefined
      });
    } catch {
      throw new Error('Cannot reach the server. Make sure the service is running.');
    }

    if (res.status === 401 && auth) {
      signOut();
      throw new Error('Your session expired. Please sign in again.');
    }

    const text = await res.text();
    let data = null;
    if (text) { try { data = JSON.parse(text); } catch { data = text; } }

    if (!res.ok) {
      throw new Error((data && (data.error || data.title)) || `Request failed (${res.status})`);
    }
    return data;
  }

  const api = (path, opts) => request(cfg.apiBase || '', path, opts);
  const identity = (path, opts) => request(cfg.identityUrl || '', path, opts);
  const qs = (obj) => {
    const p = new URLSearchParams();
    Object.entries(obj).forEach(([k, v]) => { if (v !== '' && v != null) p.set(k, v); });
    const s = p.toString();
    return s ? '?' + s : '';
  };

  // ---------------------------------------------------------------- session
  function setSession(token, user) {
    state.token = token;
    state.user = user;
    localStorage.setItem(TOKEN_KEY, token);
    localStorage.setItem(USER_KEY, JSON.stringify(user));
  }

  function signOut() {
    state.token = null;
    state.user = null;
    localStorage.removeItem(TOKEN_KEY);
    localStorage.removeItem(USER_KEY);
    renderLogin();
  }

  const isManager = () => !!state.user && ['Admin', 'Manager'].includes(state.user.role);
  const isAdmin = () => !!state.user && state.user.role === 'Admin';

  // Modules run on different ports, so each has its own localStorage. When the user jumps from
  // one module to another we hand the token over in the URL fragment (never sent to a server).
  async function consumeHandoffToken() {
    const m = /[#&]sso=([^&]+)/.exec(location.hash);
    if (!m) return;
    const token = decodeURIComponent(m[1]);
    history.replaceState(null, '', location.pathname + location.search);
    try {
      state.token = token;
      const me = await identity('/api/auth/me');
      setSession(token, me);
    } catch {
      state.token = null;
    }
  }

  // ---------------------------------------------------------------- toast / modal
  function toast(message, type = '') {
    let box = $('#toasts');
    if (!box) { box = document.createElement('div'); box.id = 'toasts'; document.body.appendChild(box); }
    const el = document.createElement('div');
    el.className = 'toast ' + type;
    el.textContent = message;
    box.appendChild(el);
    setTimeout(() => el.remove(), 4000);
  }

  function modal({ title, body, submitText = 'Save', onSubmit, wide = false, cancelText = 'Cancel' }) {
    const wrap = document.createElement('div');
    wrap.className = 'modal-backdrop';
    wrap.innerHTML = `
      <div class="modal ${wide ? 'wide' : ''}" role="dialog" aria-modal="true">
        <div class="modal-head"><h3>${esc(title)}</h3><button type="button" class="icon-btn" data-close aria-label="Close">&times;</button></div>
        <form class="modal-body" novalidate>
          ${body}
          <div class="form-error" hidden></div>
          <div class="modal-foot">
            <button type="button" class="btn ghost" data-close>${esc(cancelText)}</button>
            ${onSubmit ? `<button type="submit" class="btn primary">${esc(submitText)}</button>` : ''}
          </div>
        </form>
      </div>`;
    document.body.appendChild(wrap);

    const form = $('form', wrap);
    const errBox = $('.form-error', wrap);
    const close = () => wrap.remove();
    wrap.addEventListener('click', (e) => { if (e.target === wrap || e.target.closest('[data-close]')) close(); });

    form.addEventListener('submit', async (e) => {
      e.preventDefault();
      if (!onSubmit) return close();
      const btn = $('button[type=submit]', form);
      errBox.hidden = true;
      btn.disabled = true;
      try {
        await onSubmit(form);
        close();
      } catch (err) {
        errBox.textContent = err.message;
        errBox.hidden = false;
        btn.disabled = false;
      }
    });

    const first = $('input, select, textarea', form);
    if (first) first.focus();
    return { close, form };
  }

  function confirmDialog(message, { okText = 'Confirm', danger = false } = {}) {
    return new Promise((resolve) => {
      let done = false;
      const finish = (v) => { if (!done) { done = true; resolve(v); } };
      const m = modal({
        title: 'Please confirm',
        body: `<p>${esc(message)}</p>`,
        submitText: okText,
        onSubmit: async () => { finish(true); }
      });
      if (danger) $('button[type=submit]', m.form).classList.add('danger');
      // closing via X/Cancel resolves false
      const obs = new MutationObserver(() => { if (!document.body.contains(m.form)) { finish(false); obs.disconnect(); } });
      obs.observe(document.body, { childList: true });
    });
  }

  // ---------------------------------------------------------------- form helpers
  // field: { name, label, type: text|number|date|datetime-local|select|textarea|checkbox|password|email,
  //          options:[{value,label}], required, full, number, step, placeholder, readonly, hint }
  function fieldHtml(f, values = {}) {
    const v = values[f.name] ?? f.value ?? '';
    const id = 'f_' + f.name;
    const req = f.required ? 'required' : '';
    const ro = f.readonly ? 'readonly' : '';
    const cls = f.full ? 'full' : '';
    if (f.type === 'checkbox') {
      return `<label class="check ${cls}"><input type="checkbox" name="${esc(f.name)}" ${v === true || v === 'true' ? 'checked' : ''}> ${esc(f.label)}</label>`;
    }
    let control;
    if (f.type === 'select') {
      const opts = (f.options || []).map((o) =>
        `<option value="${esc(o.value)}" ${String(o.value) === String(v) ? 'selected' : ''}>${esc(o.label)}</option>`).join('');
      control = `<select id="${id}" name="${esc(f.name)}" ${req}>${opts}</select>`;
    } else if (f.type === 'textarea') {
      control = `<textarea id="${id}" name="${esc(f.name)}" ${req} ${ro} placeholder="${esc(f.placeholder || '')}">${esc(v)}</textarea>`;
    } else {
      let val = v;
      if (f.type === 'datetime-local') val = toLocalInput(v);
      if (f.type === 'date') val = v ? String(v).slice(0, 10) : '';
      control = `<input id="${id}" type="${f.type || 'text'}" name="${esc(f.name)}" value="${esc(val)}" ${req} ${ro}
        ${f.step ? `step="${esc(f.step)}"` : ''} ${f.min ? `min="${esc(f.min)}"` : ''} placeholder="${esc(f.placeholder || '')}" autocomplete="off">`;
    }
    return `<label class="field ${cls}">${esc(f.label)}${f.required ? ' *' : ''}${control}${f.hint ? `<span class="muted" style="font-weight:400">${esc(f.hint)}</span>` : ''}</label>`;
  }

  const formHtml = (fields, values) => `<div class="form-grid">${fields.map((f) => fieldHtml(f, values)).join('')}</div>`;

  function readForm(form, fields) {
    const out = {};
    for (const f of fields) {
      const el = form.elements[f.name];
      if (!el) continue;
      if (f.type === 'checkbox') { out[f.name] = el.checked; continue; }
      let v = el.value.trim();
      if (f.required && v === '') throw new Error(`${f.label} is required.`);
      if (v === '') { out[f.name] = null; continue; }
      if (f.type === 'number' || f.number) v = Number(v);
      else if (f.type === 'datetime-local') v = new Date(v).toISOString();
      out[f.name] = v;
    }
    return out;
  }

  function formModal({ title, fields, values = {}, submitText, onSubmit, wide }) {
    return modal({
      title, submitText, wide,
      body: formHtml(fields, values),
      onSubmit: async (form) => { await onSubmit(readForm(form, fields)); }
    });
  }

  // ---------------------------------------------------------------- tables
  // cols: [{ label, render: (row) => html, align }]
  function table(cols, rows, emptyText = 'Nothing to show yet.') {
    if (!rows || !rows.length) return `<div class="card empty">${esc(emptyText)}</div>`;
    const head = cols.map((c) => `<th>${esc(c.label)}</th>`).join('');
    const body = rows.map((r) => `<tr>${cols.map((c) => `<td${c.align ? ` style="text-align:${c.align}"` : ''}>${c.render(r)}</td>`).join('')}</tr>`).join('');
    return `<div class="card table-wrap"><table class="data"><thead><tr>${head}</tr></thead><tbody>${body}</tbody></table></div>`;
  }

  const btn = (act, id, label, cls = '') =>
    `<button class="btn small ${cls}" data-act="${esc(act)}" data-id="${esc(id)}">${esc(label)}</button>`;

  // Delegated click handling: App.on(container, { edit: (id, el) => ... })
  // One listener per container; calling on() again replaces the handler map, so handlers from a
  // previous page (or a previous drill-down screen) never fire on the current one.
  function on(container, handlers) {
    container.__handlers = handlers;
    if (container.__bound) return;
    container.__bound = true;
    container.addEventListener('click', async (e) => {
      const el = e.target.closest('[data-act]');
      if (!el || !container.contains(el) || el.disabled) return;
      const h = (container.__handlers || {})[el.dataset.act];
      if (!h) return;
      el.disabled = true;
      try { await h(el.dataset.id, el); }
      catch (err) { toast(err.message, 'error'); }
      finally { el.disabled = false; }
    });
  }

  const pageHead = (title, subtitle, actionsHtml = '') =>
    `<div class="page-head"><div><h2>${esc(title)}</h2>${subtitle ? `<p>${esc(subtitle)}</p>` : ''}</div><div class="actions">${actionsHtml}</div></div>`;

  const options = (items, valueKey, labelFn, { blank } = {}) =>
    [...(blank !== undefined ? [{ value: '', label: blank }] : []),
      ...items.map((i) => ({ value: i[valueKey], label: typeof labelFn === 'function' ? labelFn(i) : i[labelFn] }))];

  // ---------------------------------------------------------------- login
  function renderLogin() {
    const root = $('#app');
    let mode = 'login';
    let departments = [];

    const draw = () => {
      root.innerHTML = `
        <div class="login-wrap"><div class="card login-card">
          <h1>${esc(cfg.title || 'AssetHub')}</h1>
          <div class="muted">Sign in with your AssetHub account</div>
          <div class="tabs">
            <button class="btn ${mode === 'login' ? 'on' : ''}" data-mode="login" type="button">Sign in</button>
            <button class="btn ${mode === 'signup' ? 'on' : ''}" data-mode="signup" type="button">Create account</button>
          </div>
          <form class="stack" novalidate>
            ${mode === 'signup' ? `
              <label class="field">Full name *<input name="fullName" required></label>
              <label class="field">Department
                <select name="departmentId"><option value="">— none —</option>${departments.map((d) => `<option value="${d.id}">${esc(d.name)}</option>`).join('')}</select>
              </label>
              <label class="field">Designation<input name="designation"></label>` : ''}
            <label class="field">Email *<input name="email" type="email" required autocomplete="username"></label>
            <label class="field">Password *<input name="password" type="password" required autocomplete="${mode === 'login' ? 'current-password' : 'new-password'}">
              ${mode === 'signup' ? '<span class="muted" style="font-weight:400">At least 8 characters, with a letter and a digit.</span>' : ''}</label>
            <div class="form-error" hidden></div>
            <button class="btn primary" type="submit">${mode === 'login' ? 'Sign in' : 'Create account'}</button>
          </form>
          <div class="hint">Accounts are managed by the Identity module. One login works for all three modules.</div>
        </div></div>`;

      root.querySelectorAll('[data-mode]').forEach((b) => b.addEventListener('click', async () => {
        mode = b.dataset.mode;
        if (mode === 'signup' && !departments.length) {
          try { departments = await identity('/api/departments', { auth: false }); } catch { /* optional */ }
        }
        draw();
      }));

      $('form', root).addEventListener('submit', async (e) => {
        e.preventDefault();
        const form = e.target;
        const err = $('.form-error', form);
        const submit = $('button[type=submit]', form);
        err.hidden = true;
        submit.disabled = true;
        try {
          const f = Object.fromEntries(new FormData(form).entries());
          let body;
          if (mode === 'login') {
            body = { email: f.email, password: f.password };
          } else {
            body = {
              fullName: f.fullName, email: f.email, password: f.password,
              departmentId: f.departmentId ? Number(f.departmentId) : null,
              designation: f.designation || null
            };
          }
          const res = await identity(mode === 'login' ? '/api/auth/login' : '/api/auth/signup', { method: 'POST', body, auth: false });
          setSession(res.token, res.user);
          renderShell();
        } catch (ex) {
          err.textContent = ex.message;
          err.hidden = false;
          submit.disabled = false;
        }
      });
    };
    draw();
  }

  // ---------------------------------------------------------------- shell + routing
  function renderShell() {
    const root = $('#app');
    const u = state.user;
    const views = state.views.filter((v) => !v.roles || v.roles.includes(u.role));
    const others = (cfg.modules || []).filter((m) => m.key !== cfg.module);

    root.innerHTML = `
      <div class="shell">
        <aside class="sidebar">
          <div class="brand">AssetHub</div>
          <div class="brand-sub">${esc(cfg.title || '')}</div>
          <nav class="nav" id="nav">
            <div class="nav-title">This module</div>
            ${views.map((v) => `<a href="#/${esc(v.id)}" data-view="${esc(v.id)}"><span>${esc(v.label)}</span><span class="count" data-badge="${esc(v.id)}" hidden></span></a>`).join('')}
            <div class="nav-title">Other modules</div>
            ${others.map((m) => `<a href="${esc(m.url)}" data-module="1" target="_self"><span>${esc(m.name)}</span><span>↗</span></a>`).join('')}
          </nav>
        </aside>
        <div class="main">
          <header class="topbar">
            <div class="muted">${esc(cfg.title || '')}</div>
            <div class="who"><span><strong>${esc(u.fullName)}</strong> ${badge(u.role)}</span>
              <button class="btn small" id="logout">Sign out</button></div>
          </header>
          <main class="content" id="view"></main>
        </div>
      </div>`;

    $('#logout').addEventListener('click', signOut);
    // pass the session to the other module without making the user log in again
    root.querySelectorAll('a[data-module]').forEach((a) => {
      const base = a.getAttribute('href');
      a.addEventListener('click', () => { a.href = base + '#sso=' + encodeURIComponent(state.token); });
    });

    window.onhashchange = route;
    applyBadges();
    route();
    if (state.options.onReady) state.options.onReady();
  }

  async function route() {
    const views = state.views.filter((v) => !v.roles || v.roles.includes(state.user.role));
    const id = (location.hash.match(/^#\/([\w-]+)/) || [])[1] || views[0].id;
    const view = views.find((v) => v.id === id) || views[0];
    document.querySelectorAll('#nav a[data-view]').forEach((a) => a.classList.toggle('active', a.dataset.view === view.id));
    const el = $('#view');
    el.innerHTML = '<div class="empty">Loading…</div>';
    try {
      await view.render(el);
    } catch (err) {
      el.innerHTML = `<div class="card card-pad"><strong>Could not load this page.</strong><p class="muted">${esc(err.message)}</p></div>`;
    }
  }

  const reload = () => route();

  function setNavBadge(viewId, n) {
    state.navBadges[viewId] = n;
    applyBadges();
  }

  function applyBadges() {
    document.querySelectorAll('[data-badge]').forEach((el) => {
      const n = state.navBadges[el.dataset.badge] || 0;
      el.textContent = n;
      el.hidden = n === 0;
    });
  }

  async function start(options) {
    state.views = options.views;
    state.options = options;
    await consumeHandoffToken();
    if (state.token && state.user) renderShell(); else renderLogin();
  }

  window.App = {
    start, api, identity, qs, esc, badge, btn, on, table, pageHead, options,
    modal, formModal, formHtml, readForm, confirm: confirmDialog, toast,
    fmtDate, fmtDateTime, toLocalInput, todayInput, money,
    reload, setNavBadge, signOut,
    get user() { return state.user; },
    isManager, isAdmin
  };
})();
