import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createServer } from '../server.mjs';

async function serve(t, options) {
  const server = createServer(options);
  await new Promise((resolve) => server.listen(0, '127.0.0.1', resolve));
  t.after(() => new Promise((resolve) => server.close(resolve)));
  return `http://127.0.0.1:${server.address().port}`;
}

function call(url, body, headers = {}) {
  return fetch(`${url}/api/call`, { method: 'POST', headers: { Origin: url, 'Content-Type': 'application/json', ...headers }, body: JSON.stringify(body) });
}

test('serves the app and reports missing credentials without revealing secrets', async (t) => {
  const url = await serve(t, { apiKey: '' });
  assert.deepEqual(await (await fetch(`${url}/api/status`)).json(), { configured: false });
  assert.match(await (await fetch(url)).text(), /全息通话/);
  assert.equal((await fetch(`${url}/app.js`)).headers.get('content-type'), 'text/javascript; charset=utf-8');
  assert.equal((await fetch(`${url}/.env`)).status, 404);
  assert.equal((await fetch(`${url}/%2e%2e/server.mjs`)).status, 404);
  assert.equal((await call(url, {})).status, 503);
});

test('validates input and blocks cross-origin calls before contacting OpenAI', async (t) => {
  const url = await serve(t, { apiKey: 'test-secret', upstream: () => { assert.fail('upstream must not be called'); } });
  const body = { sdp: 'v=0\r\n', voice: 'marin', language: 'zh' };
  assert.equal((await call(url, body, { Origin: 'https://other.example' })).status, 403);
  assert.equal((await fetch(`${url}/api/call`, { method: 'POST' })).status, 403);
  assert.equal((await call(url, body, { 'Content-Type': 'text/plain' })).status, 415);
  assert.equal((await call(url, { ...body, voice: 'invalid' })).status, 400);
  assert.equal((await call(url, { ...body, sdp: 'not sdp' })).status, 400);
  assert.equal((await call(url, { ...body, sdp: 'v=0' + 'x'.repeat(65536) })).status, 413);
  assert.equal((await fetch(`${url}/api/call`, { method: 'POST', headers: { Origin: url, 'Content-Type': 'application/json' }, body: '{' })).status, 400);
});

test('exchanges SDP with the server-side credential and expected realtime session', async (t) => {
  const url = await serve(t, {
    apiKey: 'test-secret',
    upstream: async (endpoint, options) => {
      assert.equal(endpoint, 'https://api.openai.com/v1/realtime/calls');
      assert.equal(options.headers.Authorization, 'Bearer test-secret');
      assert.equal(options.body.get('sdp'), 'v=0\r\n');
      const session = JSON.parse(options.body.get('session'));
      assert.equal(session.type, 'realtime');
      assert.equal(session.audio.output.voice, 'cedar');
      assert.equal(session.audio.input.turn_detection.interrupt_response, true);
      assert.match(session.instructions, /English/);
      return new Response('v=0\r\nanswer');
    },
  });
  const response = await call(url, { sdp: 'v=0\r\n', voice: 'cedar', language: 'en' });
  assert.equal(response.status, 200);
  assert.equal(response.headers.get('content-type'), 'application/sdp');
  assert.equal(await response.text(), 'v=0\r\nanswer');
  assert.equal(JSON.stringify(await (await fetch(`${url}/api/status`)).json()).includes('test-secret'), false);
});

test('sanitizes upstream failures and handles network timeouts', async (t) => {
  for (const status of [401, 429]) {
    const url = await serve(t, { apiKey: 'test-secret', upstream: async () => new Response('sensitive account details', { status }) });
    const response = await call(url, { sdp: 'v=0', voice: 'marin', language: 'zh' });
    assert.equal(response.status, 502);
    assert.doesNotMatch(await response.text(), /sensitive|test-secret/);
  }
  const url = await serve(t, { apiKey: 'test-secret', upstream: async () => { throw new DOMException('timeout', 'TimeoutError'); } });
  assert.equal((await call(url, { sdp: 'v=0', voice: 'marin', language: 'zh' })).status, 504);
});
