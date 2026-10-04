(function () {
  const { api, esc, badge, btn, table, pageHead, on, toast, formModal, modal, confirm, fmtDate } = App;

  // ------------------------------------------------------------------ Employee directory
  async function directory(el) {
    const [departments] = await Promise.all([api('/api/departments')]);
    const filters = { search: '', departmentId: '', role: '', includeInactive: false };

    el.innerHTML = pageHead('Employee Directory', 'Everyone in the organisation. Admins can edit people and change roles.') + `
      <div class="toolbar">
        <label class="field grow">Search<input id="q" placeholder="Name, email or designation"></label>
        <label class="field">Department<select id="dept"><option value="">All</option>
          ${departments.map((d) => `<option value="${d.id}">${esc(d.name)}</option>`).join('')}</select></label>
        <label class="field">Role<select id="role"><option value="">All</option><option>Employee</option><option>Manager</option><option>Admin</option></select></label>
        ${App.isAdmin() ? '<label class="check"><input type="checkbox" id="inactive"> Show inactive</label>' : ''}
      </div>
      <div id="list"></div>`;

    const list = el.querySelector('#list');
    let users = [];

    async function load() {
      users = await api('/api/users' + App.qs(filters));
      const cols = [
        { label: 'Name', render: (u) => `<strong>${esc(u.fullName)}</strong><span class="sub">${esc(u.email)}</span>` },
        { label: 'Department', render: (u) => esc(u.departmentName || '—') },
        { label: 'Designation', render: (u) => esc(u.designation || '—') },
        { label: 'Role', render: (u) => badge(u.role) },
        { label: 'Status', render: (u) => badge(u.isActive ? 'Active' : 'Inactive') }
      ];
      if (App.isAdmin()) {
        cols.push({
          label: '', render: (u) => `<div class="actions">${btn('edit', u.id, 'Edit')}${btn('role', u.id, 'Change role')}</div>`
        });
      }
      list.innerHTML = table(cols, users, 'No employees match these filters.');
    }

    let t;
    el.querySelector('#q').addEventListener('input', (e) => { clearTimeout(t); t = setTimeout(() => { filters.search = e.target.value; load().catch((x) => toast(x.message, 'error')); }, 250); });
    el.querySelector('#dept').addEventListener('change', (e) => { filters.departmentId = e.target.value; load(); });
    el.querySelector('#role').addEventListener('change', (e) => { filters.role = e.target.value; load(); });
    const inactive = el.querySelector('#inactive');
    if (inactive) inactive.addEventListener('change', (e) => { filters.includeInactive = e.target.checked; load(); });

    on(el, {
      edit: (id) => {
        const u = users.find((x) => x.id === Number(id));
        formModal({
          title: 'Edit employee',
          values: u,
          fields: [
            { name: 'fullName', label: 'Full name', required: true, full: true },
            { name: 'departmentId', label: 'Department', type: 'select', number: true, options: App.options(departments, 'id', 'name', { blank: '— none —' }) },
            { name: 'designation', label: 'Designation' },
            { name: 'phone', label: 'Phone' },
            { name: 'isActive', label: 'Account is active', type: 'checkbox' }
          ],
          onSubmit: async (v) => {
            await api('/api/users/' + id, { method: 'PUT', body: v });
            toast('Employee updated', 'success');
            await load();
          }
        });
      },
      role: (id) => {
        const u = users.find((x) => x.id === Number(id));
        formModal({
          title: `Change role: ${u.fullName}`,
          values: { role: u.role },
          fields: [{
            name: 'role', label: 'Role', type: 'select', required: true,
            options: ['Employee', 'Manager', 'Admin'].map((r) => ({ value: r, label: r })),
            hint: 'Employee: self-service. Manager: manages assets, bookings and audits. Admin: manages everything, including roles. The person must sign in again for a new role to apply.'
          }],
          onSubmit: async (v) => {
            await api(`/api/users/${id}/role`, { method: 'PUT', body: { role: v.role } });
            toast('Role updated', 'success');
            await load();
          }
        });
      }
    });

    await load();
  }

  // ------------------------------------------------------------------ Departments
  async function departments(el) {
    let rows = [];
    const fields = [
      { name: 'name', label: 'Name', required: true, full: true },
      { name: 'code', label: 'Code', required: true, hint: 'Short code, e.g. IT' },
      { name: 'description', label: 'Description', type: 'textarea', full: true }
    ];

    async function load() {
      rows = await api('/api/departments');
      const cols = [
        { label: 'Name', render: (d) => `<strong>${esc(d.name)}</strong>` },
        { label: 'Code', render: (d) => `<span class="mono">${esc(d.code)}</span>` },
        { label: 'Description', render: (d) => esc(d.description || '—') },
        { label: 'Employees', render: (d) => d.employeeCount }
      ];
      if (App.isAdmin()) cols.push({ label: '', render: (d) => `<div class="actions">${btn('edit', d.id, 'Edit')}${btn('del', d.id, 'Delete', 'danger')}</div>` });
      el.querySelector('#list').innerHTML = table(cols, rows, 'No departments yet.');
    }

    el.innerHTML = pageHead('Departments', 'Organisational units that employees belong to.',
      App.isAdmin() ? btn('add', 0, '+ New department', 'primary') : '') + '<div id="list"></div>';

    on(el, {
      add: () => formModal({
        title: 'New department', fields,
        onSubmit: async (v) => { await api('/api/departments', { method: 'POST', body: v }); toast('Department created', 'success'); await load(); }
      }),
      edit: (id) => formModal({
        title: 'Edit department', fields, values: rows.find((d) => d.id === Number(id)),
        onSubmit: async (v) => { await api('/api/departments/' + id, { method: 'PUT', body: v }); toast('Department updated', 'success'); await load(); }
      }),
      del: async (id) => {
        const d = rows.find((x) => x.id === Number(id));
        if (!(await confirm(`Delete department "${d.name}"?`, { okText: 'Delete', danger: true }))) return;
        await api('/api/departments/' + id, { method: 'DELETE' });
        toast('Department deleted', 'success');
        await load();
      }
    });
    await load();
  }

  // ------------------------------------------------------------------ Asset categories
  async function categories(el) {
    let rows = [];
    const createFields = [
      { name: 'name', label: 'Name', required: true, full: true },
      { name: 'prefix', label: 'Tag prefix', required: true, hint: '2-5 letters. Asset tags look like LAP-0001.' },
      { name: 'description', label: 'Description', type: 'textarea', full: true }
    ];
    const editFields = [
      { name: 'name', label: 'Name', required: true, full: true },
      { name: 'prefix', label: 'Tag prefix', readonly: true, hint: 'Cannot be changed after creation.' },
      { name: 'description', label: 'Description', type: 'textarea', full: true }
    ];

    async function load() {
      rows = await api('/api/categories');
      const cols = [
        { label: 'Name', render: (c) => `<strong>${esc(c.name)}</strong>` },
        { label: 'Tag prefix', render: (c) => `<span class="mono">${esc(c.prefix)}-0001</span>` },
        { label: 'Description', render: (c) => esc(c.description || '—') }
      ];
      if (App.isAdmin()) cols.push({ label: '', render: (c) => `<div class="actions">${btn('edit', c.id, 'Edit')}${btn('del', c.id, 'Delete', 'danger')}</div>` });
      el.querySelector('#list').innerHTML = table(cols, rows, 'No categories yet.');
    }

    el.innerHTML = pageHead('Asset Categories', 'Used by the Assets module to group assets and build tags.',
      App.isAdmin() ? btn('add', 0, '+ New category', 'primary') : '') + '<div id="list"></div>';

    on(el, {
      add: () => formModal({
        title: 'New category', fields: createFields,
        onSubmit: async (v) => { await api('/api/categories', { method: 'POST', body: v }); toast('Category created', 'success'); await load(); }
      }),
      edit: (id) => formModal({
        title: 'Edit category', fields: editFields, values: rows.find((c) => c.id === Number(id)),
        onSubmit: async (v) => { await api('/api/categories/' + id, { method: 'PUT', body: v }); toast('Category updated', 'success'); await load(); }
      }),
      del: async (id) => {
        const c = rows.find((x) => x.id === Number(id));
        if (!(await confirm(`Delete category "${c.name}"? Existing assets keep their tags.`, { okText: 'Delete', danger: true }))) return;
        await api('/api/categories/' + id, { method: 'DELETE' });
        toast('Category deleted', 'success');
        await load();
      }
    });
    await load();
  }

  // ------------------------------------------------------------------ My profile
  async function profile(el) {
    const me = await api('/api/auth/me');
    el.innerHTML = pageHead('My Profile') + `
      <div class="card card-pad" style="max-width:520px">
        <h3>${esc(me.fullName)} ${badge(me.role)}</h3>
        <p class="muted">${esc(me.email)}</p>
        <p>Department: <strong>${esc(me.departmentName || '—')}</strong></p>
        <p>Designation: <strong>${esc(me.designation || '—')}</strong></p>
        <p>Phone: <strong>${esc(me.phone || '—')}</strong></p>
        <p class="muted">Member since ${fmtDate(me.createdAt)}</p>
      </div>`;
  }

  App.start({
    views: [
      { id: 'directory', label: 'Employee Directory', render: directory },
      { id: 'departments', label: 'Departments', render: departments },
      { id: 'categories', label: 'Asset Categories', render: categories },
      { id: 'profile', label: 'My Profile', render: profile }
    ]
  });
})();
