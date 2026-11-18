import * as THREE from 'three';
import { EffectComposer } from 'three/addons/postprocessing/EffectComposer.js';
import { RenderPass } from 'three/addons/postprocessing/RenderPass.js';
import { UnrealBloomPass } from 'three/addons/postprocessing/UnrealBloomPass.js';
import { OutputPass } from 'three/addons/postprocessing/OutputPass.js';

const canvas = document.getElementById('hologram');
const container = document.getElementById('scene');
const reducedMotion = matchMedia('(prefers-reduced-motion: reduce)');
let renderer;
try {
  renderer = new THREE.WebGLRenderer({ canvas, alpha: true, antialias: true, powerPreference: 'high-performance' });
} catch {
  container.classList.add('render-fallback');
  document.getElementById('holo-fallback-image').hidden = false;
  document.getElementById('render-error').hidden = false;
  document.getElementById('render-error').textContent = '当前设备使用静态角色投影，语音通话仍可使用。';
}

if (renderer) {
  renderer.setPixelRatio(Math.min(devicePixelRatio, 2));
  renderer.setClearColor(0x030910, 0);
  renderer.toneMapping = THREE.NoToneMapping;
  const scene = new THREE.Scene();
  scene.fog = new THREE.FogExp2(0x030910, .055);
  const camera = new THREE.PerspectiveCamera(37, 1, .1, 80);
  camera.position.set(0, 1.35, 8.5);
  camera.lookAt(0, 1.02, 0);
  const composer = new EffectComposer(renderer);
  composer.addPass(new RenderPass(scene, camera));
  const bloom = new UnrealBloomPass(new THREE.Vector2(1, 1), .22, .42, .92);
  composer.addPass(bloom);
  composer.addPass(new OutputPass());

  const figure = new THREE.Group();
  figure.position.y = 1.10;
  scene.add(figure);
  const core = new THREE.Mesh(new THREE.IcosahedronGeometry(.58, 1), new THREE.MeshBasicMaterial({ color: 0xb8a5ff, wireframe: true, transparent: true, opacity: .6 }));
  core.position.y = .9;
  scene.add(core);
  let ready = false;
  let revealStarted = null;
  let texture = null;
  let disposed = false;
  let contextLost = false;
  const uniforms = {
    uImage: { value: null }, uTexel: { value: new THREE.Vector2(1 / 1024, 1 / 1536) },
    uTime: { value: 0 }, uEnergy: { value: 0 }, uReveal: { value: 0 },
    uStrength: { value: .6 }, uMotion: { value: 1 },
    uAccent: { value: new THREE.Color('#b5a0ff') },
  };
  // The reference is an illustrated cutout, not a reconstructed or rigged 3D mesh.
  // Shader layers add depth, edge light, scanning, and slight cloth/hair movement.
  const vertexShader = `
    uniform float uTime, uMotion;
    varying vec2 vUv;
    void main() {
      vUv = uv;
      vec3 p = position;
      float edges = pow(abs(uv.x - .5) * 2., 2.);
      float drape = 1. - smoothstep(.15, .8, uv.y);
      float wings = smoothstep(.20, .38, abs(uv.x - .5))
        * smoothstep(.48, .65, uv.y) * (1. - smoothstep(.77, .87, uv.y));
      p.x += (sin(uTime * .72 + uv.y * 5.) * edges * drape * .018
        + sin(uTime * .9) * wings * sign(uv.x - .5) * .008) * uMotion;
      p.z += sin(uTime * .9 + uv.x * 2.) * wings * .018 * uMotion;
      p.z += (1. - edges) * .025;
      gl_Position = projectionMatrix * modelViewMatrix * vec4(p, 1.);
    }`;
  const material = new THREE.ShaderMaterial({
    uniforms, vertexShader, transparent: true, depthWrite: false,
    fragmentShader: `
      uniform sampler2D uImage;
      uniform float uTime, uEnergy, uReveal, uStrength;
      uniform vec3 uAccent;
      varying vec2 vUv;
      void main() {
        vec4 art = texture2D(uImage, vUv);
        if (art.a < .005) discard;
        float reveal = smoothstep(0., .055, uReveal - vUv.y);
        float lines = smoothstep(.85, .99, sin(vUv.y * 1100. - uTime * .4));
        float band = exp(-pow((vUv.y - fract(uTime * .065)) * 45., 2.));
        vec3 color = art.rgb;
        // Preserve the face, silver-lilac hair, dress, and violet crystal details.
        color = mix(color, color * vec3(.90, .97, 1.12), uStrength * .32);
        float body = 1. - smoothstep(.72, .88, vUv.y);
        color *= 1. - lines * uStrength * .018 * body;
        color += uAccent * (band * .035 + uEnergy * .012) * uStrength * body;
        // The formation edge fades out completely once the full figure is present.
        color += uAccent * exp(-pow((vUv.y - uReveal + .025) * 70., 2.)) * .2;
        gl_FragColor = vec4(color, art.a * reveal * (1. - uStrength * .06));
        #include <tonemapping_fragment>
        #include <colorspace_fragment>
      }`,
  });
  const haloMaterial = new THREE.ShaderMaterial({
    uniforms, vertexShader, transparent: true, depthWrite: false, blending: THREE.AdditiveBlending,
    fragmentShader: `
      uniform sampler2D uImage;
      uniform vec2 uTexel;
      uniform float uEnergy, uReveal, uStrength;
      uniform vec3 uAccent;
      varying vec2 vUv;
      void main() {
        float center = texture2D(uImage, vUv).a;
        vec2 d = uTexel * 3.;
        float nearby = max(max(texture2D(uImage, vUv + vec2(d.x, 0.)).a,
          texture2D(uImage, vUv - vec2(d.x, 0.)).a),
          max(texture2D(uImage, vUv + vec2(0., d.y)).a,
          texture2D(uImage, vUv - vec2(0., d.y)).a));
        float edge = max(0., nearby - center);
        float reveal = smoothstep(0., .055, uReveal - vUv.y);
        gl_FragColor = vec4(uAccent, edge * reveal * (.28 + uEnergy * .45) * uStrength);
        #include <tonemapping_fragment>
        #include <colorspace_fragment>
      }`,
  });
  const artGeometry = new THREE.PlaneGeometry(2.9, 4.35, 32, 64);
  const art = new THREE.Mesh(artGeometry, material);
  art.renderOrder = 3;
  const silhouette = new THREE.Mesh(artGeometry, haloMaterial);
  silhouette.position.set(.006, 0, -.018);
  silhouette.renderOrder = 2;
  figure.add(silhouette, art);
  figure.visible = false;
  container.dataset.detail = 'loading';
  new THREE.TextureLoader().load('/assets/silverwing-avatar.png', (image) => {
    if (disposed) { image.dispose(); return; }
    texture = image;
    image.colorSpace = THREE.SRGBColorSpace;
    image.anisotropy = Math.min(8, renderer.capabilities.getMaxAnisotropy());
    uniforms.uImage.value = image;
    uniforms.uTexel.value.set(1 / image.image.width, 1 / image.image.height);
    revealStarted = performance.now();
    ready = true;
    container.dataset.detail = 'reference-hologram';
  }, undefined, () => {
    if (disposed) return;
    container.dataset.detail = 'abstract';
    document.getElementById('render-error').hidden = false;
    document.getElementById('render-error').textContent = '角色素材暂时无法加载，已显示无面孔核心。语音通话仍可使用。';
  });

  function arc(radius, start, length, color, opacity, vertical = false) {
    const points = Array.from({ length: 181 }, (_, i) => {
      const a = start + length * i / 180;
      return new THREE.Vector3(Math.cos(a) * radius, 0, Math.sin(a) * radius);
    });
    const line = new THREE.Line(new THREE.BufferGeometry().setFromPoints(points), new THREE.LineBasicMaterial({ color, transparent: true, opacity, blending: THREE.AdditiveBlending, depthWrite: false }));
    if (vertical) line.rotation.x = Math.PI / 2;
    return line;
  }
  const platform = new THREE.Group();
  platform.position.y = -1.13;
  scene.add(platform);
  const flatVertex = 'varying vec2 vUv; void main(){vUv=uv;gl_Position=projectionMatrix*modelViewMatrix*vec4(position,1.);}';
  const floorGlow = new THREE.Mesh(new THREE.PlaneGeometry(5.4, 5.4), new THREE.ShaderMaterial({
    uniforms, vertexShader: flatVertex, transparent: true, depthWrite: false,
    blending: THREE.AdditiveBlending,
    fragmentShader: `
      uniform vec3 uAccent;
      uniform float uEnergy, uStrength;
      varying vec2 vUv;
      void main() {
        float r = length(vUv - .5) * 2.;
        float light = exp(-r * r * 9.) * .12 + exp(-pow((r - .66) * 38., 2.)) * .12;
        float alpha = light * (1. - smoothstep(.75, 1., r));
        gl_FragColor = vec4(uAccent, alpha * (.45 + uStrength * .55 + uEnergy * .3));
      }`,
  }));
  floorGlow.rotation.x = -Math.PI / 2;
  floorGlow.position.y = -.06;
  platform.add(floorGlow);

  // A faint reflection lies on the emitter plane, not a second upright character.
  const reflection = new THREE.Mesh(new THREE.PlaneGeometry(2.9, 1.55), new THREE.ShaderMaterial({
    uniforms, vertexShader: flatVertex, transparent: true, depthWrite: false, side: THREE.DoubleSide,
    fragmentShader: `
      uniform sampler2D uImage;
      uniform float uReveal;
      varying vec2 vUv;
      void main() {
        vec4 art = texture2D(uImage, vUv);
        float fade = pow(1. - vUv.y, 2.6) * .11;
        gl_FragColor = vec4(art.rgb * vec3(.7, .8, 1.), art.a * fade * smoothstep(0., .055, uReveal - vUv.y));
        #include <colorspace_fragment>
      }`,
  }));
  reflection.rotation.x = Math.PI / 2;
  reflection.position.set(0, .012, .7);
  platform.add(reflection);
  const rings = [];
  for (let i = 0; i < 4; i++) {
    const ring = arc(1.52 + i * .21, i * .8, Math.PI * (i % 2 ? 1.5 : 1.97), i === 2 ? 0xcebfff : 0x7a8dd9, i === 2 ? .55 : .22);
    ring.position.y = i * -.018;
    platform.add(ring);
    rings.push(ring);
  }
  // Short, finite-width light segments remain crisp at laptop resolutions.
  const emitter = new THREE.Group();
  for (let i = 0; i < 12; i++) {
    const segment = new THREE.Mesh(new THREE.RingGeometry(1.76, 1.78, 32, 1, i * Math.PI / 6, Math.PI / 7),
      new THREE.MeshBasicMaterial({ color: i % 3 ? 0x938be1 : 0xa7e4f4, transparent: true,
        opacity: i % 3 ? .38 : .72, side: THREE.DoubleSide, blending: THREE.AdditiveBlending, depthWrite: false }));
    segment.rotation.x = -Math.PI / 2;
    emitter.add(segment);
  }
  emitter.position.y = -.025;
  platform.add(emitter);
  const lowerRing = arc(2.18, 0, Math.PI * 2, 0x6e72af, .16);
  lowerRing.position.y = -.13;
  platform.add(lowerRing);
  const ticks = [];
  for (let i = 0; i < 96; i++) {
    const angle = i / 96 * Math.PI * 2;
    const r = i % 8 === 0 ? 2.48 : 2.42;
    ticks.push(new THREE.Vector3(Math.cos(angle) * 2.37, 0, Math.sin(angle) * 2.37), new THREE.Vector3(Math.cos(angle) * r, 0, Math.sin(angle) * r));
  }
  platform.add(new THREE.LineSegments(new THREE.BufferGeometry().setFromPoints(ticks), new THREE.LineBasicMaterial({ color: 0xa69ce7, transparent: true, opacity: .3 })));
  const voiceBars = [];
  const barGeometry = new THREE.BoxGeometry(.016, 1, .016);
  const barMaterial = new THREE.MeshBasicMaterial({ color: 0xb7c9ff, transparent: true, opacity: .35, blending: THREE.AdditiveBlending, depthWrite: false });
  for (let i = 0; i < 64; i++) {
    const angle = i / 64 * Math.PI * 2;
    const bar = new THREE.Mesh(barGeometry, barMaterial);
    bar.position.set(Math.cos(angle) * 1.82, 0, Math.sin(angle) * 1.82);
    platform.add(bar);
    voiceBars.push(bar);
  }
  const halo = new THREE.Group();
  halo.position.set(0, .85, -.65);
  halo.add(arc(2.05, .4, Math.PI * 1.4, 0x7876bc, .13, true));
  halo.add(arc(2.16, -.6, Math.PI * .9, 0xb2a7ff, .22, true));
  const haloTicks = [];
  for (let i = 0; i < 72; i++) {
    const angle = i / 72 * Math.PI * 2;
    const r = i % 6 === 0 ? 2.25 : 2.21;
    haloTicks.push(new THREE.Vector3(Math.cos(angle) * 2.19, Math.sin(angle) * 2.19, 0),
      new THREE.Vector3(Math.cos(angle) * r, Math.sin(angle) * r, 0));
  }
  halo.add(new THREE.LineSegments(new THREE.BufferGeometry().setFromPoints(haloTicks),
    new THREE.LineBasicMaterial({ color: 0x8d93cb, transparent: true, opacity: .18, depthWrite: false })));
  scene.add(halo);
  const grid = new THREE.GridHelper(30, 60, 0x2a355c, 0x14283a);
  grid.position.y = -1.28;
  grid.material.transparent = true;
  grid.material.opacity = .10;
  scene.add(grid);

  const beam = new THREE.Mesh(new THREE.CylinderGeometry(1.2, 1.65, 2.5, 64, 1, true), new THREE.ShaderMaterial({
    uniforms,
    transparent: true, depthWrite: false, side: THREE.DoubleSide, blending: THREE.AdditiveBlending,
    vertexShader: flatVertex,
    fragmentShader: `uniform vec3 uAccent; uniform float uStrength, uEnergy; varying vec2 vUv;
      void main(){float ray=pow(max(0.,sin(vUv.x*100.)),32.);
      gl_FragColor=vec4(uAccent,pow(1.-vUv.y,3.)*(.006+ray*.016)*(.4+uStrength*.6+uEnergy*.3));}`,
  }));
  beam.position.y = .12;
  beam.renderOrder = 1;
  scene.add(beam);
  const dustPoints = Array.from({ length: 110 }, (_, i) => new THREE.Vector3(Math.sin(i * 127.1) * 2.7, (i * .761 % 4.7) - 1.2, Math.cos(i * 33.3) * .7));
  const dust = new THREE.Points(new THREE.BufferGeometry().setFromPoints(dustPoints), new THREE.ShaderMaterial({
    uniforms, transparent: true, depthWrite: false, blending: THREE.AdditiveBlending,
    vertexShader: `uniform float uTime; varying float vLight;
      void main(){vec3 p=position; p.y+=sin(uTime*.35+position.x*6.)*.025;
      vLight=.35+.25*sin(uTime*.65+position.y*7.);
      vec4 mv=modelViewMatrix*vec4(p,1.); gl_PointSize=clamp(17./-mv.z,1.,3.);
      gl_Position=projectionMatrix*mv;}`,
    fragmentShader: `uniform vec3 uAccent; varying float vLight;
      void main(){float r=length(gl_PointCoord-.5)*2.;
      gl_FragColor=vec4(uAccent,(1.-smoothstep(.1,1.,r))*vLight);}`,
  }));
  scene.add(dust);

  const resizeObserver = new ResizeObserver(([entry]) => {
    const { width, height } = entry.contentRect;
    if (!width || !height) return;
    camera.aspect = width / height;
    // Fit the whole character and platform even in a narrow, tall window.
    camera.position.z = Math.max(8.5, 2.65 / (Math.tan(THREE.MathUtils.degToRad(37 / 2)) * camera.aspect));
    camera.lookAt(0, 1.02, 0);
    camera.updateProjectionMatrix();
    renderer.setSize(width, height, false);
    composer.setSize(width, height);
  });
  resizeObserver.observe(container);
  const pointer = { x: 0, y: 0 };
  canvas.addEventListener('pointermove', (event) => {
    const rect = canvas.getBoundingClientRect();
    pointer.x = (event.clientX - rect.left) / rect.width - .5;
    pointer.y = (event.clientY - rect.top) / rect.height - .5;
  });
  canvas.addEventListener('pointerleave', () => { pointer.x = pointer.y = 0; });
  canvas.addEventListener('webglcontextlost', (event) => {
    event.preventDefault();
    contextLost = true;
    container.classList.add('render-fallback');
    document.getElementById('holo-fallback-image').hidden = false;
    document.getElementById('render-error').hidden = false;
    document.getElementById('render-error').textContent = '投影渲染暂时中断，正在显示静态角色。语音通话仍可使用。';
  });
  canvas.addEventListener('webglcontextrestored', () => {
    contextLost = false;
    container.classList.remove('render-fallback');
    document.getElementById('holo-fallback-image').hidden = true;
    document.getElementById('render-error').hidden = ready;
  });
  window.addEventListener('pagehide', () => {
    disposed = true;
    cancelAnimationFrame(animationFrame);
    resizeObserver.disconnect();
    scene.traverse((object) => {
      object.geometry?.dispose();
      if (object.material) object.material.dispose();
    });
    texture?.dispose();
    composer.dispose();
    renderer.dispose();
  }, { once: true });

  let previous = 0, elapsed = 0, energy = 0, previousMode = 'idle';
  const violet = new THREE.Color('#b6a0ff');
  const blue = new THREE.Color('#8edbff');
  const thought = new THREE.Color('#d6afff');
  const mutedColor = new THREE.Color('#7a88a7');
  let animationFrame;
  function frame(timestamp) {
    if (disposed) return;
    animationFrame = requestAnimationFrame(frame);
    const visual = window.holoState || { mode: 'idle', mic: 0, output: 0 };
    const reduced = document.body.dataset.motion === 'reduced' || (document.body.dataset.motion !== 'full' && reducedMotion.matches);
    if (document.hidden || contextLost) { previous = timestamp; return; }
    if (timestamp - previous < (reduced ? 150 : 32)) return;
    const dt = Math.min((timestamp - previous) / 1000, .1);
    previous = timestamp;
    if (!reduced) elapsed += dt;
    if (visual.mode === 'preview' && previousMode !== 'preview') revealStarted = timestamp;
    previousMode = visual.mode;
    const demo = visual.mode === 'preview' && !reduced ? .22 + Math.sin(elapsed * 3) ** 2 * .25 : 0;
    energy += (Math.max(visual.output || 0, demo) - energy) * .18;
    const level = Math.max(energy, visual.mic || 0);
    uniforms.uTime.value = elapsed;
    uniforms.uEnergy.value = energy;
    uniforms.uMotion.value = reduced ? 0 : 1;
    uniforms.uStrength.value = THREE.MathUtils.clamp(visual.strength ?? .6, 0, 1);
    uniforms.uReveal.value = reduced ? 1.06 : revealStarted === null ? 0 : Math.min(1.06, (timestamp - revealStarted) / 1500);
    uniforms.uAccent.value.lerp(visual.mode === 'thinking' ? thought : visual.mode === 'listening' ? blue : visual.mode === 'muted' ? mutedColor : violet, .08);
    figure.visible = ready && visual.avatar !== 'core';
    reflection.visible = figure.visible;
    core.visible = !ready || visual.avatar === 'core';
    core.rotation.y = elapsed * .18;
    core.scale.setScalar(1 + level * .15);
    // Shallow parallax preserves the reference silhouette; this is not orbiting a 3D model.
    figure.rotation.y += ((reduced ? 0 : pointer.x * .09) - figure.rotation.y) * .04;
    figure.position.x += ((reduced ? 0 : pointer.x * .035) - figure.position.x) * .04;
    figure.position.y = 1.10 + (reduced ? 0 : Math.sin(elapsed * .8) * .012);
    reflection.position.x = figure.position.x;
    rings.forEach((ring, i) => { ring.rotation.y = elapsed * (i % 2 ? -.07 : .09) + i; });
    emitter.rotation.y = -elapsed * .035;
    voiceBars.forEach((bar, i) => {
      const band = visual.spectrum?.[i % 32] || 0;
      const amplitude = demo ? demo * (.55 + Math.sin(i * .55 + elapsed * 2) * .3) : band;
      const height = .014 + (reduced ? 0 : amplitude) * .28;
      bar.scale.y += (height - bar.scale.y) * .25;
      bar.position.y = bar.scale.y / 2;
    });
    barMaterial.color.copy(uniforms.uAccent.value);
    barMaterial.opacity = .22 + level * .36;
    halo.rotation.z = reduced ? 0 : Math.sin(elapsed * .12) * .05;
    dust.rotation.y = elapsed * .012;
    bloom.strength = .16 + uniforms.uStrength.value * .10 + energy * .08;
    composer.render();
  }
  animationFrame = requestAnimationFrame(frame);
}
