import * as THREE from 'three';
import { EffectComposer } from 'three/addons/postprocessing/EffectComposer.js';
import { RenderPass } from 'three/addons/postprocessing/RenderPass.js';
import { UnrealBloomPass } from 'three/addons/postprocessing/UnrealBloomPass.js';
import { OutputPass } from 'three/addons/postprocessing/OutputPass.js';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
import { VRMLoaderPlugin, VRMUtils } from '@pixiv/three-vrm';

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
  scene.add(figure);
  camera.position.set(0, 1.4, 8.5);
  camera.lookAt(0, 1.0, 0);
  let avatar = null;
  const uniforms = { uTime: { value: 0 }, uEnergy: { value: 0 } };
  const fallback = new THREE.Mesh(new THREE.IcosahedronGeometry(.45, 1), new THREE.MeshBasicMaterial({ color: 0x6bd7e8, wireframe: true, transparent: true, opacity: .5 }));
  fallback.position.y = .8;
  figure.add(fallback);
  scene.add(new THREE.HemisphereLight(0xe9f6ff, 0x24435c, 1.35));
  const keyLight = new THREE.DirectionalLight(0xfff2eb, 1.5);
  keyLight.position.set(-2, 4, 4);
  scene.add(keyLight);
  const rimLight = new THREE.DirectionalLight(0x76d8ff, 1.1);
  rimLight.position.set(2, 2, -2);
  scene.add(rimLight);
  const loader = new GLTFLoader();
  loader.register((parser) => new VRMLoaderPlugin(parser));
  loader.load('/assets/nova-female.vrm', (gltf) => {
    avatar = gltf.userData.vrm;
    VRMUtils.rotateVRM0(avatar);
    const bone = (name) => avatar.humanoid.getNormalizedBoneNode(name);
    bone('leftUpperArm').rotation.z = -1.18;
    bone('rightUpperArm').rotation.z = 1.18;
    bone('leftLowerArm').rotation.y = -.15;
    bone('rightLowerArm').rotation.y = .15;
    bone('hips').rotation.z = .025;
    avatar.update(0);
    avatar.scene.updateMatrixWorld(true);
    const box = new THREE.Box3().setFromObject(avatar.scene);
    const scale = 4.15 / (box.max.y - box.min.y);
    avatar.scene.scale.setScalar(scale);
    avatar.scene.position.set(-(box.min.x + box.max.x) / 2 * scale, -1.08 - box.min.y * scale, 0);
    avatar.scene.updateMatrixWorld(true);
    avatar.springBoneManager?.reset();
    // This constraint demo's secondary physics destabilizes after scene scaling.
    // Keep the authored hair shape and animate the humanoid instead.
    avatar.springBoneManager = undefined;
    avatar.scene.traverse((node) => {
      if (!node.isMesh) return;
      node.frustumCulled = false;
      const restyle = (original) => {
        const material = new THREE.MeshStandardMaterial({
          map: original.map, color: 0xffffff, roughness: .72, metalness: .03,
          side: original.side, transparent: original.transparent,
          opacity: original.opacity, alphaTest: original.alphaTest,
          depthWrite: original.depthWrite,
        });
        const hair = /hair/i.test(original.name);
        const clothes = /cloth/i.test(original.name);
        if (clothes) material.color.set(/bottom/i.test(original.name) ? '#566d8b' : '#b4d0e5');
        material.onBeforeCompile = (shader) => {
          shader.uniforms.uTime = uniforms.uTime;
          shader.uniforms.uEnergy = uniforms.uEnergy;
          shader.vertexShader = 'varying float vHoloY;\n' + shader.vertexShader;
          shader.vertexShader = shader.vertexShader.replace('#include <project_vertex>', 'vHoloY = (modelMatrix * vec4(transformed, 1.0)).y;\n#include <project_vertex>');
          shader.fragmentShader = 'uniform float uTime, uEnergy; varying float vHoloY;\n' + shader.fragmentShader;
          shader.fragmentShader = shader.fragmentShader.replace('#include <dithering_fragment>', `
            #include <dithering_fragment>
            float scan = pow(max(0., sin(vHoloY * 220. - uTime * .7)), 12.);
            float rim = pow(1. - max(0., dot(normal, normalize(vViewPosition))), 2.8);
            gl_FragColor.rgb = mix(gl_FragColor.rgb, gl_FragColor.rgb * vec3(.65, .94, 1.12), .20);
            ${hair ? 'float hairLuma = dot(gl_FragColor.rgb, vec3(.2126, .7152, .0722)); gl_FragColor.rgb = mix(vec3(.035,.065,.13),vec3(.65,.82,.94),sqrt(hairLuma));' : ''}
            gl_FragColor.rgb += vec3(.18, .46, .6) * (scan * .045 + rim * (.12 + uEnergy * .06));
          `);
        };
        return material;
      };
      node.material = Array.isArray(node.material) ? node.material.map(restyle) : restyle(node.material);
    });
    figure.add(avatar.scene);
    container.dataset.detail = 'full-body';
  }, undefined, () => {
    container.dataset.detail = 'abstract';
    document.getElementById('render-error').hidden = false;
    document.getElementById('render-error').textContent = '角色加载失败，暂时显示无面孔的全息核心。语音通话仍可使用。';
  });

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
  const beam = new THREE.Mesh(new THREE.CylinderGeometry(.75, 1.3, 4.2, 64, 1, true), new THREE.ShaderMaterial({
    transparent: true, depthWrite: false, side: THREE.DoubleSide, blending: THREE.AdditiveBlending,
    vertexShader: 'varying vec2 vUv; void main(){vUv=uv;gl_Position=projectionMatrix*modelViewMatrix*vec4(position,1.);}',
    fragmentShader: 'varying vec2 vUv; void main(){float stripes=pow(max(0.,sin(vUv.x*160.)),20.);gl_FragColor=vec4(.12,.55,.85,(1.-vUv.y)*(.008+stripes*.018));}',
  }));
  beam.position.y = 1.0;
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

  let previous = 0, elapsed = 0, energy = 0;
  function frame(timestamp) {
    requestAnimationFrame(frame);
    const visual = window.holoState || { mode: 'idle', mic: 0, output: 0 };
    const reduced = document.body.dataset.motion === 'reduced' || (document.body.dataset.motion !== 'full' && reducedMotion.matches);
    if (document.hidden) { previous = timestamp; return; }
    if (timestamp - previous < (reduced ? 125 : 32)) return;
    const dt = Math.min((timestamp - previous) / 1000, .1);
    previous = timestamp;
    if (!reduced) elapsed += dt;
    const demoEnergy = visual.mode === 'preview' ? .25 + Math.sin(elapsed * 5) ** 2 * .35 : 0;
    energy += (Math.max(visual.output || 0, demoEnergy) - energy) * .15;
    uniforms.uTime.value = elapsed;
    uniforms.uEnergy.value = energy;
    fallback.visible = !avatar || visual.avatar === 'core';
    if (avatar) {
      avatar.scene.visible = visual.avatar !== 'core';
      avatar.expressionManager?.setValue('aa', energy * .45);
      avatar.expressionManager?.setValue('happy', .18);
      const blink = reduced ? 0 : Math.max(0, 1 - Math.abs((elapsed % 5.3) - 4.9) / .1);
      avatar.expressionManager?.setValue('blink', blink);
      avatar.humanoid.getNormalizedBoneNode('head').rotation.y = reduced ? 0 : Math.sin(elapsed * .3) * .035;
      avatar.humanoid.getNormalizedBoneNode('chest').rotation.x = reduced ? 0 : Math.sin(elapsed * 1.1) * .006;
      avatar.update(reduced ? 0 : dt);
    } else fallback.rotation.y = elapsed * .2;
    figure.rotation.y += ((reduced ? -.06 : -.06 + pointer.x * .25) - figure.rotation.y) * .035;
    figure.rotation.x += ((reduced ? 0 : pointer.y * .08) - figure.rotation.x) * .035;
    figure.position.y = reduced ? 0 : Math.sin(elapsed * .7) * .012;
    rings.forEach((ring, i) => { ring.rotation.y = elapsed * (i % 2 ? -.1 : .12) * (1 + energy * .7) + i; });
    platform.scale.setScalar(1 + Math.max(energy, visual.mic || 0) * .035);
    halo.rotation.z = reduced ? 0 : Math.sin(elapsed * .15) * .1;
    halo.children[1].rotation.y = reduced ? 0 : Math.sin(elapsed * .1) * .15;
    dust.rotation.y = elapsed * .015;
    bloom.strength = .14 + energy * .08;
    composer.render();
  }
  requestAnimationFrame(frame);
}
