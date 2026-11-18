const test = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
const path = require('node:path');

function setup(response = { ok: true, message: 'Shared' }) {
  const elements = Object.fromEntries(['gps-form', 'gps-auto', 'gps-stop', 'gps-indicator', 'gps-status', 'gps-btn'].map(id => [id, {
    action: '/Driver/Trip?handler=PostLocation', checked: false, hidden: true, handlers: {},
    addEventListener(name, fn) { this.handlers[name] = fn; }
  }]));
  let tick, onPosition, onError, pagehide, clears = 0, calls = 0, watches = 0;
  const context = {
    document: { getElementById: id => elements[id] },
    navigator: { geolocation: {
      watchPosition(position, error) { watches++; onPosition = position; onError = error; return 1; },
      clearWatch() { clears++; }
    } },
    setInterval(fn, ms) { assert.equal(ms, 10000); tick = fn; return 1; }, clearInterval() { tick = null; },
    setTimeout() { return 1; }, clearTimeout() {}, AbortController,
    FormData: class { set() {} },
    fetch: async () => { calls++; return { ok: response.ok, status: response.status ?? 200, json: async () => ({ message: response.message }) }; },
    window: { addEventListener(_, fn) { pagehide = fn; } }, Date
  };
  vm.runInNewContext(fs.readFileSync(path.join(__dirname, '../../wwwroot/js/driver-location.js'), 'utf8'), context);
  return {
    elements, enable() { elements['gps-auto'].checked = true; elements['gps-auto'].handlers.change(); },
    position(age = 0) { onPosition({ timestamp: Date.now() - age, coords: { latitude: -29, longitude: 31 } }); },
    async tick() { tick(); await new Promise(resolve => setImmediate(resolve)); },
    stop() { elements['gps-stop'].handlers.click(); }, pagehide() { pagehide(); }, failure() { onError({ message: 'Denied' }); },
    get calls() { return calls; }, get clears() { return clears; }, get watches() { return watches; }, get timer() { return tick; }
  };
}

test('starts off, posts on ten-second tick, and stop clears watch and timer', async () => {
  const f = setup(); assert.equal(f.watches, 0); assert.equal(f.calls, 0);
  f.enable(); assert.equal(f.watches, 1); assert.equal(f.elements['gps-indicator'].hidden, false);
  f.position(); await f.tick(); assert.equal(f.calls, 1);
  f.stop(); assert.equal(f.clears, 1); assert.equal(f.timer, null);
  assert.equal(f.elements['gps-auto'].checked, false); assert.equal(f.elements['gps-indicator'].hidden, true);
});
test('stale fixes are not posted', async () => {
  const f = setup(); f.enable(); f.position(31000); await f.tick(); assert.equal(f.calls, 0);
});
test('429 shows friendly message and keeps sharing', async () => {
  const f = setup({ ok: false, status: 429, message: 'Please wait a few seconds.' }); f.enable(); f.position(); await f.tick();
  assert.equal(f.elements['gps-status'].textContent, 'Please wait a few seconds.'); assert.equal(f.elements['gps-auto'].checked, true);
});
test('rejected GPS stops automatic sharing', async () => {
  const f = setup({ ok: false, status: 400, message: 'GPS jumped too far.' }); f.enable(); f.position(); await f.tick();
  assert.equal(f.elements['gps-status'].textContent, 'GPS jumped too far.'); assert.equal(f.timer, null);
});
test('navigation and permission failure clear watch and timer', () => {
  const f = setup(); f.enable(); f.pagehide(); assert.equal(f.clears, 1); assert.equal(f.timer, null);
  f.enable(); f.failure(); assert.equal(f.clears, 2); assert.equal(f.timer, null); assert.match(f.elements['gps-status'].textContent, /Denied/);
});
