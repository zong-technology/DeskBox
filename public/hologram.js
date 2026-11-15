import * as THREE from 'three';
import { EffectComposer } from 'three/addons/postprocessing/EffectComposer.js';
import { RenderPass } from 'three/addons/postprocessing/RenderPass.js';
import { UnrealBloomPass } from 'three/addons/postprocessing/UnrealBloomPass.js';
import { OutputPass } from 'three/addons/postprocessing/OutputPass.js';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
import { MeshSurfaceSampler } from 'three/addons/math/MeshSurfaceSampler.js';

const canvas = document.getElementById('hologram');
const container = document.getElementById('scene');
const reducedMotion = matchMedia('(prefers-reduced-motion: reduce)');
let renderer;
try {
  renderer = new THREE.WebGLRenderer({ canvas, alpha: true, antialias: true, powerPreference: 'high-performance' });
} catch {
  container.classList.add('render-fallback');
  document.getElementById('render-error').hidden = false;
}

if (renderer) {
  renderer.setPixelRatio(Math.min(devicePixelRatio, 1.75));
  renderer.setClearColor(0x030910, 0);
  renderer.toneMapping = THREE.ACESFilmicToneMapping;
  const scene = new THREE.Scene();
  scene.fog = new THREE.FogExp2(0x030910, .055);
  const camera = new THREE.PerspectiveCamera(37, 1, .1, 80);
  camera.position.set(0, 1.2, 8.9);
  camera.lookAt(0, .6, 0);
  const composer = new EffectComposer(renderer);
  composer.addPass(new RenderPass(scene, camera));
  const bloom = new UnrealBloomPass(new THREE.Vector2(1, 1), .32, .5, .7);
  composer.addPass(bloom);
  composer.addPass(new OutputPass());
  const figure = new THREE.Group();
  figure.position.y = -.12;
  figure.rotation.y = -.35;
  figure.scale.x = .94;
  scene.add(figure);

  const positions = [], weights = [], seeds = [];
  const headRows = [];
  // Front-facing facial relief, tapered jaw, ears, neck, and shoulders.
  for (let row = 0; row < 125; row++) {
    const v = row / 124;
    const y = .55 + v * 2.05;
    const slice = Math.sin(Math.PI * v) ** .48;
    const jaw = v < .4 ? .67 + v * .825 : 1;
    const rowPoints = [];
    for (let col = 0; col < 155; col++) {
      const angle = col / 155 * Math.PI * 2;
      const x = Math.cos(angle) * .68 * slice * jaw;
      let z = Math.sin(angle) * .55 * slice;
      const front = Math.max(0, Math.sin(angle)) ** 8;
      const nose = Math.exp(-((x / .12) ** 2) - ((y - 1.4) / .25) ** 2) * .25;
      const eye = Math.exp(-(((Math.abs(x) - .255) / .12) ** 2) - ((y - 1.67) / .075) ** 2);
      const brow = Math.exp(-(((Math.abs(x) - .27) / .17) ** 2) - ((y - 1.79) / .06) ** 2);
      const lips = Math.exp(-((x / .22) ** 2) - ((y - 1.06) / .045) ** 2);
      z += front * (nose - eye * .09 + brow * .035 + lips * .035);
      positions.push(x, y, z);
      weights.push(eye * front, lips * front);
      seeds.push(row * 1.93 + col * .73);
      rowPoints.push(new THREE.Vector3(x, y, z));
    }
    headRows.push(rowPoints);
  }
  for (let row = 0; row < 52; row++) {
    const v = row / 51;
    const radius = .24 + Math.max(0, .76 - v) ** .65 * 1.6;
    for (let col = 0; col < 150; col++) {
      const a = col / 150 * Math.PI * 2;
      const shoulder = Math.cos(a) ** 2 * Math.max(0, .5 - v) * .2;
      positions.push(Math.cos(a) * radius, -.65 + v * 1.32 - shoulder, Math.sin(a) * (.34 + (1 - v) * .18));
      weights.push(0, 0);
      seeds.push(row * 2.13 + col);
    }
  }
  const geometry = new THREE.BufferGeometry();
  geometry.setAttribute('position', new THREE.Float32BufferAttribute(positions, 3));
  geometry.setAttribute('feature', new THREE.Float32BufferAttribute(weights, 2));
  geometry.setAttribute('seed', new THREE.Float32BufferAttribute(seeds, 1));
  const uniforms = { uTime: { value: 0 }, uEnergy: { value: 0 }, uAssemble: { value: 0 }, uPixelRatio: { value: renderer.getPixelRatio() }, uColor: { value: new THREE.Color('#71cfe0') } };
  const particles = new THREE.ShaderMaterial({
    uniforms, transparent: true, depthWrite: false, blending: THREE.AdditiveBlending,
    vertexShader: `
      attribute vec2 feature;
      attribute float seed;
      uniform float uTime, uEnergy, uAssemble, uPixelRatio;
      varying float vAlpha;
      void main() {
        vec3 p = position;
        float progress = smoothstep(0., 1., clamp(uAssemble * 1.7 - (position.y + .8) * .15, 0., 1.));
        p.xz *= 1. + (1. - progress) * 1.7;
        p.y = mix(-1., position.y, progress);
        p.y += sin(uTime * .8) * .025;
        p.y += feature.y * sin(seed) * uEnergy * .012;
        p.z += feature.y * uEnergy * .012;
        p.x += sin(uTime * 2. + seed) * .003 * uEnergy;
        vec4 mv = modelViewMatrix * vec4(p, 1.);
        gl_Position = projectionMatrix * mv;
        gl_PointSize = clamp(6. * uPixelRatio / -mv.z, .6, 1.7);
        float scan = pow(max(0., 1. - abs(position.y - mod(uTime * .5, 3.5) + .7) * 9.), 2.);
        vAlpha = (.035 + smoothstep(-.1, .4, position.z) * .10 + scan * .08) * progress;
        vAlpha *= smoothstep(-.65, -.1, position.y);
      }`,
    fragmentShader: `
      uniform vec3 uColor;
      varying float vAlpha;
      void main() {
        float d = length(gl_PointCoord - .5);
        if(d > .5) discard;
        float soft = 1. - smoothstep(.1, .5, d);
        gl_FragColor = vec4(uColor, vAlpha * soft);
      }`,
  });
  figure.add(new THREE.Points(geometry, particles));

  const meshPositions = [];
  for (let row = 5; row < 120; row += 5) {
    for (let col = 1; col < 155; col++) {
      meshPositions.push(...headRows[row][col - 1].toArray(), ...headRows[row][col].toArray());
      if (col % 10 === 0 && row < 115) meshPositions.push(...headRows[row][col].toArray(), ...headRows[row + 5][col].toArray());
    }
  }
  const meshGeometry = new THREE.BufferGeometry();
  meshGeometry.setAttribute('position', new THREE.Float32BufferAttribute(meshPositions, 3));
  const meshMaterial = new THREE.LineBasicMaterial({ color: 0x3484ad, transparent: true, opacity: .13, blending: THREE.AdditiveBlending, depthWrite: false });
  figure.add(new THREE.LineSegments(meshGeometry, meshMaterial));
  const facialMaterial = new THREE.LineBasicMaterial({ color: 0x93ebff, transparent: true, opacity: .65, blending: THREE.AdditiveBlending, depthWrite: false });
  const features = new THREE.Group();
  function faceLine(coordinates) {
    const line = new THREE.Line(new THREE.BufferGeometry().setFromPoints(coordinates.map((p) => new THREE.Vector3(...p))), facialMaterial);
    features.add(line);
    return line;
  }
  for (const side of [-1, 1]) {
    faceLine([[side * .12, 1.67, .54], [side * .23, 1.69, .53], [side * .35, 1.66, .49]]);
    faceLine([[side * .12, 1.81, .56], [side * .25, 1.84, .54], [side * .40, 1.78, .47]]);
    faceLine([[side * .46, 1.57, .40], [side * .42, 1.35, .46], [side * .30, 1.15, .47]]);
    faceLine([[side * .33, 1.06, .42], [side * .25, .89, .39], [side * .08, .78, .35]]);
  }
  faceLine([[0, 1.65, .60], [0, 1.38, .79], [.07, 1.31, .65]]);
  const mouth = faceLine(Array.from({ length: 49 }, (_, i) => { const a = i / 48 * Math.PI * 2; return [Math.cos(a) * .145, Math.sin(a) * .016, 0]; }));
  mouth.position.set(0, 1.06, .53);
  figure.add(features);
  let hasScan = false;
  // CC BY 3.0 scan by Lee Perry-Smith; attribution ships with the local asset.
  // Keep the procedural figure if the optional high-detail asset cannot load.
  new GLTFLoader().load('/assets/nova-head.glb', (gltf) => {
    let source;
    gltf.scene.traverse((node) => { if (node.isMesh && !source) source = node; });
    if (!source) return;
    const scan = source.geometry.clone();
    scan.computeBoundingBox();
    const box = scan.boundingBox;
    const scale = 3.25 / (box.max.y - box.min.y);
    scan.translate(-(box.min.x + box.max.x) / 2, -box.min.y, 0);
    scan.scale(scale, scale, scale);
    scan.translate(0, -.65, 0);
    const sampler = new MeshSurfaceSampler(new THREE.Mesh(scan)).build();
    const point = new THREE.Vector3();
    const sampled = [], features = [], seeds = [];
    for (let i = 0; i < 11000; i++) {
      sampler.sample(point);
      sampled.push(point.x, point.y, point.z);
      const front = THREE.MathUtils.smoothstep(point.z, .25, .6);
      features.push(Math.exp(-(((Math.abs(point.x) - .24) / .13) ** 2) - ((point.y - 1.67) / .07) ** 2) * front,
        Math.exp(-((point.x / .22) ** 2) - ((point.y - 1.06) / .07) ** 2) * front);
      seeds.push(i * .731);
    }
    geometry.dispose();
    geometry.setAttribute('position', new THREE.Float32BufferAttribute(sampled, 3));
    geometry.setAttribute('feature', new THREE.Float32BufferAttribute(features, 2));
    geometry.setAttribute('seed', new THREE.Float32BufferAttribute(seeds, 1));
    geometry.computeBoundingSphere();
    const edges = new THREE.WireframeGeometry(scan);
    meshGeometry.dispose();
    meshGeometry.copy(edges);
    edges.dispose();
    scan.computeVertexNormals();
    const surface = new THREE.ShaderMaterial({
      uniforms, transparent: true, depthWrite: false, side: THREE.FrontSide,
      vertexShader: `
        uniform float uTime, uEnergy;
        varying vec3 vNormal, vView, vPosition;
        void main() {
          vec3 p = position;
          p.y += sin(uTime * .8) * .025;
          float mouth = exp(-pow(p.x / .22, 2.) - pow((p.y - 1.06) / .07, 2.));
          p.z += mouth * uEnergy * .012;
          vec4 mv = modelViewMatrix * vec4(p, 1.);
          vNormal = normalize(normalMatrix * normal);
          vView = normalize(-mv.xyz);
          vPosition = p;
          gl_Position = projectionMatrix * mv;
        }`,
      fragmentShader: `
        uniform float uTime, uAssemble, uEnergy;
        uniform vec3 uColor;
        varying vec3 vNormal, vView, vPosition;
        void main() {
          vec3 n = normalize(vNormal);
          float rim = pow(1. - max(0., dot(n, normalize(vView))), 2.4);
          float key = max(0., dot(n, normalize(vec3(-.8, 1.2, 1.5))));
          float fill = max(0., dot(n, normalize(vec3(1., .2, .4))));
          float lines = smoothstep(.80, .97, sin(vPosition.y * 190.));
          float scan = exp(-pow((vPosition.y - mod(uTime * .28, 4.) + .8) * 12., 2.));
          vec3 color = vec3(.008, .045, .08) + uColor * (key * .20 + fill * .045 + rim * .8);
          color += uColor * (lines * .025 + scan * .08 + uEnergy * .025);
          float fade = smoothstep(-.65, -.12, vPosition.y);
          float reveal = smoothstep(vPosition.y + .8, vPosition.y + 1.1, uAssemble * 4.5);
          gl_FragColor = vec4(color, (.80 + rim * .18) * fade * reveal);
        }`,
    });
    // Populate depth first so the translucent face cannot reveal the rear skull.
    const depth = new THREE.ShaderMaterial({
      uniforms, vertexShader: surface.vertexShader,
      fragmentShader: 'void main(){gl_FragColor=vec4(0.);}',
      colorWrite: false, depthWrite: true, side: THREE.FrontSide,
    });
    figure.add(new THREE.Mesh(scan, depth));
    const skin = new THREE.Mesh(scan, surface);
    skin.renderOrder = 1;
    figure.add(skin);
    hasScan = true;
    container.dataset.detail = 'scanned';
  }, undefined, () => { container.dataset.detail = 'procedural'; });

  function arc(radius, start, length, color, opacity, vertical = false) {
    const positions = [];
    for (let i = 0; i <= 180; i++) {
      const a = start + length * i / 180;
      positions.push(new THREE.Vector3(Math.cos(a) * radius, 0, Math.sin(a) * radius));
    }
    const line = new THREE.Line(new THREE.BufferGeometry().setFromPoints(positions), new THREE.LineBasicMaterial({ color, transparent: true, opacity, blending: THREE.AdditiveBlending, depthWrite: false }));
    if (vertical) line.rotation.x = Math.PI / 2;
    return line;
  }
  const platform = new THREE.Group();
  platform.position.y = -1.13;
  scene.add(platform);
  const rings = [];
  for (let i = 0; i < 4; i++) {
    const ring = arc(1.65 + i * .24, i * .8, Math.PI * (i % 2 ? 1.5 : 1.95), i === 2 ? 0xb2f4ff : 0x3f9dce, i === 2 ? .45 : .15);
    ring.position.y = i * -.016;
    platform.add(ring);
    rings.push(ring);
  }
  const ticks = [];
  for (let i = 0; i < 96; i++) {
    const a = i / 96 * Math.PI * 2;
    const r = i % 10 === 0 ? 2.75 : 2.66;
    ticks.push(new THREE.Vector3(Math.cos(a) * 2.6, 0, Math.sin(a) * 2.6), new THREE.Vector3(Math.cos(a) * r, 0, Math.sin(a) * r));
  }
  platform.add(new THREE.LineSegments(new THREE.BufferGeometry().setFromPoints(ticks), new THREE.LineBasicMaterial({ color: 0x5599b4, transparent: true, opacity: .22 })));
  const halo = new THREE.Group();
  halo.position.set(0, .68, -.62);
  halo.add(arc(2.13, .4, Math.PI * 1.4, 0x276688, .18, true));
  halo.add(arc(2.22, -.6, Math.PI * .95, 0x57bbd4, .3, true));
  halo.add(arc(2.32, 1.2, Math.PI * .58, 0x568d9f, .2, true));
  scene.add(halo);
  const grid = new THREE.GridHelper(30, 60, 0x1a526a, 0x103045);
  grid.position.y = -1.28;
  grid.material.transparent = true;
  grid.material.opacity = .12;
  scene.add(grid);

  // Transparent projection cone: emission from the circular base to the figure.
  const beam = new THREE.Mesh(new THREE.CylinderGeometry(.8, 1.8, 3.7, 64, 1, true), new THREE.ShaderMaterial({
    transparent: true, depthWrite: false, side: THREE.DoubleSide, blending: THREE.AdditiveBlending,
    vertexShader: 'varying vec2 vUv; void main(){vUv=uv;gl_Position=projectionMatrix*modelViewMatrix*vec4(position,1.);}',
    fragmentShader: 'varying vec2 vUv; void main(){float stripes=pow(max(0.,sin(vUv.x*160.)),20.);gl_FragColor=vec4(.12,.55,.85,(1.-vUv.y)*(.008+stripes*.018));}',
  }));
  beam.position.y = .72;
  scene.add(beam);
  const dustPositions = [];
  for (let i = 0; i < 85; i++) dustPositions.push(Math.sin(i * 127.1) * 3.4, (i * .761 % 4.5) - 1.2, Math.cos(i * 33.3) * 1.8);
  const dustGeometry = new THREE.BufferGeometry();
  dustGeometry.setAttribute('position', new THREE.Float32BufferAttribute(dustPositions, 3));
  const dust = new THREE.Points(dustGeometry, new THREE.PointsMaterial({ color: 0x6ad5ed, size: .012, transparent: true, opacity: .18, depthWrite: false, blending: THREE.AdditiveBlending }));
  scene.add(dust);

  new ResizeObserver(([entry]) => {
    const { width, height } = entry.contentRect;
    if (!width || !height) return;
    camera.aspect = width / height;
    camera.updateProjectionMatrix();
    renderer.setSize(width, height, false);
    composer.setSize(width, height);
  }).observe(container);
  const pointer = { x: 0, y: 0 };
  canvas.addEventListener('pointermove', (event) => {
    const rect = canvas.getBoundingClientRect();
    pointer.x = (event.clientX - rect.left) / rect.width - .5;
    pointer.y = (event.clientY - rect.top) / rect.height - .5;
  });
  canvas.addEventListener('pointerleave', () => { pointer.x = pointer.y = 0; });
  canvas.addEventListener('webglcontextlost', (event) => { event.preventDefault(); document.getElementById('render-error').hidden = false; });
  canvas.addEventListener('webglcontextrestored', () => { document.getElementById('render-error').hidden = true; });

  let previous = 0, elapsed = 0, assembly = 0, assemblyStarted = null, energy = 0, previousMode = 'idle';
  const ice = new THREE.Color('#71cfe0'), amber = new THREE.Color('#e7c38d');
  function frame(timestamp) {
    requestAnimationFrame(frame);
    const visual = window.holoState || { mode: 'idle', mic: 0, output: 0 };
    const reduced = document.body.dataset.motion === 'reduced' || (document.body.dataset.motion !== 'full' && reducedMotion.matches);
    if (document.hidden) { previous = timestamp; return; }
    if (timestamp - previous < (reduced ? 125 : 32)) return;
    const dt = Math.min((timestamp - previous) / 1000, .1);
    previous = timestamp;
    if (!reduced) elapsed += dt;
    if (assemblyStarted === null || (visual.mode === 'preview' && previousMode !== 'preview' && !reduced)) assemblyStarted = timestamp;
    previousMode = visual.mode;
    assembly = reduced ? 1 : Math.min(1, (timestamp - assemblyStarted) / 1600);
    const demoEnergy = visual.mode === 'preview' ? .25 + Math.sin(elapsed * 5) ** 2 * .35 : 0;
    energy += (Math.max(visual.output || 0, demoEnergy) - energy) * .15;
    uniforms.uTime.value = elapsed;
    uniforms.uEnergy.value = energy;
    uniforms.uAssemble.value = assembly;
    uniforms.uColor.value.lerp(visual.mode === 'thinking' ? amber : ice, .04);
    meshMaterial.opacity = hasScan ? 0 : .06 * assembly;
    features.visible = assembly === 1 && !hasScan;
    mouth.scale.y = 1 + energy * 6;
    figure.rotation.y += ((reduced ? -.35 : -.35 + Math.sin(elapsed * .2) * .035 + pointer.x * .3) - figure.rotation.y) * .035;
    figure.rotation.x += ((reduced ? 0 : pointer.y * .08) - figure.rotation.x) * .035;
    figure.position.y = -.12 + (reduced ? 0 : Math.sin(elapsed * .7) * .025);
    rings.forEach((ring, i) => { ring.rotation.y = elapsed * (i % 2 ? -.1 : .12) * (1 + energy * .7) + i; });
    platform.scale.setScalar(1 + Math.max(energy, visual.mic || 0) * .035);
    halo.rotation.z = reduced ? 0 : Math.sin(elapsed * .15) * .1;
    halo.children[1].rotation.y = reduced ? 0 : Math.sin(elapsed * .1) * .15;
    dust.rotation.y = elapsed * .015;
    bloom.strength = .28 + energy * .12;
    composer.render();
  }
  requestAnimationFrame(frame);
}
