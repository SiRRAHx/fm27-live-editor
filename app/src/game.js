// Talks to the editor's plugin inside FM27. The plugin serves a small JSON API
// on this PC only (127.0.0.1), so nothing is reachable from the network.
const BASE = 'http://127.0.0.1:27270/api';

async function call(path, opts = {}) {
  let res;
  try {
    res = await fetch(BASE + path, {
      ...opts,
      // The plugin only answers requests that carry this header.
      headers: { 'Content-Type': 'application/json', 'X-FM27-Editor': '1' },
      signal: AbortSignal.timeout(opts.timeout || 8000),
    });
  } catch {
    const e = new Error('FM27 isn\'t running with the editor plugin.');
    e.offline = true;
    throw e;
  }
  const body = await res.json().catch(() => ({}));
  if (!res.ok) throw new Error(body.error || `The game said ${res.status}.`);
  return body;
}

module.exports = {
  async status() {
    try {
      return { connected: true, ...(await call('/status', { timeout: 2500 })) };
    } catch (e) {
      if (e.offline) return { connected: false };
      throw e;
    }
  },
  search: (q) => call(`/players?q=${encodeURIComponent(q || '')}`),
  player: (id) => call(`/players/${id}`),
  savePlayer: (id, changes) => call(`/players/${id}`, { method: 'PUT', body: JSON.stringify(changes) }),
  searchClubs: (q) => call(`/clubs?q=${encodeURIComponent(q || '')}`),
  club: (id) => call(`/clubs/${id}`),
  saveClub: (id, changes) => call(`/clubs/${id}`, { method: 'PUT', body: JSON.stringify(changes) }),
  saveSquad: (id, values) => call(`/clubs/${id}`, { method: 'POST', body: JSON.stringify(values), timeout: 30000 }),
  transferStatus: () => call('/transfers'),
  // action: move, swap, release, end-loan or undo.
  transfer: (action, req) => call(`/transfers/${action}`, { method: 'POST', body: JSON.stringify(req || {}), timeout: 30000 }),
};
