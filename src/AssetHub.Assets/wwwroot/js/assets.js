(function () {
  const { api, identity, esc, badge, btn, table, pageHead, on, toast, formModal, modal, confirm,
    fmtDate, fmtDateTime, money, qs, isManager } = App;

  const employeeLabel = (u) => `${u.fullName}${u.departmentName ? ' · ' + u.departmentName : ''}`;
  const statusOptions = (list) => list.map((s) => ({ value: s, label: s }));

  // ------------------------------------------------------------------ shared dialogs
  async function openAllocateForm(assetId, done) {
    const [assets, employees] = await Promise.all([
      api('/api/assets?status=Available'),
      identity('/api/users')
    ]);
    if (!assets.length) throw new Error('There are no available assets to allocate.');

    formModal({
      title: 'Allocate asset',
      values: { assetId },
      fields: [
        { name: 'assetId', label: 'Asset', type: 'select', number: true, required: true, full: true,
          options: App.options(assets, 'id', (a) => `${a.assetTag} — ${a.name}`) },
        { name: 'employeeId', label: 'Employee', type: 'select', number: true, required: true, full: true,
          options: App.options(employees, 'id', employeeLabel) },
        { name: 'expectedReturnDate', label: 'Expected return date', type: 'date' },
        { name: 'notes', label: 'Notes', type: 'textarea', full: true }
      ],
      onSubmit: async (v) => {
        await api('/api/allocations', { method: 'POST', body: v });
        toast('Asset allocated', 'success');
        done();
      }
    });
  }

  async function openTransferForm(assetId, done) {
    const [allocs, employees] = await Promise.all([
      api('/api/allocations?active=true'),
      identity('/api/users')
    ]);
    if (!allocs.length) throw new Error('There are no allocated assets that can be transferred.');

    formModal({
      title: 'Request asset transfer',
      values: { assetId },
      fields: [
        { name: 'assetId', label: 'Asset', type: 'select', number: true, required: true, full: true,
          options: App.options(allocs, 'assetId', (a) => `${a.assetTag} — ${a.assetName} (held by ${a.employeeName})`) },
        { name: 'toEmployeeId', label: 'Transfer to', type: 'select', number: true, required: true, full: true,
          options: App.options(employees, 'id', employeeLabel) },
        { name: 'reason', label: 'Reason', type: 'textarea', full: true }
      ],
      submitText: 'Submit request',
      onSubmit: async (v) => {
        await api('/api/transfers', { method: 'POST', body: v });
        toast('Transfer request submitted for approval', 'success');
        done();
      }
    });
  }

  async function openMaintenanceForm(assetId, done) {
    let choices;
    if (isManager()) {
      const all = await api('/api/assets?excludeRetired=true');
      choices = all.filter((a) => a.status !== 'UnderMaintenance').map((a) => ({ id: a.id, label: `${a.assetTag} — ${a.name}` }));
    } else {
      const mine = await api('/api/allocations/mine');
      choices = mine.map((a) => ({ id: a.assetId, label: `${a.assetTag} — ${a.assetName}` }));
    }
    if (!choices.length) throw new Error('There are no assets you can report an issue for.');

    formModal({
      title: 'Request maintenance',
      values: { assetId, priority: 'Medium' },
      fields: [
        { name: 'assetId', label: 'Asset', type: 'select', number: true, required: true, full: true,
          options: App.options(choices, 'id', 'label') },
        { name: 'priority', label: 'Priority', type: 'select', required: true,
          options: statusOptions(['Low', 'Medium', 'High', 'Critical']) },
        { name: 'issue', label: 'What is wrong?', type: 'textarea', required: true, full: true }
      ],
      submitText: 'Submit request',
      onSubmit: async (v) => {
        await api('/api/maintenance', { method: 'POST', body: v });
        toast('Maintenance request submitted', 'success');
        done();
      }
    });
  }

  function noteModal(title, label, onSubmit, submitText) {
    formModal({
      title, submitText,
      fields: [{ name: 'note', label, type: 'textarea', full: true }],
      onSubmit
    });
  }

  // ------------------------------------------------------------------ Dashboard
  async function dashboard(el) {
    const [d, mine] = await Promise.all([api('/api/dashboard'), api('/api/allocations/mine')]);
    const s = d.assetsByStatus;
    const stat = (n, label) => `<div class="card stat"><div class="num">${n}</div><div class="label">${label}</div></div>`;

    el.innerHTML = pageHead('Dashboard', `Welcome back, ${App.user.fullName}.`) + `
      <div class="grid stats">
        ${stat(d.totalAssets, 'Total assets')}
        ${stat(s.Available, 'Available')}
        ${stat(s.Allocated, 'Allocated')}
        ${stat(s.UnderMaintenance, 'Under maintenance')}
        ${stat(s.Retired, 'Retired')}
      </div>
      <div class="grid stats">
        ${stat(d.myAllocations, 'Assets with me')}
        ${stat(d.pendingTransfers, 'Pending transfers')}
        ${stat(d.pendingMaintenance, 'Pending maintenance')}
      </div>
      <h3 style="margin:8px 0 10px">Assets allocated to me</h3>
      <div id="mine"></div>`;

    el.querySelector('#mine').innerHTML = table([
      { label: 'Tag', render: (a) => `<span class="mono">${esc(a.assetTag)}</span>` },
      { label: 'Asset', render: (a) => esc(a.assetName) },
      { label: 'Since', render: (a) => fmtDate(a.allocatedAt) },
      { label: 'Expected return', render: (a) => fmtDate(a.expectedReturnDate) }
    ], mine, 'No assets are allocated to you.');
  }

  // ------------------------------------------------------------------ Assets
  async function assets(el) {
    const categories = await identity('/api/categories');
    const filters = { search: '', status: '', categoryId: '' };
    let rows = [];

    el.innerHTML = pageHead('Assets', 'Every registered asset, its status and current holder.',
      isManager() ? btn('register', 0, '+ Register asset', 'primary') : '') + `
      <div class="toolbar">
        <label class="field grow">Search<input id="q" placeholder="Tag, name or serial number"></label>
        <label class="field">Status<select id="status"><option value="">All</option>
          ${['Available', 'Allocated', 'UnderMaintenance', 'Retired'].map((s) => `<option>${s}</option>`).join('')}</select></label>
        <label class="field">Category<select id="cat"><option value="">All</option>
          ${categories.map((c) => `<option value="${c.id}">${esc(c.name)}</option>`).join('')}</select></label>
      </div>
      <div id="list"></div>`;

    async function load() {
      rows = await api('/api/assets' + qs(filters));
      const me = App.user;
      el.querySelector('#list').innerHTML = table([
        { label: 'Tag', render: (a) => `<span class="mono"><strong>${esc(a.assetTag)}</strong></span>` },
        { label: 'Asset', render: (a) => `${esc(a.name)}<span class="sub">${esc(a.serialNumber || '')}</span>` },
        { label: 'Category', render: (a) => esc(a.categoryName) },
        { label: 'Status', render: (a) => badge(a.status) },
        { label: 'Held by', render: (a) => esc(a.currentHolderName || '—') },
        { label: 'Location', render: (a) => esc(a.location || '—') },
        {
          label: '', render: (a) => {
            const b = [btn('view', a.id, 'Details')];
            if (isManager()) {
              if (a.status === 'Available') b.push(btn('allocate', a.id, 'Allocate', 'primary'));
              b.push(btn('edit', a.id, 'Edit'));
            }
            const canReport = a.status !== 'Retired' && a.status !== 'UnderMaintenance' && (isManager() || a.currentHolderId === me.id);
            if (canReport) b.push(btn('issue', a.id, 'Report issue'));
            if (isManager() && a.status === 'Available') b.push(btn('retire', a.id, 'Retire', 'danger'));
            return `<div class="actions">${b.join('')}</div>`;
          }
        }
      ], rows, 'No assets match these filters.');
    }

    let t;
    el.querySelector('#q').addEventListener('input', (e) => { clearTimeout(t); t = setTimeout(() => { filters.search = e.target.value; load().catch((x) => toast(x.message, 'error')); }, 250); });
    el.querySelector('#status').addEventListener('change', (e) => { filters.status = e.target.value; load(); });
    el.querySelector('#cat').addEventListener('change', (e) => { filters.categoryId = e.target.value; load(); });

    const assetFields = (create) => [
      { name: 'name', label: 'Name', required: true, full: true, placeholder: 'e.g. Dell Latitude 5440' },
      ...(create ? [{ name: 'categoryId', label: 'Category', type: 'select', number: true, required: true,
        options: App.options(categories, 'id', (c) => `${c.name} (${c.prefix})`), hint: 'The asset tag is generated automatically.' }] : []),
      { name: 'serialNumber', label: 'Serial number' },
      { name: 'purchaseDate', label: 'Purchase date', type: 'date' },
      { name: 'purchaseCost', label: 'Purchase cost', type: 'number', step: '0.01', min: '0' },
      { name: 'location', label: 'Location' },
      { name: 'notes', label: 'Notes', type: 'textarea', full: true }
    ];

    on(el, {
      register: () => formModal({
        title: 'Register asset', fields: assetFields(true), submitText: 'Register',
        onSubmit: async (v) => {
          const a = await api('/api/assets', { method: 'POST', body: v });
          toast(`Registered ${a.assetTag}`, 'success');
          await load();
        }
      }),
      edit: (id) => formModal({
        title: 'Edit asset', fields: assetFields(false), values: rows.find((a) => a.id === Number(id)),
        onSubmit: async (v) => { await api('/api/assets/' + id, { method: 'PUT', body: v }); toast('Asset updated', 'success'); await load(); }
      }),
      allocate: (id) => openAllocateForm(Number(id), load),
      issue: (id) => openMaintenanceForm(Number(id), load),
      retire: async (id) => {
        const a = rows.find((x) => x.id === Number(id));
        if (!(await confirm(`Retire ${a.assetTag} (${a.name})? It can no longer be allocated.`, { okText: 'Retire', danger: true }))) return;
        await api(`/api/assets/${id}/retire`, { method: 'POST' });
        toast('Asset retired', 'success');
        await load();
      },
      view: async (id) => {
        const d = await api('/api/assets/' + id);
        const a = d.asset;
        const hist = table([
          { label: 'Employee', render: (h) => esc(h.employeeName) },
          { label: 'From', render: (h) => fmtDateTime(h.allocatedAt) },
          { label: 'Returned', render: (h) => (h.returnedAt ? fmtDateTime(h.returnedAt) : badge('Active')) },
          { label: 'Notes', render: (h) => esc(h.returnNotes || h.notes || '—') }
        ], d.history, 'Never allocated.');
        const maint = table([
          { label: 'Issue', render: (m) => esc(m.issue) },
          { label: 'Priority', render: (m) => badge(m.priority) },
          { label: 'Status', render: (m) => badge(m.status) },
          { label: 'Requested', render: (m) => fmtDate(m.requestedAt) }
        ], d.maintenance, 'No maintenance history.');
        modal({
          title: `${a.assetTag} — ${a.name}`, wide: true,
          body: `
            <div class="form-grid" style="margin-bottom:16px">
              <div><span class="muted">Status</span><br>${badge(a.status)}</div>
              <div><span class="muted">Held by</span><br>${esc(a.currentHolderName || '—')}</div>
              <div><span class="muted">Category</span><br>${esc(a.categoryName)}</div>
              <div><span class="muted">Serial number</span><br>${esc(a.serialNumber || '—')}</div>
              <div><span class="muted">Purchased</span><br>${fmtDate(a.purchaseDate)} · ${money(a.purchaseCost)}</div>
              <div><span class="muted">Location</span><br>${esc(a.location || '—')}</div>
              ${a.notes ? `<div class="full"><span class="muted">Notes</span><br>${esc(a.notes)}</div>` : ''}
            </div>
            <h4>Allocation history</h4>${hist}
            <h4 style="margin-top:16px">Maintenance history</h4>${maint}`,
          cancelText: 'Close'
        });
      }
    });

    await load();
  }

  // ------------------------------------------------------------------ Allocations
  async function allocations(el) {
    let activeOnly = true;
    let rows = [];

    el.innerHTML = pageHead('Allocations', isManager() ? 'Who holds what. Allocation is conflict-checked: an asset can only be with one person at a time.' : 'Assets allocated to you.',
      isManager() ? btn('allocate', 0, '+ Allocate asset', 'primary') : '') + `
      <div class="toolbar"><label class="check"><input type="checkbox" id="active" checked> Show active allocations only</label></div>
      <div id="list"></div>`;

    async function load() {
      rows = await api('/api/allocations' + qs({ active: activeOnly }));
      const today = new Date().toISOString().slice(0, 10);
      el.querySelector('#list').innerHTML = table([
        { label: 'Asset', render: (a) => `<span class="mono"><strong>${esc(a.assetTag)}</strong></span><span class="sub">${esc(a.assetName)}</span>` },
        { label: 'Employee', render: (a) => `${esc(a.employeeName)}<span class="sub">${esc(a.employeeDepartment || '')}</span>` },
        { label: 'Allocated', render: (a) => fmtDate(a.allocatedAt) },
        {
          label: 'Expected return', render: (a) => {
            const overdue = !a.returnedAt && a.expectedReturnDate && fmtDate(a.expectedReturnDate) < today;
            return overdue ? `${fmtDate(a.expectedReturnDate)} ${badge('Overdue')}` : fmtDate(a.expectedReturnDate);
          }
        },
        { label: 'Returned', render: (a) => (a.returnedAt ? fmtDate(a.returnedAt) : badge('Active')) },
        {
          label: '', render: (a) => a.returnedAt ? '' :
            `<div class="actions">${btn('transfer', a.assetId, 'Transfer')}${btn('return', a.id, 'Return')}</div>`
        }
      ], rows, activeOnly ? 'No active allocations.' : 'No allocations yet.');
    }

    el.querySelector('#active').addEventListener('change', (e) => { activeOnly = e.target.checked; load(); });

    on(el, {
      allocate: () => openAllocateForm(undefined, load),
      transfer: (assetId) => openTransferForm(Number(assetId), load),
      return: (id) => noteModal('Return asset', 'Return notes (condition, accessories…)', async (v) => {
        await api(`/api/allocations/${id}/return`, { method: 'POST', body: { notes: v.note } });
        toast('Asset returned', 'success');
        await load();
      }, 'Mark as returned')
    });

    await load();
  }

  // ------------------------------------------------------------------ Transfers
  async function transfers(el) {
    let status = '';
    let rows = [];

    el.innerHTML = pageHead('Transfer Requests', 'Hand an allocated asset to another employee. A manager approves each transfer.',
      btn('new', 0, '+ New transfer request', 'primary')) + `
      <div class="toolbar"><label class="field">Status<select id="status"><option value="">All</option>
        ${['Pending', 'Approved', 'Rejected', 'Cancelled'].map((s) => `<option>${s}</option>`).join('')}</select></label></div>
      <div id="list"></div>`;

    async function load() {
      rows = await api('/api/transfers' + qs({ status }));
      el.querySelector('#list').innerHTML = table([
        { label: 'Asset', render: (t) => `<span class="mono"><strong>${esc(t.assetTag)}</strong></span><span class="sub">${esc(t.assetName)}</span>` },
        { label: 'From → To', render: (t) => `${esc(t.fromEmployeeName)} → <strong>${esc(t.toEmployeeName)}</strong>` },
        { label: 'Reason', render: (t) => esc(t.reason || '—') },
        { label: 'Requested', render: (t) => `${fmtDate(t.requestedAt)}<span class="sub">by ${esc(t.requestedByName)}</span>` },
        { label: 'Status', render: (t) => badge(t.status) + (t.decidedByName ? `<span class="sub">${esc(t.decidedByName)}${t.decisionNote ? ': ' + esc(t.decisionNote) : ''}</span>` : '') },
        {
          label: '', render: (t) => {
            if (t.status !== 'Pending') return '';
            const b = [];
            if (isManager()) b.push(btn('approve', t.id, 'Approve', 'success'), btn('reject', t.id, 'Reject', 'danger'));
            b.push(btn('cancel', t.id, 'Cancel'));
            return `<div class="actions">${b.join('')}</div>`;
          }
        }
      ], rows, 'No transfer requests.');
    }

    el.querySelector('#status').addEventListener('change', (e) => { status = e.target.value; load(); });

    on(el, {
      new: () => openTransferForm(undefined, load),
      approve: (id) => noteModal('Approve transfer', 'Note (optional)', async (v) => {
        await api(`/api/transfers/${id}/approve`, { method: 'POST', body: { note: v.note } });
        toast('Transfer approved — the asset now belongs to the new holder', 'success');
        await load();
      }, 'Approve'),
      reject: (id) => noteModal('Reject transfer', 'Reason (optional)', async (v) => {
        await api(`/api/transfers/${id}/reject`, { method: 'POST', body: { note: v.note } });
        toast('Transfer rejected');
        await load();
      }, 'Reject'),
      cancel: async (id) => {
        if (!(await confirm('Cancel this transfer request?'))) return;
        await api(`/api/transfers/${id}/cancel`, { method: 'POST' });
        toast('Request cancelled');
        await load();
      }
    });

    await load();
  }

  // ------------------------------------------------------------------ Maintenance
  async function maintenance(el) {
    let status = '';
    let rows = [];

    el.innerHTML = pageHead('Maintenance Requests',
      'Report a faulty asset. When a manager approves, the asset goes under maintenance and is taken off its holder.',
      btn('new', 0, '+ New maintenance request', 'primary')) + `
      <div class="toolbar"><label class="field">Status<select id="status"><option value="">All</option>
        ${['Pending', 'Approved', 'Rejected', 'Completed'].map((s) => `<option>${s}</option>`).join('')}</select></label></div>
      <div id="list"></div>`;

    async function load() {
      rows = await api('/api/maintenance' + qs({ status }));
      el.querySelector('#list').innerHTML = table([
        { label: 'Asset', render: (m) => `<span class="mono"><strong>${esc(m.assetTag)}</strong></span><span class="sub">${esc(m.assetName)}</span>` },
        { label: 'Issue', render: (m) => esc(m.issue) },
        { label: 'Priority', render: (m) => badge(m.priority) },
        { label: 'Requested', render: (m) => `${fmtDate(m.requestedAt)}<span class="sub">by ${esc(m.requestedByName)}</span>` },
        {
          label: 'Status', render: (m) => badge(m.status) +
            (m.decidedByName ? `<span class="sub">${esc(m.decidedByName)}${m.decisionNote ? ': ' + esc(m.decisionNote) : ''}</span>` : '') +
            (m.completionNote ? `<span class="sub">${esc(m.completionNote)}</span>` : '')
        },
        {
          label: '', render: (m) => {
            if (!isManager()) return '';
            if (m.status === 'Pending') return `<div class="actions">${btn('approve', m.id, 'Approve', 'success')}${btn('reject', m.id, 'Reject', 'danger')}</div>`;
            if (m.status === 'Approved') return `<div class="actions">${btn('complete', m.id, 'Mark completed', 'primary')}</div>`;
            return '';
          }
        }
      ], rows, 'No maintenance requests.');
    }

    el.querySelector('#status').addEventListener('change', (e) => { status = e.target.value; load(); });

    on(el, {
      new: () => openMaintenanceForm(undefined, load),
      approve: (id) => noteModal('Approve maintenance', 'Note (optional)', async (v) => {
        await api(`/api/maintenance/${id}/approve`, { method: 'POST', body: { note: v.note } });
        toast('Approved — asset is now under maintenance', 'success');
        await load();
      }, 'Approve'),
      reject: (id) => noteModal('Reject maintenance', 'Reason (optional)', async (v) => {
        await api(`/api/maintenance/${id}/reject`, { method: 'POST', body: { note: v.note } });
        toast('Request rejected');
        await load();
      }, 'Reject'),
      complete: (id) => formModal({
        title: 'Complete maintenance',
        fields: [
          { name: 'note', label: 'What was done?', type: 'textarea', full: true },
          { name: 'retire', label: 'Could not be repaired — retire this asset', type: 'checkbox' }
        ],
        submitText: 'Mark completed',
        onSubmit: async (v) => {
          await api(`/api/maintenance/${id}/complete`, { method: 'POST', body: { note: v.note, retire: v.retire } });
          toast('Maintenance completed', 'success');
          await load();
        }
      })
    });

    await load();
  }

  App.start({
    views: [
      { id: 'dashboard', label: 'Dashboard', render: dashboard },
      { id: 'assets', label: 'Assets', render: assets },
      { id: 'allocations', label: 'Allocations', render: allocations },
      { id: 'transfers', label: 'Transfers', render: transfers },
      { id: 'maintenance', label: 'Maintenance', render: maintenance }
    ]
  });
})();
