import * as THREE from './vendor/three.module.js';

export function createPresence() {
  const group = new THREE.Group();
  const uniforms = {
    time: { value: 0 }, signal: { value: 0 }, speaking: { value: 0 },
    opacity: { value: 1 }
  };
  const controls = [
    [[-3.8,-1.4,-.9],[-2.4,-.5,.4],[-1.1,.5,.8],[.35,-.1,.2],[1.4,-1.6,-.3],[2.1,-.8,.9],[1.5,.3,.7],[.1,1.25,-.4],[-1.8,1.9,-1.1],[-2.4,.7,-.4],[-1.1,.5,.8],[.8,.65,.1],[2.6,1.55,-.6],[3.7,2.2,-1]],
    [[-3.1,.8,-1],[-1.7,1.5,-.6],[-1.1,.5,.7],[-.3,-1.2,1],[1.35,-1.6,.1],[2.5,-.3,-.6],[1.1,.75,-.4],[-.3,1.6,.3],[-1.1,.5,.7],[-.4,-.3,1],[1.2,-.05,.6],[3.3,.8,-.7]],
    [[-2.8,-2,-.5],[-1.3,-.8,-.8],[.1,1.25,-.2],[1.6,1.5,.7],[2.25,.4,.8],[1.35,-1.6,.1],[.2,-1.9,-.5],[-1.2,-1,-.8],[-1.1,.5,.7],[.1,1.25,.2],[2.3,1,-.9],[3.6,.5,-1.1]],
    [[-3.5,-.5,-1],[-1.9,-.2,.1],[-1.1,.5,.7],[.1,1.25,.2],[1.8,.7,.6],[2.2,-.5,.8],[1.35,-1.6,.1],[.4,-.6,-.5],[1.8,.8,-.8],[3.2,1.7,-1]]
  ];
  const curves = [];
  const vertexShader = `
    uniform float time; uniform float signal;
    varying float along; varying float depth;
    void main(){
      along=uv.x;
      vec3 p=position;
      float taper=sin(uv.x*3.1415926);
      p.y+=.045*sin(p.x*1.8+p.z*2.0+time*.65)*taper;
      p.z+=.06*cos(p.y*1.5+time*.45)*taper;
      p *= 1.0 - signal*.055;
      vec4 view=modelViewMatrix*vec4(p,1.0); depth=-view.z;
      gl_Position=projectionMatrix*view;
    }`;
  const fragmentShader = `
    uniform float time; uniform float signal; uniform float speaking;
    uniform float opacity; uniform vec3 tint; uniform float strength; uniform float seed;
    varying float along; varying float depth;
    void main(){
      float ends=smoothstep(0.0,.1,along)*(1.0-smoothstep(.78,1.0,along));
      float travel=fract(along-time*(.055+speaking*.07)+seed);
      float packet=exp(-pow((travel-.5)*24.0,2.0));
      float rhythm=.5+.5*sin(along*44.0-time*4.0+seed*6.0);
      float front=clamp((14.5-depth)*.35,.14,1.0);
      float light=(.43+packet*1.7+signal*(.15+rhythm*.9))*front;
      gl_FragColor=vec4(tint*(.78+packet*.8),ends*light*strength*opacity);
    }`;
  for (let family = 0; family < controls.length; family++) {
    for (let strand = 0; strand < 12; strand++) {
      const spread = (strand - 5.5) / 5.5;
      const points = controls[family].map((point, i) => {
        const taper = Math.sin(i / (controls[family].length - 1) * Math.PI);
        return new THREE.Vector3(point[0] + spread*.10*Math.sin(i*1.8),
          point[1]+spread*.17*taper, point[2]+spread*.21*taper);
      });
      const curve = new THREE.CatmullRomCurve3(points, false, 'centripetal');
      curves.push(curve);
      const amber = family === 2 && strand >= 9;
      const strong = strand === 5 || strand === 6;
      const tint = new THREE.Color(amber ? '#ffd5a0' : strong ? '#c4faff' : '#299bb6');
      const params = { ...uniforms, tint: {value:tint}, strength:{value:strong?1.1:.65}, seed:{value:strand*.061+family*.19} };
      const mat = new THREE.ShaderMaterial({uniforms:params,vertexShader,fragmentShader,
        transparent:true,depthWrite:false,blending:THREE.AdditiveBlending});
      const radius = strong ? .013 : .0045;
      const tube = new THREE.Mesh(new THREE.TubeGeometry(curve,220,radius,4,false),mat);
      group.add(tube);
      if (strong) {
        const haloMat=mat.clone();
        haloMat.uniforms.time=uniforms.time;haloMat.uniforms.signal=uniforms.signal;
        haloMat.uniforms.speaking=uniforms.speaking;
        haloMat.uniforms.strength.value=.13;
        group.add(new THREE.Mesh(new THREE.TubeGeometry(curve,180,.065,6,false),haloMat));
      }
    }
  }
  const particleCount=230, positions=new Float32Array(particleCount*3);
  const particlesGeometry=new THREE.BufferGeometry();
  particlesGeometry.setAttribute('position',new THREE.BufferAttribute(positions,3));
  const points=new THREE.Points(particlesGeometry,new THREE.ShaderMaterial({
    uniforms:{signal:uniforms.signal},
    vertexShader:`uniform float signal; varying float light;void main(){
      vec4 p=modelViewMatrix*vec4(position,1.);light=clamp((14.7+p.z)*.23,.15,.7);
      gl_Position=projectionMatrix*p;gl_PointSize=(1.4+signal*1.7)*min(1.4,12./-p.z);
    }`,
    fragmentShader:`varying float light;void main(){float r=length(gl_PointCoord-.5);if(r>.5)discard;
      gl_FragColor=vec4(.35,.85,1.,(1.-r*2.)*light);}`,
    transparent:true,depthWrite:false,blending:THREE.AdditiveBlending
  }));
  group.add(points);
  const knots=[new THREE.Vector3(-1.1,.5,.7),new THREE.Vector3(1.35,-1.6,.1),new THREE.Vector3(.1,1.25,.2)];
  for(let i=0;i<knots.length;i++){
    const mat=new THREE.ShaderMaterial({uniforms:{signal:uniforms.signal,tint:{value:new THREE.Color(i===2?'#f7c18b':'#8bf2ff')}},
      vertexShader:`varying vec2 v;void main(){v=uv;gl_Position=projectionMatrix*modelViewMatrix*vec4(position,1.);}`,
      fragmentShader:`uniform float signal;uniform vec3 tint;varying vec2 v;void main(){
        float d=length(v-.5);float glow=exp(-d*13.)*.32+exp(-d*75.)*.95;
        gl_FragColor=vec4(tint,glow*(.75+signal));}`,
      transparent:true,depthWrite:false,blending:THREE.AdditiveBlending});
    const mesh=new THREE.Mesh(new THREE.PlaneGeometry(1.8,1.8),mat);
    mesh.position.copy(knots[i]);group.add(mesh);
  }
  return {group,update(time,signal,speaking,reduced){
    uniforms.time.value=reduced?0:time;uniforms.signal.value=reduced?0:signal;uniforms.speaking.value=speaking?1:0;
    for(let i=0;i<particleCount;i++){
      const curve=curves[i%curves.length],u=(i*.6180339+(reduced?0:time*.017))%1;
      const point=curve.getPoint(u);
      point.x+=.07*Math.sin(i*31.3);point.y+=.09*Math.sin(i*7.7);point.z+=.12*Math.cos(i*4.9);
      point.toArray(positions,i*3);
    }
    particlesGeometry.attributes.position.needsUpdate=true;
  }};
}

