const http=require('node:http'),fs=require('node:fs'),path=require('node:path');
const root=__dirname;
http.createServer((req,res)=>{
 const url=new URL(req.url,'http://localhost');
 if(req.method==='POST'&&url.pathname.startsWith('/save/')){
  const name=decodeURIComponent(url.pathname.slice(6));
  if(!/^[a-z0-9_-]+\.(png|mp4|webm|json)$/.test(name)){res.writeHead(400);res.end();return}
  const stream=fs.createWriteStream(path.join(root,'evidence',name));
  req.pipe(stream);stream.on('finish',()=>res.end('saved'));return;
 }
 const target=path.resolve(root,'.'+decodeURIComponent(url.pathname==='/'?'/index.html':url.pathname));
 if(!target.startsWith(root+path.sep)){res.writeHead(403);res.end();return}
 fs.readFile(target,(error,data)=>{
  if(error){res.writeHead(404);res.end();return}
  const mime={'.js':'text/javascript','.html':'text/html','.png':'image/png','.mp4':'video/mp4','.json':'application/json'};
  res.setHeader('Content-Type',mime[path.extname(target)]||'application/octet-stream');res.end(data);
 });
}).listen(4178,'127.0.0.1',()=>console.log('http://127.0.0.1:4178'));

