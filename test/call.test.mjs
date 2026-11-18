import { test } from 'node:test';
import assert from 'node:assert/strict';
import vm from 'node:vm';
import { readFile } from 'node:fs/promises';

const source = await readFile(new URL('../public/app.js', import.meta.url), 'utf8');
const flush = async () => { for (let i = 0; i < 4; i++) await new Promise(setImmediate); };

async function browser(t, getUserMedia, AudioContext) {
  class Element {
    textContent = '';
    innerHTML = '';
    disabled = false;
    attributes = {};
    handlers = {};
    children = [];
    selectors = new Map();
    classList = { toggle() {} };
    dataset = {};
    addEventListener(name, handler) { this.handlers[name] = handler; }
    querySelector(selector) {
      if (!this.selectors.has(selector)) this.selectors.set(selector, new Element());
      return this.selectors.get(selector);
    }
    setAttribute(key, value) { this.attributes[key] = value; }
    append(...children) { this.children.push(...children); }
    remove() {}
    getContext() { return {}; }
    pause() { this.paused = true; }
    async play() { this.paused = false; }
    click() { this.handlers.click?.(); }
  }
  const elements = new Map();
  const element = (id) => {
    if (!elements.has(id)) elements.set(id, new Element());
    return elements.get(id);
  };
  element('voice').value = 'marin';
  element('language').value = 'zh';
  const track = { enabled: true, stopped: false, stop() { this.stopped = true; }, addEventListener() {} };
  const stream = { getTracks: () => [track], getAudioTracks: () => [track] };
  let peer;
  class Peer {
    connectionState = 'new';
    constructor() { peer = this; }
    addTrack() {}
    createDataChannel() { return this.channel = { close() { this.closed = true; } }; }
    async createOffer() { return { type: 'offer', sdp: 'v=0' }; }
    async setLocalDescription() {}
    async setRemoteDescription() { this.connectionState = 'connected'; this.onconnectionstatechange(); }
    close() { this.connectionState = 'closed'; }
  }
  const windowHandlers = {};
  let permissionCalls = 0;
  const sandbox = {
    ...(AudioContext ? { AudioContext } : {}),
    document: { getElementById: element, body: new Element(), createElement: () => new Element(), addEventListener() {} },
    window: { isSecureContext: true, RTCPeerConnection: Peer, addEventListener: (name, handler) => { windowHandlers[name] = handler; } },
    navigator: { mediaDevices: { getUserMedia: async () => { permissionCalls++; return getUserMedia ? getUserMedia(stream) : stream; } } },
    RTCPeerConnection: Peer, AbortController, setTimeout, clearTimeout, setInterval, clearInterval,
    fetch: async (url) => url === '/api/status' ? { ok: true, json: async () => ({ configured: true }) } : { ok: true, text: async () => 'v=0 answer' },
    matchMedia: () => ({ matches: false }), ResizeObserver: class { observe() {} }, requestAnimationFrame() {},
  };
  vm.runInNewContext(source, sandbox);
  t.after(() => windowHandlers.pagehide());
  await flush();
  return { element, track, stream, peer: () => peer, permissionCalls: () => permissionCalls };
}

test('visual preview never opens the microphone; real call can mute, show transcripts, and hang up', async (t) => {
  const b = await browser(t);
  b.element('preview').click();
  assert.equal(b.permissionCalls(), 0);
  assert.match(b.element('stage-status').textContent, /视觉预览/);
  b.element('call').click();
  await flush();
  assert.equal(b.peer().connectionState, 'connected');
  assert.equal(b.element('mute').disabled, false);
  assert.equal(b.element('voice').disabled, true);
  assert.equal(b.element('core-state').textContent, '倾听 · LISTENING');
  b.element('mute').click();
  assert.equal(b.track.enabled, false);
  b.element('mute').click();
  assert.equal(b.track.enabled, true);
  const channel = b.peer().channel;
  channel.onmessage({ data: JSON.stringify({ type: 'input_audio_buffer.speech_stopped' }) });
  assert.equal(b.element('core-state').textContent, '思考 · PROCESSING');
  channel.onmessage({ data: JSON.stringify({ type: 'output_audio_buffer.started' }) });
  assert.equal(b.element('core-state').textContent, '回应 · SPEAKING');
  channel.onmessage({ data: JSON.stringify({ type: 'input_audio_buffer.speech_started' }) });
  assert.equal(b.element('core-state').textContent, '倾听 · LISTENING');
  channel.onmessage({ data: JSON.stringify({ type: 'response.output_audio_transcript.delta', item_id: 'a', delta: '<hello>' }) });
  channel.onmessage({ data: JSON.stringify({ type: 'response.output_audio_transcript.delta', item_id: 'a', delta: ' world' }) });
  const entry = b.element('transcript').children[0];
  assert.equal(entry.children[1].textContent, '<hello> world');
  assert.equal(entry.children[1].innerHTML, '');
  b.element('call').click();
  assert.equal(b.track.stopped, true);
  assert.equal(b.peer().connectionState, 'closed');
  assert.equal(channel.closed, true);
  assert.equal(b.element('mute').disabled, true);
  assert.equal(b.element('voice').disabled, false);
});

test('cancelling while microphone permission is pending stops the late stream', async (t) => {
  let grant;
  const b = await browser(t, (stream) => new Promise((resolve) => { grant = () => resolve(stream); }));
  b.element('call').click();
  b.element('call').click();
  grant();
  await flush();
  assert.equal(b.track.stopped, true);
  assert.equal(b.peer(), undefined);
  assert.equal(b.element('mute').disabled, true);
});

test('denied permissions and lost connections reset controls and release microphone', async (t) => {
  const denied = await browser(t, () => { const error = new Error(); error.name = 'NotAllowedError'; throw error; });
  denied.element('call').click();
  await flush();
  assert.match(denied.element('notice').textContent, /麦克风权限未获允许/);
  assert.equal(denied.element('voice').disabled, false);
  const b = await browser(t);
  b.element('call').click();
  await flush();
  b.peer().connectionState = 'failed';
  b.peer().onconnectionstatechange();
  assert.equal(b.track.stopped, true);
  assert.equal(b.element('mute').disabled, true);
  assert.match(b.element('notice').textContent, /连接已断开/);
});

test('a suspended visualization audio context never blocks the real voice connection', async (t) => {
  class SuspendedContext {
    resume() { return new Promise(() => {}); }
    close() { return Promise.resolve(); }
    createAnalyser() { return { frequencyBinCount: 128 }; }
    createMediaStreamSource() { return { connect() {} }; }
  }
  const b = await browser(t, undefined, SuspendedContext);
  b.element('call').click();
  await flush();
  assert.equal(b.peer().connectionState, 'connected');
  b.element('call').click();
  assert.equal(b.track.stopped, true);
});

test('a recovered voice connection restores link status without changing mute', async (t) => {
  const b = await browser(t);
  b.element('call').click();
  await flush();
  b.element('mute').click();
  b.peer().connectionState = 'disconnected';
  b.peer().onconnectionstatechange();
  assert.equal(b.element('link-state').textContent, '正在恢复');
  b.peer().connectionState = 'connected';
  b.peer().onconnectionstatechange();
  assert.equal(b.element('link-state').textContent, '已连接 / LIVE');
  assert.equal(b.track.enabled, false);
  assert.match(b.element('notice').textContent, /已恢复/);
});
