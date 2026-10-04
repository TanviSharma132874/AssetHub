(function () {
  const { api, identity, esc, badge, btn, table, pageHead, on, toast, formModal, confirm,
    fmtDate, fmtDateTime, qs, isManager } = App;

  const pad = (n) => String(n).padStart(2, '0');
  const dayInput = (d) => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
  const timeRange = (b) => {
    const s = new Date(b.startTime), e = new Date(b.endTime);
    const t = (d) => d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
    return `${s.toLocaleDateString([], { dateStyle: 'medium' })}<span class="sub">${t(s)} – ${t(e)}</span>`;
  };

  // ------------------------------------------------------------------ Dashboard
  async function dashboard(el) {
    const [s, mine] = await Promise.all([
      api('/api/summary'),
      api('/api/bookings' + qs({ mine: true, start: new Date().toISOString() }))
    ]);
    const stat = (n, label) => `<div class="card stat"><div class="num">${n}</div><div class="label">${label}</div></div>`;
    el.innerHTML = pageHead('Dashboard', `Welcome back, ${App.user.fullName}.`) + `
      <div class="grid stats">
        ${stat(s.upcomingBookings, 'My bookings (next 7 days)')}
        ${stat(s.openAudits, 'Open audit cycles')}
        ${stat(s.pendingAuditItems, 'Assets awaiting audit')}
        ${stat(s.unreadNotifications, 'Unread notifications')}
      </div>
      <h3 style="margin:8px 0 10px">My upcoming bookings</h3><div id="mine"></div>`;
    el.querySelector('#mine').innerHTML = table([
      { label: 'Resource', render: (b) => `<strong>${esc(b.resourceName)}</strong>` },
      { label: 'Title', render: (b) => esc(b.title) },
      { label: 'When', render: timeRange }
    ], mine, 'You have no upcoming bookings.');
  }

  // ------------------------------------------------------------------ Resources
  async function resources(el) {
    let rows = [];
    const fields = [
      { name: 'name', label: 'Name', required: true, full: true },
      { name: 'type', label: 'Type', type: 'select', required: true,
        options: ['Room', 'Vehicle', 'Equipment', 'Other'].map((t) => ({ value: t, label: t })) },
      { name: 'capacity', label: 'Capacity', type: 'number', min: '0' },
      { name: 'location', label: 'Location', full: true },
      { name: 'description', label: 'Description', type: 'textarea', full: true },
      { name: 'isActive', label: 'Available for booking', type: 'checkbox' }
    ];

    el.innerHTML = pageHead('Resources', 'Rooms, vehicles and equipment that can be booked.',
      isManager() ? btn('add', 0, '+ New resource', 'primary') : '') + '<div id="list"></div>';

    async function load() {
      rows = await api('/api/resources' + qs({ includeInactive: true }));
      const cols = [
        { label: 'Name', render: (r) => `<strong>${esc(r.name)}</strong><span class="sub">${esc(r.description || '')}</span>` },
        { label: 'Type', render: (r) => esc(r.type) },
        { label: 'Location', render: (r) => esc(r.location || '—') },
        { label: 'Capacity', render: (r) => r.capacity ?? '—' },
        { label: 'Status', render: (r) => badge(r.isActive ? 'Active' : 'Inactive') },
        { label: '', render: (r) => `<div class="actions">${r.isActive ? btn('book', r.id, 'Book', 'primary') : ''}${isManager() ? btn('edit', r.id, 'Edit') : ''}</div>` }
      ];
      el.querySelector('#list').innerHTML = table(cols, rows, 'No resources yet.');
    }

    on(el, {
      add: () => formModal({
        title: 'New resource', fields, values: { type: 'Room', isActive: true },
        onSubmit: async (v) => { await api('/api/resources', { method: 'POST', body: v }); toast('Resource created', 'success'); await load(); }
      }),
      edit: (id) => formModal({
        title: 'Edit resource', fields, values: rows.find((r) => r.id === Number(id)),
        onSubmit: async (v) => { await api('/api/resources/' + id, { method: 'PUT', body: v }); toast('Resource updated', 'success'); await load(); }
      }),
      book: (id) => openBookingForm(Number(id), () => { location.hash = '#/bookings'; })
    });
    await load();
  }

  // ------------------------------------------------------------------ Bookings
  async function openBookingForm(resourceId, done) {
    const list = await api('/api/resources');
    if (!list.length) throw new Error('There are no resources to book yet.');

    const start = new Date();
    start.setMinutes(0, 0, 0);
    start.setHours(start.getHours() + 1);
    const end = new Date(start.getTime() + 60 * 60 * 1000);

    formModal({
      title: 'Book a resource',
      values: { resourceId, startTime: start.toISOString(), endTime: end.toISOString() },
      fields: [
        { name: 'resourceId', label: 'Resource', type: 'select', number: true, required: true, full: true,
          options: App.options(list, 'id', (r) => `${r.name} (${r.type})`) },
        { name: 'title', label: 'Purpose / title', required: true, full: true },
        { name: 'startTime', label: 'Starts', type: 'datetime-local', required: true },
        { name: 'endTime', label: 'Ends', type: 'datetime-local', required: true }
      ],
      submitText: 'Book',
      onSubmit: async (v) => {
        await api('/api/bookings', { method: 'POST', body: v });
        toast('Booking confirmed', 'success');
        done();
      }
    });
  }

  async function bookings(el) {
    const list = await api('/api/resources');
    const filters = { resourceId: '', day: dayInput(new Date()), mine: false, includeCancelled: false };
    let rows = [];

    el.innerHTML = pageHead('Bookings', 'Overlapping bookings for the same resource are rejected automatically.',
      btn('new', 0, '+ New booking', 'primary')) + `
      <div class="toolbar">
        <label class="field">Resource<select id="res"><option value="">All resources</option>
          ${list.map((r) => `<option value="${r.id}">${esc(r.name)}</option>`).join('')}</select></label>
        <label class="field">From day<input type="date" id="day" value="${filters.day}"></label>
        <label class="check"><input type="checkbox" id="mine"> Only my bookings</label>
        <label class="check"><input type="checkbox" id="cancelled"> Show cancelled</label>
      </div>
      <div id="list"></div>`;

    async function load() {
      const params = {
        resourceId: filters.resourceId,
        mine: filters.mine || '',
        includeCancelled: filters.includeCancelled || '',
        start: filters.day ? new Date(filters.day + 'T00:00:00').toISOString() : ''
      };
      rows = await api('/api/bookings' + qs(params));
      const me = App.user;
      const now = Date.now();
      el.querySelector('#list').innerHTML = table([
        { label: 'Resource', render: (b) => `<strong>${esc(b.resourceName)}</strong>` },
        { label: 'Title', render: (b) => esc(b.title) },
        { label: 'When', render: timeRange },
        { label: 'Booked by', render: (b) => esc(b.bookedByName) },
        { label: 'Status', render: (b) => badge(b.status) },
        {
          label: '', render: (b) => (b.status === 'Confirmed' && new Date(b.endTime).getTime() > now && (isManager() || b.bookedById === me.id))
            ? `<div class="actions">${btn('cancel', b.id, 'Cancel', 'danger')}</div>` : ''
        }
      ], rows, 'No bookings for these filters.');
    }

    el.querySelector('#res').addEventListener('change', (e) => { filters.resourceId = e.target.value; load(); });
    el.querySelector('#day').addEventListener('change', (e) => { filters.day = e.target.value; load(); });
    el.querySelector('#mine').addEventListener('change', (e) => { filters.mine = e.target.checked; load(); });
    el.querySelector('#cancelled').addEventListener('change', (e) => { filters.includeCancelled = e.target.checked; load(); });

    on(el, {
      new: () => openBookingForm(undefined, load),
      cancel: async (id) => {
        if (!(await confirm('Cancel this booking?', { okText: 'Cancel booking', danger: true }))) return;
        await api(`/api/bookings/${id}/cancel`, { method: 'POST' });
        toast('Booking cancelled');
        await load();
        refreshUnread();
      }
    });

    await load();
  }

  // ------------------------------------------------------------------ Audits
  async function audits(el) {
    async function showList() {
      const [cycles, categories] = await Promise.all([
        api('/api/audits'),
        isManager() ? identity('/api/categories') : Promise.resolve([])
      ]);

      el.innerHTML = pageHead('Audit Cycles', 'Verify that every asset is where it should be.',
        isManager() ? btn('new', 0, '+ New audit cycle', 'primary') : '') + '<div id="list"></div>';

      el.querySelector('#list').innerHTML = table([
        { label: 'Audit', render: (c) => `<strong>${esc(c.name)}</strong><span class="sub">${esc(c.description || '')}</span>` },
        { label: 'Scope', render: (c) => esc(c.categoryFilterName || 'All assets') },
        { label: 'Due', render: (c) => fmtDate(c.dueDate) },
        { label: 'Status', render: (c) => badge(c.status) },
        {
          label: 'Progress', render: (c) => {
            const done = c.total - c.pending;
            return `${done} / ${c.total} checked<span class="sub">${c.verified} verified · ${c.missing} missing · ${c.damaged} damaged</span>`;
          }
        },
        { label: '', render: (c) => `<div class="actions">${btn('open', c.id, c.status === 'Open' && isManager() ? 'Verify' : 'View', 'primary')}</div>` }
      ], cycles, 'No audit cycles yet.');

      on(el, {
        new: () => formModal({
          title: 'New audit cycle',
          fields: [
            { name: 'name', label: 'Name', required: true, full: true, placeholder: 'e.g. Q4 2026 physical audit' },
            { name: 'categoryId', label: 'Scope', type: 'select', number: true,
              options: App.options(categories, 'id', 'name', { blank: 'All categories' }) },
            { name: 'dueDate', label: 'Due date', type: 'date' },
            { name: 'description', label: 'Description', type: 'textarea', full: true }
          ],
          submitText: 'Create cycle',
          onSubmit: async (v) => {
            const c = await api('/api/audits', { method: 'POST', body: v });
            toast(`Audit created with ${c.total} assets`, 'success');
            refreshUnread();
            await showDetail(c.id);
          }
        }),
        open: (id) => showDetail(Number(id))
      });
    }

    async function showDetail(id) {
      const d = await api('/api/audits/' + id);
      const c = d.cycle;
      let filter = '';
      const open = c.status === 'Open' && isManager();

      el.innerHTML = pageHead(c.name, `${c.categoryFilterName || 'All assets'} · created by ${c.createdByName} · due ${fmtDate(c.dueDate)}`,
        `${btn('back', 0, '← All cycles')}${open ? btn('close', id, 'Close cycle', 'primary') : badge('Closed')}`) + `
        <div class="grid stats">
          <div class="card stat"><div class="num">${c.pending}</div><div class="label">Pending</div></div>
          <div class="card stat"><div class="num">${c.verified}</div><div class="label">Verified</div></div>
          <div class="card stat"><div class="num">${c.missing}</div><div class="label">Missing</div></div>
          <div class="card stat"><div class="num">${c.damaged}</div><div class="label">Damaged</div></div>
        </div>
        <div class="toolbar"><label class="field">Show<select id="filter"><option value="">All</option>
          ${['Pending', 'Verified', 'Missing', 'Damaged'].map((s) => `<option>${s}</option>`).join('')}</select></label></div>
        <div id="items"></div>`;

      const draw = () => {
        const items = d.items.filter((i) => !filter || i.status === filter);
        el.querySelector('#items').innerHTML = table([
          { label: 'Asset', render: (i) => `<span class="mono"><strong>${esc(i.assetTag)}</strong></span><span class="sub">${esc(i.assetName)}</span>` },
          { label: 'Location', render: (i) => esc(i.location || '—') },
          { label: 'Result', render: (i) => badge(i.status) },
          { label: 'Remarks', render: (i) => esc(i.remarks || '—') + (i.verifiedByName ? `<span class="sub">${esc(i.verifiedByName)} · ${fmtDateTime(i.verifiedAt)}</span>` : '') },
          {
            label: '', render: (i) => open
              ? `<div class="actions">${btn('verified', i.id, 'Verified', 'success')}${btn('missing', i.id, 'Missing', 'danger')}${btn('damaged', i.id, 'Damaged', 'danger')}</div>` : ''
          }
        ], items, 'No assets in this view.');
      };

      el.querySelector('#filter').addEventListener('change', (e) => { filter = e.target.value; draw(); });
      draw();

      const mark = async (itemId, status, remarks) => {
        await api(`/api/audits/${id}/items/${itemId}`, { method: 'PUT', body: { status, remarks } });
        refreshUnread();
        await showDetail(id);
      };
      const markWithRemarks = (itemId, status) => {
        const item = d.items.find((i) => i.id === Number(itemId));
        formModal({
          title: `Mark ${item.assetTag} as ${status}`,
          fields: [{ name: 'remarks', label: 'Remarks', type: 'textarea', required: true, full: true,
            hint: status === 'Missing' ? 'Where was it last seen? Who should be contacted?' : 'Describe the damage.' }],
          submitText: `Mark ${status}`,
          onSubmit: (v) => mark(itemId, status, v.remarks)
        });
      };

      on(el, {
        back: () => showList(),
        verified: (itemId) => mark(itemId, 'Verified', null),
        missing: (itemId) => markWithRemarks(itemId, 'Missing'),
        damaged: (itemId) => markWithRemarks(itemId, 'Damaged'),
        close: async () => {
          const force = c.pending > 0;
          const msg = force
            ? `${c.pending} asset(s) have not been checked. Close the cycle anyway?`
            : 'Close this audit cycle? Results can no longer be changed.';
          if (!(await confirm(msg, { okText: 'Close cycle' }))) return;
          await api(`/api/audits/${id}/close`, { method: 'POST', body: { force } });
          toast('Audit cycle closed', 'success');
          refreshUnread();
          await showDetail(id);
        }
      });
    }

    await showList();
  }

  // ------------------------------------------------------------------ Notifications
  async function notifications(el) {
    async function load() {
      const rows = await api('/api/notifications');
      el.innerHTML = pageHead('Notifications', 'Booking confirmations, reminders and audit alerts.',
        rows.some((n) => !n.isRead) ? btn('readall', 0, 'Mark all as read') : '') + `<div id="list" class="grid"></div>`;

      el.querySelector('#list').innerHTML = rows.length ? rows.map((n) => `
        <div class="card card-pad" style="${n.isRead ? 'opacity:.65' : 'border-left:4px solid var(--primary)'}">
          <div style="display:flex;justify-content:space-between;gap:12px;align-items:start">
            <div><strong>${esc(n.title)}</strong> ${badge(n.type)}
              <div style="margin-top:4px">${esc(n.message)}</div>
              <div class="muted" style="margin-top:4px">${fmtDateTime(n.createdAt)}</div></div>
            ${n.isRead ? '' : btn('read', n.id, 'Mark read')}
          </div>
        </div>`).join('') : '<div class="card empty">You have no notifications.</div>';
      refreshUnread();
    }

    on(el, {
      read: async (id) => { await api(`/api/notifications/${id}/read`, { method: 'POST' }); await load(); },
      readall: async () => { await api('/api/notifications/read-all', { method: 'POST' }); await load(); }
    });
    await load();
  }

  // ------------------------------------------------------------------ unread badge in the sidebar
  async function refreshUnread() {
    try {
      const r = await api('/api/notifications/unread-count');
      App.setNavBadge('notifications', r.count);
    } catch { /* badge is optional */ }
  }

  let timer;
  App.start({
    views: [
      { id: 'dashboard', label: 'Dashboard', render: dashboard },
      { id: 'resources', label: 'Resources', render: resources },
      { id: 'bookings', label: 'Bookings', render: bookings },
      { id: 'audits', label: 'Audit Cycles', render: audits },
      { id: 'notifications', label: 'Notifications', render: notifications }
    ],
    onReady: () => {
      refreshUnread();
      clearInterval(timer);
      timer = setInterval(refreshUnread, 60000);
    }
  });
})();
