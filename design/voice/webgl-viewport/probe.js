import * as THREE from './vendor/three.module.js';

// Geometry and its staged reveal share the same local coordinates.
// All transparency comes from live WebGL primitives.
export function createProbe() {
  const group = new THREE.Group();
  group.name = 'TT-101 schematic temperature probe';
  const paths = [];
  const tip = new THREE.Vector3(0, -2.4, 0);
  const head = new THREE.Vector3(.76, 1.32, 0);
  const tau = Math.PI * 2;
  const cyan = new THREE.Color('#8ceeff');
  const amber = new THREE.Color('#ffbf70');
  let lastBuild = 0;
  let lastFade = 0;

  function path(points, start, duration, brightness = 1) {
    paths.push({ points, start, duration, brightness });
  }

  function ring(radius, y, start, duration, brightness = 1, segments = 72) {
    const points = [];
    for (let i = 0; i <= segments; i++) {
      const angle = i / segments * tau;
      points.push(new THREE.Vector3(radius * Math.cos(angle), y, radius * Math.sin(angle)));
    }
    path(points, start, duration, brightness);
  }

  const profile = [
    [.18, 2.04], [.48, 2.02], [.64, 1.97], [.72, 1.87],
    [.74, 1.72], [.74, 1.30], [.70, 1.19], [.53, 1.08],
    [.29, 1.04], [.25, .91], [.25, .73], [.18, .64]
  ];

  // First draw the axis and a few major silhouettes before adding ring detail.
  path([new THREE.Vector3(0, -2.4, 0), new THREE.Vector3(0, 2.04, 0)], .025, .23, .35);
  [0, Math.PI / 2, Math.PI, Math.PI * 1.5].forEach((angle, i) => {
    path(profile.map(([r, y]) => new THREE.Vector3(r * Math.cos(angle), y, r * Math.sin(angle))),
      .09 + i * .035, .25, .95);
  });
  profile.forEach(([radius, y], i) => {
    ring(radius, y, .27 + i * .018, .19, i === 3 || i === 5 || i === 8 ? 1 : .55);
  });

  // The ribs on the head stay individually legible through the transparent cap.
  for (let i = 0; i < 16; i++) {
    const angle = i / 16 * tau;
    path(profile.slice(0, 9).map(([r, y]) =>
      new THREE.Vector3(r * Math.cos(angle), y, r * Math.sin(angle))),
    .38 + i * .009, .21, i % 4 === 0 ? .8 : .42);
  }

  // A radial cable gland on the right makes viewpoint changes unambiguous.
  const glandCenter = new THREE.Vector3(.67, 1.40, 0);
  const glandProfile = [[0, .18], [.13, .18], [.18, .23], [.37, .23], [.43, .15]];
  glandProfile.forEach(([x, radius], index) => {
    const points = [];
    for (let i = 0; i <= 6; i++) {
      const angle = i / 6 * tau + Math.PI / 6;
      points.push(new THREE.Vector3(glandCenter.x + x,
        glandCenter.y + radius * Math.cos(angle), radius * Math.sin(angle)));
    }
    path(points, .52 + index * .035, .15, .9);
  });
  for (let i = 0; i < 6; i++) {
    const angle = i / 6 * tau + Math.PI / 6;
    path(glandProfile.map(([x, radius]) =>
      new THREE.Vector3(glandCenter.x + x,
        glandCenter.y + radius * Math.cos(angle), radius * Math.sin(angle))),
    .60 + i * .012, .17, .7);
  }

  // Hex collar and screw thread above the long immersion shaft.
  ring(.23, .63, .62, .15, 1, 6);
  ring(.23, .43, .65, .15, 1, 6);
  for (let i = 0; i < 6; i++) {
    const angle = i / 6 * tau;
    path([new THREE.Vector3(.23 * Math.cos(angle), .63, .23 * Math.sin(angle)),
      new THREE.Vector3(.23 * Math.cos(angle), .43, .23 * Math.sin(angle))],
    .63 + i * .009, .18, .9);
  }
  for (let i = 0; i < 7; i++) {
    ring(.15, .39 - i * .027, .69 + i * .012, .12, .63);
  }

  // The shaft outline arrives early; its cross-sections are filled last.
  for (let i = 0; i < 8; i++) {
    const angle = i / 8 * tau;
    path([new THREE.Vector3(.07 * Math.cos(angle), .20, .07 * Math.sin(angle)),
      new THREE.Vector3(.07 * Math.cos(angle), -2.33, .07 * Math.sin(angle)),
      new THREE.Vector3(.05 * Math.cos(angle), -2.38, .05 * Math.sin(angle)),
      tip.clone()], .19 + i * .02, .34, i % 2 === 0 ? .85 : .4);
  }
  ring(.09, .20, .77, .10, .8);
  for (let i = 0; i < 10; i++) {
    ring(.07, .10 - i * .265, .79 + i * .013, .08, .35);
  }
  ring(.07, -2.30, .90, .08, .75);
  ring(.05, -2.38, .92, .08, .85);

  const positions = [], normals = [], births = [], strengths = [];
  const segments = [];
  for (const line of paths) {
    for (let i = 0; i < line.points.length - 1; i++) {
      const birth0 = line.start + i / (line.points.length - 1) * line.duration;
      const birth1 = line.start + (i + 1) / (line.points.length - 1) * line.duration;
      segments.push({ start: birth0, end: birth1, a: line.points[i], b: line.points[i + 1] });
      for (const [point, birth] of [[line.points[i], birth0], [line.points[i + 1], birth1]]) {
        positions.push(point.x, point.y, point.z);
        const normal = new THREE.Vector3(point.x, .15, point.z).normalize();
        normals.push(normal.x, normal.y, normal.z);
        births.push(birth);
        strengths.push(line.brightness);
      }
    }
  }
  const geometry = new THREE.BufferGeometry();
  geometry.setAttribute('position', new THREE.Float32BufferAttribute(positions, 3));
  geometry.setAttribute('normal', new THREE.Float32BufferAttribute(normals, 3));
  geometry.setAttribute('birth', new THREE.Float32BufferAttribute(births, 1));
  geometry.setAttribute('strength', new THREE.Float32BufferAttribute(strengths, 1));
  const uniforms = {
    build: { value: 0 }, fade: { value: 0 }, focus: { value: 0 },
    cyan: { value: cyan }, amber: { value: amber }
  };
  const material = new THREE.ShaderMaterial({
    transparent: true, depthWrite: false, blending: THREE.AdditiveBlending,
    uniforms,
    vertexShader: `
      attribute float birth;
      attribute float strength;
      varying float vBirth;
      varying float vStrength;
      varying float vLocalY;
      void main() {
        vBirth = birth;
        vec3 normalView = normalize(normalMatrix * normal);
        vStrength = strength * (.48 + .52 * max(0., normalView.z));
        vLocalY = position.y;
        gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.);
      }`,
    fragmentShader: `
      uniform float build;
      uniform float fade;
      uniform float focus;
      uniform vec3 cyan;
      uniform vec3 amber;
      varying float vBirth;
      varying float vStrength;
      varying float vLocalY;
      void main() {
        if (build <= 0. || vBirth > build || fade >= 1.) discard;
        if (fade > 0. && vBirth > 1. - fade) discard;
        float scan = 1. + 1.2 * exp(-110. * abs(build - vBirth));
        float tipWeight = 1. - smoothstep(-2.32, -2.20, vLocalY);
        float localFocus = focus * tipWeight;
        float dim = mix(1., .68, focus * (1. - tipWeight));
        vec3 color = mix(cyan, amber, localFocus);
        gl_FragColor = vec4(color * scan, vStrength * dim * (1. + localFocus * .8));
      }`
  });
  const linework = new THREE.LineSegments(geometry, material);
  group.add(linework);

  // Thin volumetric contours keep the silhouette legible at a distance.
  // Birth attributes use the same staged reveal as the fine wireframe.
  for (const line of paths.filter(line => line.brightness >= .85)) {
    const curve = new THREE.CurvePath();
    for (let i = 0; i < line.points.length - 1; i++)
      curve.add(new THREE.LineCurve3(line.points[i], line.points[i + 1]));
    for (const [radius, strength] of [[.006, .60], [.018, .085]]) {
      const contour = new THREE.TubeGeometry(curve, 96, radius, 4, false);
      const uv = contour.getAttribute('uv');
      const birth = new Float32Array(uv.count);
      const intensity = new Float32Array(uv.count);
      for (let i = 0; i < uv.count; i++) {
        birth[i] = line.start + uv.getX(i) * line.duration;
        intensity[i] = strength;
      }
      contour.setAttribute('birth', new THREE.BufferAttribute(birth, 1));
      contour.setAttribute('strength', new THREE.BufferAttribute(intensity, 1));
      group.add(new THREE.Mesh(contour, material));
    }
  }

  // Real 3D points mark the active scan and later drift away from dismantled edges.
  const pointCount = 240;
  const seeds = [];
  const pointPositions = new Float32Array(pointCount * 3);
  const pointAlphas = new Float32Array(pointCount);
  for (let i = 0; i < pointCount; i++) {
    const segment = segments[(i * 67) % segments.length];
    seeds.push({ point: segment.a.clone(), birth: segment.start, angle: i * 2.399963,
      speed: .25 + (i % 9) * .05 });
  }
  const pointGeometry = new THREE.BufferGeometry();
  pointGeometry.setAttribute('position', new THREE.BufferAttribute(pointPositions, 3));
  pointGeometry.setAttribute('alpha', new THREE.BufferAttribute(pointAlphas, 1));
  const pointMaterial = new THREE.ShaderMaterial({
    transparent: true, depthWrite: false, blending: THREE.AdditiveBlending,
    uniforms: { color: { value: new THREE.Color('#76dfee') } },
    vertexShader: `
      attribute float alpha;
      varying float vAlpha;
      void main() {
        vAlpha = alpha;
        vec4 view = modelViewMatrix * vec4(position, 1.);
        gl_PointSize = min(4., 20. / max(1., -view.z));
        gl_Position = projectionMatrix * view;
      }`,
    fragmentShader: `
      uniform vec3 color;
      varying float vAlpha;
      void main() {
        float distance = length(gl_PointCoord - .5) * 2.;
        if (distance > 1.) discard;
        gl_FragColor = vec4(color, (1. - distance) * vAlpha);
      }`
  });
  group.add(new THREE.Points(pointGeometry, pointMaterial));

  function update({ build = 0, fade = 0, focus = 0, time = 0, angle = 0, reduced = false }) {
    lastBuild = THREE.MathUtils.clamp(build, 0, 1);
    lastFade = THREE.MathUtils.clamp(fade, 0, 1);
    group.visible = lastBuild > 0 && lastFade < 1;
    group.rotation.y = angle;
    group.rotation.x = .10 + Math.sin(angle) * .18;
    group.rotation.z = .04 * Math.sin(angle);
    uniforms.build.value = lastBuild;
    uniforms.fade.value = lastFade;
    uniforms.focus.value = focus;
    seeds.forEach((seed, index) => {
      const dismantled = Math.max(0, lastFade - (1 - seed.birth));
      const drift = reduced ? 0 : dismantled * 2.3;
      const pulse = reduced ? 1 : .7 + .3 * Math.sin(time * 1.6 + seed.angle);
      seed.point.toArray(pointPositions, index * 3);
      pointPositions[index * 3] += Math.cos(seed.angle) * drift;
      pointPositions[index * 3 + 1] += drift * seed.speed;
      pointPositions[index * 3 + 2] += Math.sin(seed.angle) * drift;
      const drawn = seed.birth < lastBuild;
      pointAlphas[index] = drawn ? (dismantled > 0 ? Math.max(0, .7 - dismantled * 2) :
        .12 + .5 * Math.exp(-40 * Math.abs(lastBuild - seed.birth))) * pulse * (1 - lastFade) : 0;
    });
    pointGeometry.attributes.position.needsUpdate = true;
    pointGeometry.attributes.alpha.needsUpdate = true;
  }

  function stats() {
    let visibleSegments = 0, completeSegments = 0;
    for (const segment of segments) {
      if (segment.start < lastBuild && segment.start <= 1 - lastFade && lastFade < 1) visibleSegments++;
      if (segment.end <= lastBuild && segment.end <= 1 - lastFade && lastFade < 1) completeSegments++;
    }
    return { totalSegments: segments.length, visibleSegments, completeSegments,
      build: lastBuild, fade: lastFade };
  }

  update({});
  return { group, update, tip, head, stats };
}

