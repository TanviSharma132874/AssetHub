window.APP_CONFIG = {
  module: 'identity',
  title: 'Identity & Organization',
  apiBase: '',          // this module's own API (same origin)
  identityUrl: '',      // the Identity API is this very server
  modules: [
    { key: 'identity', name: 'Identity & Organization', url: 'http://localhost:5001' },
    { key: 'assets',   name: 'Assets & Allocation',      url: 'http://localhost:5002' },
    { key: 'booking',  name: 'Booking & Audit',          url: 'http://localhost:5003' }
  ]
};
