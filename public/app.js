const $ = (id) => document.getElementById(id);
const callButton = $('call');
const muteButton = $('mute');
const previewButton = $('preview');
const audio = $('remote-audio');
let state = 'idle';
let current = null;
let preview = false;
let muted = false;
let configured = false;
let activity = 0;
let assistantSpeaking = false;

function notice(message, error = false) {
  $('notice').textContent = message;
  $('notice').classList.toggle('error', error);
}

function setState(next) {
  state = next;
  document.body.dataset.state = next;
  const connected = next === 'connected';
  const connecting = next === 'connecting';
  callButton.classList.toggle('end', connected || connecting);
  callButton.querySelector('use').setAttribute('href', connected || connecting ? '#end' : '#phone');
  callButton.querySelector('span').textContent = connecting ? '取消连接' : connected ? '结束通话' : '开始通话';
  muteButton.disabled = !connected;
  $('voice').disabled = $('language').disabled = connected || connecting;
  previewButton.disabled = connected || connecting;
  $('live-badge').classList.toggle('active', connected || preview);
  $('live-badge').innerHTML = `<i></i> ${connecting ? '连接中' : connected ? '通话中' : preview ? '视觉预览' : '待机中'}`;
  $('call-label').textContent = connecting ? '正在连接 NOVA…' : connected ? '已连接 · 00:00' : '准备好开始了吗？';
  $('call-detail').textContent = connected ? '自然交流，随时可以打断' : connecting ? '正在建立安全语音连接' : '开启麦克风，自然地说出你的想法';
  $('stage-status').textContent = connecting ? '正在与你建立连接' : connected ? '我在听，请说吧' : preview ? '全息视觉预览 · 未连接语音' : '在这里，随时倾听';
  $('transcript-indicator').textContent = connected ? '实时转写' : connecting ? '连接中' : '等待通话';
}

function stopCall() {
  const session = current;
  current = null;
  if (session) {
    clearTimeout(session.timeout);
    clearTimeout(session.disconnectTimeout);
    clearInterval(session.timer);
    session.abort.abort();
    session.channel?.close();
    session.peer?.close();
    session.stream?.getTracks().forEach((track) => track.stop());
    session.context?.close().catch(() => {});
  }
  audio.pause();
  audio.srcObject = null;
  muted = false;
  activity = 0;
  assistantSpeaking = false;
  muteButton.setAttribute('aria-pressed', 'false');
  muteButton.setAttribute('aria-label', '静音麦克风');
  setState('idle');
}

function fail(session, message) {
  if (session !== current) return;
  stopCall();
  notice(message, true);
}

function addTranscript(session, id, role, text, append = false) {
  if (!text) return;
  $('transcript-empty')?.remove();
  let paragraph = session.entries.get(id);
  if (!paragraph) {
    const entry = document.createElement('div');
    entry.className = `transcript-entry ${role}`;
    const name = document.createElement('strong');
    name.textContent = role === 'user' ? '你' : 'NOVA';
    paragraph = document.createElement('p');
    entry.append(name, paragraph);
    $('transcript').append(entry);
    session.entries.set(id, paragraph);
  }
  paragraph.textContent = append ? paragraph.textContent + text : text;
  $('transcript').scrollTop = $('transcript').scrollHeight;
}

function onEvent(session, event) {
  if (session !== current) return;
  if (event.type === 'conversation.item.input_audio_transcription.completed') addTranscript(session, event.item_id, 'user', event.transcript);
  if (event.type === 'response.output_audio_transcript.delta') addTranscript(session, event.item_id, 'assistant', event.delta, true);
  if (event.type === 'response.output_audio_transcript.done') addTranscript(session, event.item_id, 'assistant', event.transcript);
  if (event.type === 'input_audio_buffer.speech_started') $('stage-status').textContent = '正在倾听你的声音…';
  if (event.type === 'input_audio_buffer.speech_stopped') $('stage-status').textContent = '正在思考…';
  if (event.type === 'output_audio_buffer.started') {
    assistantSpeaking = true;
    $('stage-status').textContent = 'NOVA 正在回应';
  }
  if (['output_audio_buffer.stopped', 'output_audio_buffer.cleared'].includes(event.type)) {
    assistantSpeaking = false;
    $('stage-status').textContent = muted ? '麦克风已静音' : '我在听，请说吧';
  }
  if (event.type === 'conversation.item.input_audio_transcription.failed') notice('这句话的字幕转写失败，你仍可以继续通话。', true);
  if (event.type === 'response.done' && event.response?.status === 'failed') fail(session, 'AI 回应失败，请检查语音服务额度后重试。');
  if (event.type === 'error') fail(session, '语音会话出现错误，请重新发起通话。');
}

async function startCall() {
  if (!configured) {
    notice('语音服务尚未就绪，请先配置服务器密钥。你可以点击「体验全息效果」预览。', true);
    return;
  }
  if (!window.isSecureContext || !navigator.mediaDevices?.getUserMedia || !window.RTCPeerConnection) {
    notice('当前浏览器不支持语音通话，请使用 HTTPS 或 localhost，并使用新版 Chrome、Edge 或 Safari。', true);
    return;
  }
  preview = false;
  previewButton.setAttribute('aria-pressed', 'false');
  previewButton.innerHTML = '体验全息效果 <svg><use href="#arrow"/></svg>';
  const session = { abort: new AbortController(), entries: new Map() };
  current = session;
  setState('connecting');
  notice('请允许使用麦克风，连接后即可开始交流。');
  session.timeout = setTimeout(() => fail(session, '连接超时，请检查麦克风权限和网络后重试。'), 40000);
  try {
    const stream = await navigator.mediaDevices.getUserMedia({ audio: { echoCancellation: true, noiseSuppression: true, autoGainControl: true } });
    // Permission prompts can resolve after the user has already cancelled.
    if (current !== session) { stream.getTracks().forEach((track) => track.stop()); return; }
    session.stream = stream;
    stream.getTracks().forEach((track) => track.addEventListener('ended', () => fail(session, '麦克风已断开，请检查设备后重新连接。')));
    const peer = session.peer = new RTCPeerConnection();
    peer.addTrack(stream.getAudioTracks()[0], stream);
    peer.ontrack = (event) => {
      if (session !== current) return;
      audio.srcObject = event.streams[0] || new MediaStream([event.track]);
      audio.play().catch(() => { if (session === current) notice('浏览器暂停了声音播放，点击页面即可恢复。', true); });
    };
    peer.onconnectionstatechange = () => {
      if (session !== current) return;
      if (peer.connectionState === 'connected') {
        clearTimeout(session.timeout);
        clearTimeout(session.disconnectTimeout);
        if (state !== 'connected') {
          session.started = Date.now();
          setState('connected');
          notice('通话已连接 · 再次点击麦克风按钮可静音');
          session.timer = setInterval(() => {
            const elapsed = Math.floor((Date.now() - session.started) / 1000);
            $('call-label').textContent = `已连接 · ${String(Math.floor(elapsed / 60)).padStart(2, '0')}:${String(elapsed % 60).padStart(2, '0')}`;
          }, 1000);
        }
      }
      if (peer.connectionState === 'failed' || peer.connectionState === 'closed') fail(session, '语音连接已断开，请重新发起通话。');
      if (peer.connectionState === 'disconnected') {
        notice('网络暂时断开，正在尝试恢复…', true);
        clearTimeout(session.disconnectTimeout);
        session.disconnectTimeout = setTimeout(() => fail(session, '网络连接已中断，请重新发起通话。'), 10000);
      }
    };
    const channel = session.channel = peer.createDataChannel('oai-events');
    channel.onmessage = ({ data }) => { try { onEvent(session, JSON.parse(data)); } catch { /* Ignore unsupported event payloads. */ } };
    channel.onclose = () => fail(session, '语音会话已结束，可以重新开始通话。');
    // Volume sampling only drives the hologram; WebRTC carries the actual audio.
    try {
      const context = session.context = new AudioContext();
      const analyser = session.analyser = context.createAnalyser();
      analyser.fftSize = 256;
      session.samples = new Uint8Array(analyser.frequencyBinCount);
      context.createMediaStreamSource(stream).connect(analyser);
      await context.resume();
    } catch { /* Audio visualization is optional. */ }
    if (session !== current) return;
    const offer = await peer.createOffer();
    if (session !== current) return;
    await peer.setLocalDescription(offer);
    const response = await fetch('/api/call', {
      method: 'POST', headers: { 'Content-Type': 'application/json' }, signal: session.abort.signal,
      body: JSON.stringify({ sdp: offer.sdp, voice: $('voice').value, language: $('language').value }),
    });
    if (session !== current) return;
    if (!response.ok) {
      const error = await response.json();
      throw new Error(error.error || '通话连接失败。');
    }
    const answer = await response.text();
    if (session !== current) return;
    await peer.setRemoteDescription({ type: 'answer', sdp: answer });
  } catch (error) {
    const messages = { NotAllowedError: '麦克风权限未获允许，请在浏览器设置中允许麦克风后重试。', NotFoundError: '没有找到麦克风，请连接麦克风后重试。', NotReadableError: '无法读取麦克风，请检查设备是否被其他应用占用。' };
    fail(session, messages[error.name] || (error instanceof TypeError ? '网络连接失败，请检查服务器是否正常运行。' : error.message));
  }
}

callButton.addEventListener('click', () => {
  if (state !== 'idle') { stopCall(); notice('通话已结束，麦克风已关闭。'); }
  else startCall();
});
muteButton.addEventListener('click', () => {
  if (state !== 'connected') return;
  muted = !muted;
  current.stream.getAudioTracks().forEach((track) => { track.enabled = !muted; });
  muteButton.setAttribute('aria-pressed', String(muted));
  muteButton.setAttribute('aria-label', muted ? '取消静音' : '静音麦克风');
  $('stage-status').textContent = muted ? '麦克风已静音' : '我在听，请说吧';
});
previewButton.addEventListener('click', () => {
  preview = !preview;
  previewButton.setAttribute('aria-pressed', String(preview));
  previewButton.innerHTML = `${preview ? '结束视觉预览' : '体验全息效果'} <svg><use href="#arrow"/></svg>`;
  setState('idle');
  notice(preview ? '正在预览全息动态效果 · 未开启麦克风，未连接 AI' : '麦克风仅在你发起通话后开启');
});
document.addEventListener('click', () => {
  if (state === 'connected' && audio.paused && audio.srcObject) audio.play().then(() => notice('声音播放已恢复。')).catch(() => {});
});
window.addEventListener('pagehide', stopCall);
fetch('/api/status').then((response) => {
  if (!response.ok) throw new Error('status unavailable');
  return response.json();
}).then((status) => {
  configured = status.configured === true;
  $('service-status').classList.toggle('ready', configured);
  $('service-status').innerHTML = `<i></i>${configured ? '语音服务已就绪' : '语音服务待配置'}`;
}).catch(() => { $('service-status').innerHTML = '<i></i>语音服务不可用'; });

// A procedural point-cloud bust: no image assets, 3D runtime, or tracking needed.
const canvas = $('hologram');
const ctx = canvas.getContext('2d');
const reducedMotion = matchMedia('(prefers-reduced-motion: reduce)');
const points = [];
let width = 0, height = 0;
let lastFrame = 0;
for (let row = 0; row < 91; row++) {
  const v = row / 90;
  const y = -1.23 + v * 1.65;
  const slice = Math.sin(Math.PI * v) ** .55;
  const jaw = v > .57 ? 1 - (v - .57) * .75 : 1;
  for (let col = 0; col < 105; col++) {
    const angle = col / 105 * Math.PI * 2;
    let x = Math.cos(angle) * .57 * slice * jaw;
    let z = Math.sin(angle) * .46 * slice;
    const front = Math.max(0, -Math.sin(angle));
    // Sculpt nose, brow, eye sockets, and mouth into the front of the head.
    const nose = Math.exp(-((x / .10) ** 2) - ((y + .33) / .22) ** 2) * .20;
    const eyes = Math.exp(-(((Math.abs(x) - .22) / .11) ** 2) - ((y + .52) / .055) ** 2);
    const mouth = Math.exp(-((x / .21) ** 2) - ((y + .02) / .035) ** 2);
    z -= front ** 7 * (nose - eyes * .07 - mouth * .05);
    points.push({ x, y, z, glow: eyes * front ** 10, seed: col + row * 1.9 });
  }
}
for (let row = 0; row < 28; row++) {
  const v = row / 27;
  for (let col = 0; col < 110; col++) {
    const a = col / 110 * Math.PI * 2;
    const radius = .21 + Math.max(0, v - .27) ** .7 * 1.05;
    points.push({ x: Math.cos(a) * radius, y: .30 + v * .65, z: Math.sin(a) * (.20 + v * .14), glow: 0, seed: col + row });
  }
}
new ResizeObserver(([entry]) => {
  width = entry.contentRect.width;
  height = entry.contentRect.height;
  const ratio = Math.min(devicePixelRatio || 1, 2);
  canvas.width = width * ratio;
  canvas.height = height * ratio;
  ctx.setTransform(ratio, 0, 0, ratio, 0, 0);
}).observe(canvas);

function draw(timestamp) {
  requestAnimationFrame(draw);
  if (document.hidden || timestamp - lastFrame < (reducedMotion.matches ? 250 : 32)) return;
  lastFrame = timestamp;
  const t = reducedMotion.matches ? 0 : timestamp / 1000;
  if (current?.analyser && !muted) {
    current.analyser.getByteTimeDomainData(current.samples);
    const rms = Math.sqrt(current.samples.reduce((sum, sample) => sum + ((sample - 128) / 128) ** 2, 0) / current.samples.length);
    activity += (Math.min(1, rms * 7) - activity) * .2;
  } else activity *= .9;
  const energy = Math.max(activity, assistantSpeaking ? .5 + Math.sin(t * 9) * .18 : preview ? .4 + Math.sin(t * 3) * .2 : 0);
  ctx.clearRect(0, 0, width, height);
  const scale = Math.min(width * .31, height * .32);
  const centerX = width / 2;
  const centerY = height * .43;
  const rotation = -.12 + Math.sin(t * .4) * .14;
  const cos = Math.cos(rotation), sin = Math.sin(rotation);
  const baseY = centerY + scale * 1.15;
  const glow = ctx.createRadialGradient(centerX, centerY, 5, centerX, centerY, scale * 1.5);
  glow.addColorStop(0, `rgba(52,220,206,${.04 + energy * .035})`);
  glow.addColorStop(1, 'rgba(52,220,206,0)');
  ctx.fillStyle = glow;
  ctx.fillRect(0, 0, width, height);
  ctx.save();
  ctx.strokeStyle = '#73e7d2';
  for (let i = 0; i < 5; i++) {
    ctx.globalAlpha = .12 + i * .025;
    ctx.lineWidth = i === 3 ? 1.5 : .7;
    ctx.beginPath();
    ctx.ellipse(centerX, baseY + i * 3, scale * (1.02 + i * .07), scale * (.13 + i * .015), 0, 0, Math.PI * 2);
    ctx.stroke();
  }
  // Projection rays rise from the base; brighter during speech.
  const beam = ctx.createLinearGradient(0, centerY - scale, 0, baseY);
  beam.addColorStop(0, 'rgba(81,241,215,0)');
  beam.addColorStop(1, `rgba(81,241,215,${.035 + energy * .025})`);
  ctx.fillStyle = beam;
  ctx.globalAlpha = 1;
  ctx.beginPath();
  ctx.moveTo(centerX - scale * .65, centerY - scale);
  ctx.lineTo(centerX + scale * .65, centerY - scale);
  ctx.lineTo(centerX + scale * 1.1, baseY);
  ctx.lineTo(centerX - scale * 1.1, baseY);
  ctx.fill();
  for (const point of points) {
    const x = point.x * cos + point.z * sin;
    const z = -point.x * sin + point.z * cos;
    const depth = 3 / (3 + z);
    const flicker = .75 + Math.sin(point.seed * 3 + t * 1.4) * .25;
    const scan = Math.max(0, 1 - Math.abs(point.y - ((t * .3) % 2.4 - 1.3)) * 12);
    ctx.globalAlpha = Math.min(1, (z < 0 ? .64 : .15) * flicker + point.glow * .7 + scan * .25 + energy * .09);
    ctx.fillStyle = point.glow > .45 ? '#c3fff1' : '#6cebd8';
    const jitter = energy * Math.sin(point.seed + t * 7) * .005;
    const size = (point.glow > .45 ? 1.45 : .85) * depth;
    ctx.fillRect(centerX + (x + jitter) * scale * depth, centerY + (point.y + Math.sin(t * .8) * .018) * scale * depth, size, size);
  }
  // A sparse field gives the projection depth without obscuring the face.
  ctx.fillStyle = '#7debd9';
  for (let i = 0; i < 55; i++) {
    const x = centerX + Math.sin(i * 127.1) * scale * 1.5;
    const y = centerY + (((i * .791 - t * .06) % 2.7 + 2.7) % 2.7 - 1.5) * scale;
    ctx.globalAlpha = .08 + (Math.sin(i + t) + 1) * .1;
    ctx.fillRect(x, y, 1, 1);
  }
  ctx.restore();
}
requestAnimationFrame(draw);
