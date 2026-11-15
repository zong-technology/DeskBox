import * as THREE from './vendor/three.module.js';
import {createPresence} from './presence.js';
import {createProbe} from './probe.js';
const canvas=document.querySelector('canvas'), ctx=canvas.getContext('2d');
const scene=new THREE.Scene();
const renderer=new THREE.WebGLRenderer({antialias:true,preserveDrawingBuffer:true});
renderer.setSize(1920,1080);renderer.setPixelRatio(1);renderer.setClearColor('#02070b');
const camera=new THREE.PerspectiveCamera(42,1920/1080,.1,100);
camera.position.set(0,.3,13);camera.lookAt(0,0,0);
const presence=createPresence(),probe=createProbe();
scene.add(presence.group,probe.group);
probe.group.position.set(3.15,.25,0);
const links=[];
for(let i=0;i<7;i++){
 const curve=new THREE.CubicBezierCurve3(new THREE.Vector3(-4.1,-.6,.5),
 new THREE.Vector3(-1.8,-1.5+i*.09,.3),new THREE.Vector3(.7,2-i*.13,-.8),
 new THREE.Vector3(3.15,1.6-i*.1,0));
 const geometry=new THREE.BufferGeometry().setFromPoints(curve.getPoints(100));
 const line=new THREE.Line(geometry,new THREE.LineBasicMaterial({color:i===5?'#d8a267':'#6cd8ea',transparent:true,opacity:.3,depthWrite:false}));
 scene.add(line);links.push(line);
}
let time=0, motionTime=0, playing=false,reduced=false,interrupted=false,ended=false,cc=true;
let mode='main',angle=.32,drag=null,last=performance.now(),recording=false;
const smooth=t=>{t=Math.max(0,Math.min(1,t));return t*t*(3-2*t)};
function state(){
 const cleared=interrupted||ended;
 const exit=reduced?+(time>=8.55):smooth((time-8.55)/2.25);
 const presenceMove=cleared?0:(reduced?+(time>=3.85):smooth((time-3.85)/.7))*(1-exit);
 const build=cleared?0:reduced?+(time>=3.85):Math.max(0,Math.min(1,(time-3.85)/1.65));
 const focus=cleared||time>=8.55?0:reduced?+(time>=5.5):smooth((time-5.5)/.5);
 const caption=ended?'会话已结束':interrupted?'已打断 · 正在聆听':time<1.5?'正在聆听':time<3.85?'我来解释一下。':time<5.5?'这个是温度探头。':time<8.55?'它的尖端是感温位置。':time<10.8?'说回刚才的话题，我们继续聊。':'正在聆听';
 return {build,exit,focus,presenceMove,caption};
}
function text(s,x,y,size=22,color='#edf7fc',align='left',weight=400){
 ctx.fillStyle=color;ctx.font=weight+' '+size+'px "Microsoft YaHei UI",system-ui';ctx.textAlign=align;ctx.fillText(s,x,y);
}
function line(points,color,w=1){ctx.beginPath();points.forEach(([x,y],i)=>i?ctx.lineTo(x,y):ctx.moveTo(x,y));ctx.strokeStyle=color;ctx.lineWidth=w;ctx.stroke()}
function ring(x,y,r,color,w=1){ctx.beginPath();ctx.arc(x,y,r,0,Math.PI*2);ctx.strokeStyle=color;ctx.lineWidth=w;ctx.stroke()}
function project(local){const point=probe.group.localToWorld(local.clone()).project(camera);return [(point.x+1)*960,(1-point.y)*540]}
function draw(){
 const s=state(),speaking=playing&&time>=1.5&&time<8.55;
 const signal=.12+.36*Math.pow(Math.sin(motionTime*4.5),4);
 presence.group.position.set(-4.05*s.presenceMove,.25-1.35*s.presenceMove,0);
 presence.group.scale.setScalar(1.28-.69*s.presenceMove);
 presence.update(motionTime,signal,speaking,reduced);
 probe.update({build:s.build,fade:s.exit,focus:s.focus,time:reduced?0:motionTime,angle,reduced});
 links.forEach((link,i)=>{
  const extend=Math.min(1,Math.max(0,(time-3.85)/.65-i*.025))*(1-s.exit);
  link.visible=s.build>0&&s.exit<1&&!reduced;
  link.geometry.setDrawRange(0,Math.floor(101*extend));
  link.material.opacity=(.25+i*.035)*(1-s.exit);
 });
 renderer.render(scene,camera);
 ctx.clearRect(0,0,1920,1080);ctx.drawImage(renderer.domElement,0,0);
 ctx.strokeStyle='#28404c';ctx.lineWidth=1.5;ctx.beginPath();ctx.roundRect(24,24,1872,1032,20);ctx.stroke();
 text('DESKBOX',76,91,29,'#edf7fc','left',700);text('LIVE',232,91,29,'#61e6f3','left',700);text('WORKBENCH',321,91,29);
 line([[535,81],[1550,81]],'#28404c');text('●  LIVE',1600,90,20,'#61e6f3');text('演示会话',1740,90,20,'#9db0bb');
 const labelFade=s.exit>0?Math.max(0,1-s.exit*6):1;
 if(s.build>=.72&&labelFade>0){
  ctx.globalAlpha=labelFade;
  const head=project(probe.head);
  line([head,[1500,245],[1840,245]],'#c99e6a',1.2);ring(...head,3,'#ffce92',1.4);
  text('TT-101 · 温度探头 · 示意',1500,230,21);
  if(s.focus>0){
   const tip=project(probe.tip);
   line([tip,[1450,730],[1660,730]],'#ffbe73',1.3);
   ring(...tip,12,'#ffbe73',1.5);text('感温端',1460,714,22,'#ffbe73');
  }
  ctx.globalAlpha=1;
 }
 if(cc)text(s.caption,960,904,28,'#edf7fc','center');
 line([[86,951],[706,951]],'#28404c');line([[1214,951],[1834,951]],'#28404c');
 ['麦克风','暂停','字幕','结束'].forEach((name,i)=>{
  const x=840+80*i,y=995,color=i===0?'#61e6f3':i===3?'#f97478':'#9db0bb';
  ring(x,y,29,color,1.4);
  if(i===0){ctx.beginPath();ctx.roundRect(x-4,y-10,8,15,4);ctx.stroke();ctx.beginPath();ctx.arc(x,y+1,8,0,Math.PI);ctx.stroke();line([[x,y+9],[x,y+14]],color,2)}
  if(i===1){if(playing){line([[x-4,y-8],[x-4,y+8]],color,3);line([[x+4,y-8],[x+4,y+8]],color,3)}else line([[x-4,y-8],[x+8,y],[x-4,y+8],[x-4,y-8]],color,2)}
  if(i===2){ctx.strokeStyle=color;ctx.strokeRect(x-11,y-8,22,16);text('CC',x,y+4,9,color,'center',700)}
  if(i===3){ctx.beginPath();ctx.arc(x,y+11,15,Math.PI*1.25,Math.PI*1.75);ctx.strokeStyle=color;ctx.lineWidth=5;ctx.stroke()}
  text(name,x,y+50,13,color,'center');
 });
 document.querySelector('#status').textContent='WebGL 2 · '+(reduced?'减少动效':'拖动探头旋转')+' · '+time.toFixed(2)+' s';
}
function interrupt(){playing=false;interrupted=true;ended=false;draw()}
function play(branch='main'){mode=branch;time=0;motionTime=0;playing=true;interrupted=ended=false;last=performance.now()}
function seek(t,rotation=.32){time=t;motionTime=t;angle=rotation;playing=false;interrupted=ended=false;draw()}
document.querySelector('#main').onclick=()=>play();
document.querySelector('#cut').onclick=()=>play('cut-build');
document.querySelector('#cut-tip').onclick=()=>play('cut-tip');
document.querySelector('#reset').onclick=()=>{cc=true;seek(0)};
document.querySelector('#less').onchange=e=>{reduced=e.target.checked;draw()};
document.querySelector('#front').onclick=()=>{angle=0;draw()};
document.querySelector('#side').onclick=()=>{angle=1.05;draw()};
addEventListener('keydown',e=>{if(e.key.toLowerCase()==='i')interrupt()});
const coords=e=>{const r=canvas.getBoundingClientRect();return [(e.clientX-r.x)*1920/r.width,(e.clientY-r.y)*1080/r.height]};
canvas.onpointerdown=e=>{
 const [x,y]=coords(e),i=Math.round((x-840)/80);
 if(Math.hypot(x-840-i*80,y-995)<=31){
  if(i===0)interrupt();
  if(i===1&&!ended&&!interrupted){playing=!playing;last=performance.now();draw()}
  if(i===2){cc=!cc;draw()}
  if(i===3){ended=true;interrupted=false;playing=false;draw()}
  return;
 }
 if(x>1000&&y>120&&y<850&&probe.group.visible){drag=e.clientX;canvas.setPointerCapture(e.pointerId)}
};
canvas.onpointermove=e=>{if(drag!==null){angle+=(e.clientX-drag)*.008;drag=e.clientX;draw()}};
canvas.onpointerup=()=>drag=null;canvas.onpointercancel=()=>drag=null;
function tick(now){
 if(recording){last=now;requestAnimationFrame(tick);return;}
 const dt=Math.max(0,(now-last)/1000);last=now;
 if(playing){
  time+=dt;
  if(mode==='cut-build'&&time>=4.45||mode==='cut-tip'&&time>=6.3)interrupt();
  if(time>=12.2)playing=false;
 }
 if(!reduced&&(playing||interrupted||time<1.5||time>=10.8))motionTime+=dt;
 draw();requestAnimationFrame(tick);
}
function info(){return {...state(),time,interrupted,playing,tip:project(probe.tip),head:project(probe.head),probe:probe.stats(),renderer:renderer.getContext().getParameter(renderer.getContext().VERSION)}}
window.sample={seek,play,interrupt,info,canvas,setReduced(value){reduced=value;document.querySelector('#less').checked=value;draw()}};
async function save(name,blob){const r=await fetch('/save/'+name,{method:'POST',body:blob});if(!r.ok)throw Error('保存失败 '+name)}
window.sample.shot=async name=>{draw();const blob=await new Promise(resolve=>canvas.toBlob(resolve));await save(name+'.png',blob)};
window.sample.record=async (branch='main')=>{
 if(recording)return;
 recording=true;
 const stream=canvas.captureStream(0),mime=MediaRecorder.isTypeSupported('video/mp4')?'video/mp4':'video/webm';
 const recorder=new MediaRecorder(stream,{mimeType:mime,videoBitsPerSecond:10000000}),chunks=[];
 recorder.ondataavailable=e=>{if(e.data.size)chunks.push(e.data)};
 const finish=new Promise(resolve=>recorder.onstop=resolve);
 angle=.32;reduced=false;document.querySelector("#less").checked=false;cc=true;play(branch);recorder.start();
 const track=stream.getVideoTracks()[0];
 for(let frame=0;frame<336;frame++){
  const sampleTime=frame/24;
  if(!interrupted)time=sampleTime;
  motionTime=sampleTime;
  if(branch==='cut-build'&&sampleTime>=4.45||branch==='cut-tip'&&sampleTime>=6.3)interrupt();
  if(sampleTime>=12.2)playing=false;
  draw();
  await new Promise(resolve=>requestAnimationFrame(resolve));
  track.requestFrame();
  await new Promise(resolve=>setTimeout(resolve,1000/24));
 }
 recorder.stop();await finish;
 await save(branch+(mime==='video/mp4'?'.mp4':'.webm'),new Blob(chunks,{type:mime}));
 stream.getTracks().forEach(t=>t.stop());recording=false;return info();
};
document.querySelector('#record').onclick=async()=>{
 document.querySelector('#record').disabled=true;
 try{await sample.record('main');await sample.record('cut-build');await sample.record('cut-tip');}
 finally{document.querySelector('#record').disabled=false}
};
draw();window.voiceReady=true;requestAnimationFrame(tick);


const evidenceButton=document.createElement('button');
evidenceButton.textContent='导出验收定帧';
document.querySelector('header').append(evidenceButton);
evidenceButton.onclick=async()=>{
 evidenceButton.disabled=true;
 const frames=[];
 try{
  reduced=false;document.querySelector('#less').checked=false;
  for(const [name,t,rotation] of [['01-presence',.7,.32],['02-building',4.55,.32],['03-probe-front',6.6,0],['04-probe-side',6.6,1.05]]){
   seek(t,rotation);await sample.shot(name);
   frames.push({name,...info()});
  }
  await save('frames.json',new Blob([JSON.stringify(frames,null,2)],{type:'application/json'}));
  evidenceButton.textContent='4 张定帧已保存';seek(0);
 }finally{evidenceButton.disabled=false}
};

const verifyButton=document.createElement('button');
verifyButton.textContent='检查打断';
document.querySelector('header').append(verifyButton);
verifyButton.onclick=async()=>{
 verifyButton.disabled=true;
 const checks=[];
 const check=(passed,name)=>{checks.push({name,passed});if(!passed)throw Error(name)};
 const wait=ms=>new Promise(resolve=>setTimeout(resolve,ms));
 const until=async predicate=>{const start=performance.now();while(!predicate()){if(performance.now()-start>25000)throw Error('演示时间轴等待超时');await wait(30)}};
 try{
  reduced=false;document.querySelector('#less').checked=false;
  seek(2.7);check(probe.stats().visibleSegments===0,'开场没有设备');
  seek(4.55);const partial=probe.stats();check(partial.visibleSegments>0&&partial.visibleSegments<partial.totalSegments*.4,'中间帧为部分几何');
  seek(6.6,0);const front=info();seek(6.6,1.05);const side=info();
  check(Math.abs(front.head[0]-side.head[0])>30&&Math.abs(front.tip[1]-side.tip[1])>5,'两个视角改变头部和尖端投影');
  for(const [branch,at] of [['build',4.45],['tip',6.3]]){
   play();await until(()=>time>=at);
   // Exercise the same keyboard handler used by the visible I shortcut.
   dispatchEvent(new KeyboardEvent('keydown',{key:'i'}));
   const stopped=time;
   check(interrupted&&state().focus===0&&probe.stats().visibleSegments===0,'打断清空 '+branch);
   await wait(7600);
   check(interrupted&&time===stopped&&state().focus===0&&probe.stats().visibleSegments===0,'超过旧时间点仍无重播 '+branch);
   await sample.shot('interrupt-'+branch+'-after-wait');
  }
  reduced=true;seek(4.55);check(probe.stats().visibleSegments===probe.stats().totalSegments,'减少动效即时成像');
  seek(9.1);check(probe.stats().visibleSegments===0,'减少动效即时退场');
  reduced=false;document.querySelector('#less').checked=false;seek(0);
  verifyButton.textContent=checks.length+' 项检查通过';
 }catch(error){checks.push({error:String(error)});verifyButton.textContent='检查发现问题'}
 finally{
  await save('validation.json',new Blob([JSON.stringify({checks},null,2)],{type:'application/json'}));
  verifyButton.disabled=false;
 }
};
