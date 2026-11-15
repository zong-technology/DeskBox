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
const visual = window.holoState = { mode: 'idle', mic: 0, output: 0, avatar: 'dragon', strength: .6, spectrum: new Float32Array(32) };

function setMode(mode) {
  visual.mode = mode;
  document.body.dataset.mode = mode;
  const labels = {
    idle: ['AWAITING CONNECTION', '待命 · AWAITING LINK'],
    connecting: ['ESTABLISHING LINK', '连接中 · CONNECTING'],
    listening: ['LISTENING TO YOU', '倾听 · LISTENING'],
    thinking: ['PROCESSING THOUGHT', '思考 · PROCESSING'],
    speaking: ['VOICE RESPONSE ACTIVE', '回应 · SPEAKING'],
    muted: ['MICROPHONE MUTED', '静音 · MUTED'],
    preview: ['VISUAL PREVIEW ONLY', '演示 · VISUAL ONLY'],
  };
  $('mode-label').textContent = labels[mode][0];
  $('core-state').textContent = labels[mode][1];
}

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
  callButton.querySelector('span').textContent = connecting ? '取消连接' : connected ? '结束通话' : '建立语音连接';
  muteButton.disabled = !connected;
  $('voice').disabled = $('language').disabled = connected || connecting;
  previewButton.disabled = connected || connecting;
  $('live-badge').classList.toggle('active', connected || preview);
  $('live-badge').innerHTML = `<i></i> ${connecting ? '连接中' : connected ? '通话中' : preview ? '视觉预览' : '系统待命'}`;
  $('call-label').textContent = connecting ? '正在连接 NOVA…' : connected ? '已连接 · 00:00' : '准备建立连接';
  $('call-detail').textContent = connected ? '自然交流 · 随时打断' : connecting ? '建立安全语音链路' : '实时语音 · 自然打断';
  $('stage-status').textContent = connecting ? '正在与你建立连接…' : connected ? '我在听，请说吧。' : preview ? '全息视觉预览 · 未连接语音' : '我在这里，等待与你连接。';
  $('transcript-indicator').textContent = connected ? 'LIVE TRANSCRIPT' : connecting ? 'CONNECTING' : 'STANDBY';
  $('link-state').textContent = connected ? '已连接 / LIVE' : connecting ? '连接中' : '未连接';
  $('mic-state').textContent = connected ? '已开启' : '未开启';
  $('audio-state').textContent = connected ? '正在接收音频' : preview ? '视觉演示 · 无音频输入' : '等待音频输入';
  setMode(connecting ? 'connecting' : connected ? 'listening' : preview ? 'preview' : 'idle');
}

function stopCall() {
  const session = current;
  current = null;
  if (session) {
    clearTimeout(session.timeout);
    clearTimeout(session.disconnectTimeout);
    clearInterval(session.timer);
    clearInterval(session.statsTimer);
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
  visual.mic = visual.output = 0;
  visual.spectrum.fill(0);
  $('latency').innerHTML = '— <small>ms</small>';
  $('input-level').innerHTML = '— <small>dB</small>';
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
  if (event.type === 'input_audio_buffer.speech_started') {
    session.userSpeaking = true;
    assistantSpeaking = false;
    setMode('listening');
    $('stage-status').textContent = '正在倾听你的声音…';
  }
  if (event.type === 'input_audio_buffer.speech_stopped') {
    session.userSpeaking = false;
    setMode('thinking');
    $('stage-status').textContent = '正在思考…';
  }
  if (event.type === 'output_audio_buffer.started') {
    assistantSpeaking = true;
    setMode('speaking');
    $('stage-status').textContent = 'NOVA 正在回应';
  }
  if (['output_audio_buffer.stopped', 'output_audio_buffer.cleared'].includes(event.type)) {
    assistantSpeaking = false;
    setMode(muted ? 'muted' : 'listening');
    $('stage-status').textContent = muted ? '麦克风已静音' : session.userSpeaking ? '正在倾听你的声音…' : '我在听，请说吧。';
  }
  if (event.type === 'conversation.item.input_audio_transcription.failed') notice('这句话的字幕转写失败，你仍可以继续通话。', true);
  if (event.type === 'response.done' && event.response?.status === 'failed') fail(session, 'AI 回应失败，请检查语音服务额度后重试。');
  if (event.type === 'error') fail(session, '语音会话出现错误，请重新发起通话。');
}

async function startCall() {
  if (!configured) {
    notice('语音服务尚未就绪，可在设置中配置服务，或点击「启动全息演示」体验投影。', true);
    if (window.deskbox) $('settings-dialog').showModal();
    return;
  }
  if (!window.isSecureContext || !navigator.mediaDevices?.getUserMedia || !window.RTCPeerConnection) {
    notice('当前浏览器不支持语音通话，请使用 HTTPS 或 localhost，并使用新版 Chrome、Edge 或 Safari。', true);
    return;
  }
  preview = false;
  previewButton.setAttribute('aria-pressed', 'false');
  previewLabel();
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
      if (session.context) {
        session.outputAnalyser = session.context.createAnalyser();
        session.outputAnalyser.fftSize = 256;
        session.outputSamples = new Uint8Array(session.outputAnalyser.frequencyBinCount);
        session.outputBands = new Uint8Array(session.outputAnalyser.frequencyBinCount);
        session.context.createMediaStreamSource(audio.srcObject).connect(session.outputAnalyser);
      }
      audio.play().catch(() => { if (session === current) notice('浏览器暂停了声音播放，点击页面即可恢复。', true); });
    };
    peer.onconnectionstatechange = () => {
      if (session !== current) return;
      if (peer.connectionState === 'connected') {
        const recovered = Boolean(session.disconnectTimeout);
        clearTimeout(session.timeout);
        clearTimeout(session.disconnectTimeout);
        session.disconnectTimeout = null;
        $('link-state').textContent = '已连接 / LIVE';
        if (recovered) notice('语音连接已恢复。');
        if (state !== 'connected') {
          session.started = Date.now();
          setState('connected');
          notice('通话已连接 · 再次点击麦克风按钮可静音');
          session.timer = setInterval(() => {
            const elapsed = Math.floor((Date.now() - session.started) / 1000);
            $('call-label').textContent = `已连接 · ${String(Math.floor(elapsed / 60)).padStart(2, '0')}:${String(elapsed % 60).padStart(2, '0')}`;
          }, 1000);
          session.statsTimer = setInterval(async () => {
            try {
              const stats = await peer.getStats();
              if (session !== current) return;
              let selected;
              stats.forEach((item) => { if (item.type === 'transport' && item.selectedCandidatePairId) selected = stats.get(item.selectedCandidatePairId); });
              if (selected?.currentRoundTripTime !== undefined) $('latency').innerHTML = `${Math.round(selected.currentRoundTripTime * 1000)} <small>ms</small>`;
            } catch { /* Stats may be unavailable while the connection recovers. */ }
          }, 2000);
        }
      }
      if (peer.connectionState === 'failed' || peer.connectionState === 'closed') fail(session, '语音连接已断开，请重新发起通话。');
      if (peer.connectionState === 'disconnected') {
        $('link-state').textContent = '正在恢复';
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
      session.bands = new Uint8Array(analyser.frequencyBinCount);
      context.createMediaStreamSource(stream).connect(analyser);
      // Browser autoplay rules may keep this promise pending until another click.
      // Optional visualization must never block the actual voice connection.
      context.resume().catch(() => {});
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
  $('mic-state').textContent = muted ? '已静音' : '已开启';
  $('audio-state').textContent = muted ? '麦克风已静音' : '正在接收音频';
  if (!assistantSpeaking) setMode(muted ? 'muted' : 'listening');
});
function previewLabel() {
  previewButton.innerHTML = `<svg><use href="#spark"/></svg><span>${preview ? '结束全息演示' : '启动全息演示'}<small>VISUAL PREVIEW · 无需麦克风</small></span><b>↗</b>`;
}
previewButton.addEventListener('click', () => {
  preview = !preview;
  previewButton.setAttribute('aria-pressed', String(preview));
  previewLabel();
  setState('idle');
  notice(preview ? '正在预览全息动态效果 · 未开启麦克风，未连接 AI' : '麦克风仅在你发起通话后开启');
});
document.addEventListener('click', () => {
  current?.context?.resume().catch(() => {});
  if (state === 'connected' && audio.paused && audio.srcObject) audio.play().then(() => notice('声音播放已恢复。')).catch(() => {});
});
window.addEventListener('pagehide', stopCall);
function updateService(ready) {
  configured = ready;
  $('service-status').classList.toggle('ready', configured);
  $('service-status').innerHTML = `<i></i>${configured ? '语音服务就绪' : '语音服务待配置'}`;
}
fetch('/api/status').then((response) => {
  if (!response.ok) throw new Error('status unavailable');
  return response.json();
}).then((status) => {
  updateService(status.configured === true);
}).catch(() => { $('service-status').innerHTML = '<i></i>语音服务不可用'; });

for (const id of ['open-settings', 'console-settings']) $(id).addEventListener('click', () => $('settings-dialog').showModal());
for (const id of ['close-settings', 'done-settings']) $(id).addEventListener('click', () => $('settings-dialog').close());
$('volume').addEventListener('input', () => { audio.volume = Number($('volume').value); });
$('avatar-style').addEventListener('change', () => {
  visual.avatar = $('avatar-style').value;
  document.body.dataset.avatar = visual.avatar;
});
$('holo-strength').addEventListener('input', () => { visual.strength = Number($('holo-strength').value); });
$('motion').addEventListener('change', () => { document.body.dataset.motion = $('motion').value; });
function focusView(active) {
  document.body.dataset.focus = String(active);
  $('focus-view').setAttribute('aria-pressed', String(active));
  $('focus-view').setAttribute('aria-label', active ? '退出专注投影' : '专注投影');
  $('focus-view').querySelector('span').textContent = active ? '退出专注' : '专注投影';
}
$('focus-view').addEventListener('click', () => focusView(document.body.dataset.focus !== 'true'));
document.addEventListener('keydown', (event) => {
  if (event.key === 'Escape' && !$('settings-dialog').open && document.body.dataset.focus === 'true') focusView(false);
});
$('fullscreen').addEventListener('click', async () => {
  try {
    if (document.fullscreenElement) await document.exitFullscreen();
    else await document.documentElement.requestFullscreen();
  } catch { notice('当前窗口无法进入全屏，可尝试最大化窗口。', true); }
});
document.addEventListener('fullscreenchange', () => $('fullscreen').setAttribute('aria-label', document.fullscreenElement ? '退出全屏' : '进入全屏'));
if (window.deskbox) {
  $('desktop-key-settings').hidden = false;
  $('web-key-notice').hidden = true;
  $('save-key').addEventListener('click', async () => {
    const key = $('api-key').value.trim();
    if (!key) { $('key-notice').textContent = '请输入有效的 API 密钥。'; return; }
    $('save-key').disabled = true;
    try {
      const result = await window.deskbox.saveApiKey(key);
      $('api-key').value = '';
      updateService(result.configured);
      $('key-notice').textContent = result.persisted ? '已加密保存到此设备，密钥有效性将在通话时验证。' : '密钥仅在本次运行中使用：系统安全存储当前不可用。';
      notice('服务密钥已配置，可以建立语音连接。');
    } catch { $('key-notice').textContent = '保存失败，请检查密钥格式和系统存储权限。'; }
    finally { $('save-key').disabled = false; }
  });
}

const waveform = $('waveform');
const waveContext = waveform.getContext('2d');
let waveWidth = 200, waveHeight = 55, previousFrame = 0;
new ResizeObserver(([entry]) => {
  waveWidth = entry.contentRect.width;
  waveHeight = entry.contentRect.height;
  const ratio = Math.min(devicePixelRatio || 1, 2);
  waveform.width = waveWidth * ratio;
  waveform.height = waveHeight * ratio;
  waveContext.setTransform(ratio, 0, 0, ratio, 0, 0);
}).observe(waveform);
function sample(analyser, values) {
  if (!analyser) return 0;
  analyser.getByteTimeDomainData(values);
  return Math.sqrt(values.reduce((sum, value) => sum + ((value - 128) / 128) ** 2, 0) / values.length);
}
function updateAudio(timestamp) {
  requestAnimationFrame(updateAudio);
  if (document.hidden || timestamp - previousFrame < 40) return;
  previousFrame = timestamp;
  const rms = current && !muted ? sample(current.analyser, current.samples) : 0;
  activity += (Math.min(1, rms * 8) - activity) * .25;
  visual.mic = activity;
  visual.output = current ? Math.min(1, sample(current.outputAnalyser, current.outputSamples) * 6) : 0;
  const analyser = assistantSpeaking ? current?.outputAnalyser : !muted ? current?.analyser : null;
  const bands = assistantSpeaking ? current?.outputBands : current?.bands;
  if (analyser && bands) analyser.getByteFrequencyData(bands);
  for (let i = 0; i < visual.spectrum.length; i++) {
    // Log-spaced bands from the current speaker, shared with the emitter ring.
    const bin = Math.min((bands?.length || 1) - 1, Math.round(1.13 ** i));
    const target = analyser && bands ? bands[bin] / 255 : 0;
    visual.spectrum[i] += (target - visual.spectrum[i]) * .3;
  }
  if (state === 'connected') $('input-level').innerHTML = `${rms > .001 ? Math.round(20 * Math.log10(rms)) : '−∞'} <small>dB</small>`;
  waveContext.clearRect(0, 0, waveWidth, waveHeight);
  waveContext.strokeStyle = '#1c3a4b';
  waveContext.lineWidth = 1;
  for (let i = 1; i < 4; i++) {
    waveContext.beginPath(); waveContext.moveTo(0, waveHeight * i / 4); waveContext.lineTo(waveWidth, waveHeight * i / 4); waveContext.stroke();
  }
  waveContext.beginPath();
  waveContext.strokeStyle = muted ? '#598095' : '#64d7ee';
  for (let i = 0; i < 100; i++) {
    const value = current?.samples && !muted ? (current.samples[Math.floor(i / 100 * current.samples.length)] - 128) / 128 : 0;
    const y = waveHeight / 2 + value * waveHeight * 1.8;
    if (i === 0) waveContext.moveTo(0, y); else waveContext.lineTo(i / 99 * waveWidth, y);
  }
  waveContext.stroke();
}
requestAnimationFrame(updateAudio);
