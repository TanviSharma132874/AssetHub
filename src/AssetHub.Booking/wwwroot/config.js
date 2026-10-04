window.APP_CONFIG = {
  module: 'booking',
  title: 'Booking & Audit',
  apiBase: '',                              // this module's own API (same origin)
  identityUrl: 'http://localhost:5001',     // Identity module: sign-in, categories
  modules: [
    { key: 'identity', name: 'Identity & Organization', url: 'http://localhost:5001' },
    { key: 'assets',   name: 'Assets & Allocation',      url: 'http://localhost:5002' },
    { key: 'booking',  name: 'Booking & Audit',          url: 'http://localhost:5003' }
  ]
};
