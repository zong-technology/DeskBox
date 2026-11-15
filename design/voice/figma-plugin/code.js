async function buildVoice(){

const page = await figma.getNodeByIdAsync("1:2");
await figma.setCurrentPageAsync(page);
const sectionName = "V4.1 · 实时语音 · 光丝与语义投影";
if (page.children.some(n=>n.type==="SECTION" && n.name===sectionName))
  return {existing:true,id:page.children.find(n=>n.type==="SECTION"&&n.name===sectionName).id};
const available=await figma.listAvailableFontsAsync();
function font(families,style){
 for(const family of families){const f=available.find(x=>x.fontName.family===family&&x.fontName.style===style);if(f)return f.fontName;}
 throw Error("Required font style unavailable: "+style);
}
const regular=font(["Noto Sans SC","Noto Sans CJK SC","Microsoft YaHei UI","Inter"],"Regular");
const bold=font([regular.family,"Noto Sans SC","Inter"],"Bold");
const latin=font(["Inter",regular.family],"Regular");
await Promise.all([regular,bold,latin].map(f=>figma.loadFontAsync(f)));
const vars=Object.fromEntries((await figma.variables.getLocalVariablesAsync()).map(v=>[v.name,v]));
const ink={black:"#02070B",white:"#EDF7FC",cyan:"#61E6F3",muted:"#829AA5",amber:"#FFBE73",red:"#F97478",border:"#28404C"};
const ids=[];
const mark=n=>{ids.push(n.id);return n;};
function color(hex){return {r:parseInt(hex.slice(1,3),16)/255,g:parseInt(hex.slice(3,5),16)/255,b:parseInt(hex.slice(5,7),16)/255};}
function paint(hex,opacity=1,token){
 let p={type:"SOLID",color:color(hex),opacity};
 return token&&vars[token] ? figma.variables.setBoundVariableForPaint(p,"color",vars[token]) : p;
}
function append(parent,node,x,y){parent.appendChild(node);node.x=x;node.y=y;return mark(node);}
function box(parent,name,x,y,w,h,fill,stroke,rad=0){
 const n=append(parent,figma.createRectangle(),x,y);n.name=name;n.resize(w,h);n.cornerRadius=rad;
 n.fills=fill?[paint(fill)]:[];if(stroke){n.strokes=[paint(stroke)];n.strokeWeight=1;}return n;
}
function txt(parent,name,value,x,y,size,fill=ink.white,f=regular,token){
 const n=append(parent,figma.createText(),x,y);n.name=name;n.fontName=f;n.fontSize=size;
 n.characters=value;n.fills=[paint(fill,1,token)];return n;
}
function svg(parent,name,x,y,content,w,h){
 const n=append(parent,figma.createNodeFromSvg('<svg xmlns="http://www.w3.org/2000/svg" width="'+w+'" height="'+h+'" viewBox="0 0 '+w+' '+h+'">'+content+'</svg>'),x,y);
 n.name=name;n.clipsContent=false;return n;
}
const line=(d,c,w=1,o=1)=>'<path d="'+d+'" fill="none" stroke="'+c+'" stroke-width="'+w+'" opacity="'+o+'" stroke-linecap="round" stroke-linejoin="round"/>';
const dot=(x,y,r,c,o)=>'<circle cx="'+x+'" cy="'+y+'" r="'+r+'" fill="'+c+'" opacity="'+o+'"/>';
const fmt=x=>x.toFixed(1);
const paths=[
 [40,330,232,200,349,201,524,308,708,417,906,424,1182,206],
 [70,395,254,385,264,174,515,303,744,424,900,298,1150,312],
 [132,484,326,542,400,458,529,312,661,158,823,193,1137,278],
 [132,223,253,49,408,134,530,312,665,504,853,564,1043,290],
 [149,404,322,329,373,81,551,233,711,372,865,376,1130,203],
 [76,446,282,410,416,500,544,344,625,235,794,223,1152,362],
 [100,278,220,301,375,448,520,322,687,158,818,111,1098,213]
];
let strands="",particles="",glow="";
for(let k=0;k<paths.length;k++){
 const p=paths[k],gold=k===2||k===5;
 for(let j=-6;j<=6;j++){
  const d="M "+p.map((v,i)=>fmt(v+(i%2?j*(i<6?4.5:-3.5):j*1.4))).reduce((a,v,i)=>a+(i===2||i===8?" C ":" ")+v);
  strands+=line(d,gold?ink.amber:ink.cyan,j===0?2.1:.8,j===0?.86:.12+(6-Math.abs(j))*.037);
  if(j===0) glow+=line(d,gold?"#915222":"#14778C",10,.15);
 }
}
for(let i=0;i<135;i++){
 const t=i*.71,r=30+(i*79)%350;
 const x=610+Math.cos(t)*r*1.34,y=310+Math.sin(t*1.3)*r*.61;
 particles+=dot(fmt(x),fmt(y),i%9===0?1.8:.9,i%6===0?ink.amber:ink.cyan,.19+(i%5)*.09);
}
const aiArt=glow+strands+particles+
 '<defs><radialGradient id="nucleus"><stop stop-color="#DFFBFF" stop-opacity=".9"/><stop offset=".28" stop-color="#72E7F5" stop-opacity=".37"/><stop offset="1" stop-color="#137284" stop-opacity="0"/></radialGradient></defs>'+
 '<ellipse cx="535" cy="320" rx="115" ry="85" fill="url(#nucleus)"/>'+
 line("M 449 281 C 496 298 514 305 535 320 C 559 335 585 348 650 366","#C7FAFF",2,.72);
let probe="";
for(let y=88;y<=588;y+=13)probe+=line("M 105 "+y+" L 410 "+y,"#337283",.6,.18);
const geometry=[
 "M 104 127 C 90 82 128 45 230 36 C 319 28 368 56 377 99 L 390 152 C 382 185 335 211 242 217 C 163 222 113 198 111 171 Z",
 "M 104 127 C 147 149 320 126 377 99",
 "M 111 171 C 166 194 331 164 390 152",
 "M 143 201 L 151 254 C 166 279 310 277 327 249 L 338 201",
 "M 329 215 L 373 210 L 398 223 L 398 252 L 365 266 L 324 260",
 "M 186 270 L 186 324 L 213 344 L 277 344 L 294 323 L 294 270",
 "M 213 344 L 213 392 L 226 407 L 267 407 L 278 391 L 278 344",
 "M 232 407 L 232 567 Q 247 600 261 567 L 261 407"
];
for(const d of geometry) probe+=line(d,"#176D86",11,.18)+line(d,"#A5F6FF",2.4,.88);
for(let j=0;j<9;j++){
 const x=122+j*28;
 probe+=line("M "+x+" "+(109-j*5)+" Q "+(x+6)+" "+(70-j*2)+" "+(x+18)+" "+(55+j*2)+" M "+x+" "+(129-j*3)+" L "+(x+9)+" "+(186-j*2),ink.cyan,1.1,.56);
}
for(let j=0;j<7;j++) probe+=line("M 219 "+(351+j*6)+" L 274 "+(347+j*6),ink.cyan,.8,.63);
for(let j=0;j<55;j++){const x=100+(j*67)%308,y=48+(j*83)%535;probe+=dot(x,y,j%6===0?1.7:1, j%8===0?ink.amber:ink.cyan,.33);}
const beamArt=Array.from({length:12},(_,i)=>{
 const d="M 24 "+(350+i*3)+" C "+(240+i*5)+" "+(453-i*6)+" 375 "+(145+i*3)+" 680 "+(92+i*4);
 return line(d,i%4===0?ink.amber:ink.cyan,i%5===0?2.2:.85,.14+(i%5)*.10);
}).join("");
const section=mark(figma.createSection());
section.name=sectionName;
section.resizeWithoutConstraints(12200,5300);
section.x=Math.max(0,...page.children.filter(n=>n.id!==section.id).map(n=>n.x+n.width))+360;
section.y=0;
const template=append(section,figma.createFrame(),52,110);
template.name="01 · 聆听";template.resize(1920,1080);template.clipsContent=true;template.fills=[paint(ink.black)];
box(template,"边界",16,16,1888,1048,null,ink.border,24);
txt(template,"品牌","DESKBOX",65,51,24,ink.white,bold,"color/text");
txt(template,"实时","LIVE",214,52,22,ink.cyan,latin,"color/cyan");
txt(template,"工作区","WORKBENCH",292,52,22,ink.white,latin,"color/text");
box(template,"顶部分隔线",505,68,1040,1,ink.border,null);
const lamp=append(template,figma.createEllipse(),1583,62);lamp.name="在线灯";lamp.resize(10,10);lamp.fills=[paint(ink.cyan,1,"color/cyan")];
txt(template,"连接状态","LIVE   /   CONNECTED",1603,52,19,ink.muted,latin,"color/muted");
const presence=svg(template,"AI 本体 / 光丝与粒子",360,165,aiArt,1200,630);
const beam=svg(template,"成像光丝 / 连续轨迹",490,340,beamArt,715,480);beam.opacity=0;
const object=svg(template,"温度探头 / 可编辑示意线框",1040,190,probe,480,610);object.opacity=0;
const hi=svg(template,"探头感温端 / 局部高亮",1040,190,
 line("M 232 501 L 232 567 Q 247 600 261 567 L 261 501","#FFBE73",8,.25)+
 line("M 232 514 L 232 567 Q 247 600 261 567 L 261 514","#FFD8A7",2.8,.98)+
 line("M 252 582 L 410 536 L 462 536","#FFBE73",1.6,.8)+dot(251,582,4,ink.amber,1),480,610);hi.opacity=0;
const leader=svg(template,"锚定线 / TT-101",1360,229,line("M 54 101 L 145 37 L 424 37",ink.amber,1.5,.77)+dot(54,101,4,ink.amber,1),430,130);leader.opacity=0;
const tag=txt(template,"探头标注","TT-101 / 温度探头 · 示意",1514,229,21,ink.white);tag.opacity=0;
const highlightLabel=txt(template,"高亮标注","感温端 · 示意",1507,702,18,ink.amber);highlightLabel.opacity=0;
const caption=txt(template,"字幕 / 固定坐标","正在聆听",358,869,28,ink.white,regular,"color/text");
caption.textAutoResize="NONE";caption.resize(1204,50);caption.textAlignHorizontal="CENTER";caption.textAlignVertical="CENTER";
box(template,"字幕下分隔线 · 左",86,951,620,1,ink.border,null);
box(template,"字幕下分隔线 · 右",1214,951,620,1,ink.border,null);
const footer=append(template,figma.createFrame(),744,946);footer.name="固定控制栏";footer.layoutMode="HORIZONTAL";footer.itemSpacing=28;footer.fills=[];
footer.primaryAxisSizingMode="AUTO";footer.counterAxisSizingMode="AUTO";
const glyphs=[
 line("M 29 15 C 21 15 21 21 21 27 C 21 34 37 34 37 27 L 37 21 C 37 17 33 15 29 15 M 17 27 C 17 45 41 45 41 27 M 29 42 L 29 48 M 22 48 L 36 48",ink.cyan,2.4),
 line("M 24 21 L 24 40 M 36 21 L 36 40",ink.white,3),
 line("M 18 20 L 42 20 L 42 40 L 18 40 Z M 28 26 L 23 26 L 23 34 L 28 34 M 38 26 L 33 26 L 33 34 L 38 34",ink.white,1.7),
 line("M 17 34 C 19 22 41 22 43 34 M 17 34 L 24 36 M 43 34 L 36 36",ink.red,3.8)
];
const controls=["麦克风 / 打断","暂停 / 恢复","字幕","结束"];
for(let i=0;i<4;i++){
 const button=svg(footer,controls[i],0,0,'<circle cx="30" cy="30" r="28" fill="#061019" stroke="'+(i===0?ink.cyan:i===3?"#763840":ink.border)+'" stroke-width="1.5"/>'+glyphs[i],60,60);
 button.name=controls[i];
}
footer.x=798;
const low=txt(template,"减少动效入口","减少动效",1685,974,17,ink.muted,regular,"color/muted");
const cue=txt(template,"交互提示","点击麦克风可打断",78,976,17,ink.muted,regular,"color/muted");
const phases=[
 ["01 · 聆听","正在聆听",0,0,0,0,1,360,165],
 ["02 · 开始回答","先看一下这个测点。",0,0,0,0,1,376,156],
 ["03 · 提及温度探头 / 成像","这是温度探头。",.68,.61,0,0,.55,229,390],
 ["04 · 感温端高亮","探头下端是感温位置。",1,.86,1,1,.48,230,434],
 ["05 · 切回闲聊 / 投影消散","说回刚才的话题吧。",.22,.34,0,0,.76,290,276],
 ["06 · 本体回中","我在听，你可以继续说。",0,0,0,0,1,360,165],
 ["07 · 已打断 / 聆听","已打断 · 正在聆听",0,0,0,0,1,360,165],
 ["08 · 暂停","已暂停 · 点击暂停键恢复",0,0,0,0,1,360,165],
 ["09 · 会话结束","会话已结束",0,0,0,0,1,360,165]
];
function configure(frame,phase,reduced){
 const [,c,ob,trail,focus,caption2,scale,x,y]=phase;
 const lookup=n=>frame.children.find(x=>x.name===n);
 lookup(caption.name).characters=c;
 const body=lookup(presence.name);
 if(!reduced && scale!==1)body.rescale(scale);
 body.x=reduced?360:x;body.y=reduced?165:y;
 if(reduced&&ob>0){body.rescale(.54);body.x=238;body.y=420;}
 lookup(object.name).opacity=reduced&&ob>0?1:ob;
 lookup(beam.name).opacity=reduced?0:trail;
 lookup(leader.name).opacity=ob>0?ob:0;
 lookup(tag.name).opacity=ob>0?ob:0;
 lookup(hi.name).opacity=focus;lookup(highlightLabel.name).opacity=caption2;
 if(reduced)lookup(low.name).characters="标准动效";
}
const main=[],reduced=[];
for(let branch=0;branch<2;branch++)for(let i=0;i<phases.length;i++){
 const f=branch===0&&i===0 ? template : mark(template.clone());
 if(f!==template)section.appendChild(f);
 f.name=(branch?"减少动效 / ":"标准动效 / ")+phases[i][0];
 f.x=52+(i%6)*2012;
 f.y=110+(branch*2+Math.floor(i/6))*1230;
 (branch?reduced:main).push(f);
 if(f!==template) for(const node of f.findAll())ids.push(node.id);
}
for(let i=0;i<phases.length;i++){configure(main[i],phases[i],false);configure(reduced[i],phases[i],true);}
const go=(target,trigger,reducedMode=false)=>({trigger,actions:[{type:"NODE",destinationId:target.id,navigation:"NAVIGATE",
 transition:reducedMode?null:{type:"SMART_ANIMATE",easing:{type:"EASE_IN_AND_OUT"},duration:.55}}]});
for(const [branch,set] of [main,reduced].entries()){
 for(const [i,f] of set.entries()){
  const hits=f.children.find(n=>n.name==="固定控制栏").children;
  await hits[0].setReactionsAsync([go(set[6],{type:"ON_CLICK"},true)]);
  await hits[1].setReactionsAsync([go(set[i===7?0:7],{type:"ON_CLICK"},true)]);
  await hits[3].setReactionsAsync([go(set[8],{type:"ON_CLICK"},true)]);
  const motionToggle=f.children.find(n=>n.name==="减少动效入口");
  await motionToggle.setReactionsAsync([go((branch?main:reduced)[0],{type:"ON_CLICK"},true)]);
  if(i<=4)await f.setReactionsAsync([go(set[i+1],{type:"AFTER_TIMEOUT",timeout:[1.5,1.8,1.1,2.5,1.7][i]},branch===1),
    go(set[6],{type:"ON_KEY_DOWN",device:"KEYBOARD",keyCodes:[73]},true)]);
 }
}
const handoff=append(section,figma.createFrame(),52,5055);handoff.name="开发交接 / 实际播放时钟";handoff.resize(1920,195);handoff.fills=[paint("#0C1822")];
txt(handoff,"交接标题","生产交接 · 音频与语义事件",30,19,27,ink.white,bold);
txt(handoff,"时间基准","按实际扬声器播放时间触发语义事件。打断时清除上一轮尚未触发的投影和高亮。",30,69,20);
txt(handoff,"信号基准","本体光丝、亮度由实际麦克风输入或播放音频驱动。无可靠资产标注「示意」；无模型回退到本体与文字。",30,112,20);
let registered=false;
try{page.flowStartingPoints=[...page.flowStartingPoints,{nodeId:main[0].id,name:"DeskBox 实时语音 · 主线"},{nodeId:main[3].id,name:"DeskBox 实时语音 · 打断验证"},{nodeId:reduced[0].id,name:"DeskBox 实时语音 · 减少动效"}];registered=true;}catch(e){}
return {sectionId:section.id,main:main.map(f=>f.id),reduced:reduced.map(f=>f.id),handoffId:handoff.id,registered,createdNodeIds:ids};

}
figma.showUI(__html__,{width:440,height:280});
let completed;
figma.ui.onmessage=async msg=>{
 if(msg.type!=="build")return;
 try{completed=await buildVoice();
 if(completed.existing){const section=await figma.getNodeByIdAsync(completed.id); completed={sectionId:section.id,main:section.children.filter(n=>n.name.startsWith("标准动效 /")).map(n=>n.id),reduced:section.children.filter(n=>n.name.startsWith("减少动效 /")).map(n=>n.id),existing:true};}
 const page=figma.currentPage; const section=await figma.getNodeByIdAsync(completed.sectionId);
 page.selection=[section];figma.viewport.scrollAndZoomIntoView([section]);
 figma.ui.postMessage({type:"ready",result:completed});}
 catch(error){figma.ui.postMessage({type:"error",message:String(error)});}
};
