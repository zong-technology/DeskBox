import { handleProtocol } from "./protocol";

const headers = { "Cache-Control": "no-store", "X-Content-Type-Options": "nosniff", "Referrer-Policy": "same-origin", "Content-Security-Policy": "default-src 'self'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; frame-ancestors 'none'; base-uri 'none'" };
const json = (value: unknown, status = 200) => Response.json(value, { status, headers });
const hash = async (text: string) => Array.from(new Uint8Array(await crypto.subtle.digest("SHA-256", new TextEncoder().encode(text))), b => b.toString(16).padStart(2, "0")).join("");
const random = () => Array.from(crypto.getRandomValues(new Uint8Array(32)), b => b.toString(16).padStart(2, "0")).join("");
const html = (content: string) => new Response(`<!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>DeskBox 私人云同步</title><style>body{margin:0;background:#071324;color:#e8f4ff;font:16px system-ui}main{max-width:850px;margin:auto;padding:36px 20px}section{background:#101f34;border:1px solid #24405a;border-radius:18px;padding:24px;margin:22px 0}input,select,button,a.action,textarea{box-sizing:border-box;background:#123449;color:#e8f4ff;border:1px solid #2a728c;border-radius:9px;padding:12px;font:inherit;margin:6px 0}button,a{cursor:pointer}a{color:#67e8f9}label{display:block;margin:12px 0}textarea{width:100%;font:12px monospace}small,p{color:#afc4db;line-height:1.8}.row{display:flex;gap:12px;flex-wrap:wrap;align-items:center}h1{font-size:32px}h2{font-size:21px}</style><main><small>ARTHUR.ZHONG / DESKBOX CLOUD</small>${content}</main></html>`, { headers: { ...headers, "Content-Type": "text/html; charset=utf-8" } });

async function session(request: Request, env: any) {
  const value = request.headers.get("cookie")?.match(/(?:^|;\s*)deskbox_session=([a-f0-9]{64})(?:;|$)/)?.[1];
  return value && await env.DB.prepare("SELECT hash FROM admin_sessions WHERE hash=? AND expires>?").bind(await hash(value), Date.now()).first() ? { userId: "owner" } : null;
}

export default {
  async fetch(request: Request, env: any) {
    try {
      if (!env.DB || !env.DESKBOX_OWNER_PASSWORD || env.DESKBOX_OWNER_PASSWORD.length < 32) return json({ Error: "请先运行部署脚本配置数据库和随机登录密码" }, 503);
      const url = new URL(request.url);
      if (request.method === "POST" && request.headers.get("origin") && request.headers.get("origin") !== url.origin) return json({ Error: "invalid_origin" }, 403);
      if (url.pathname === "/login" && request.method === "POST") {
        const raw = await request.text(); if (raw.length > 1000) return json({ Error: "invalid_login" }, 400);
        const ip = await hash((request.headers.get("cf-connecting-ip") || "local") + env.DESKBOX_OWNER_PASSWORD);
        const window = Math.floor(Date.now() / 900000);
        await env.DB.prepare("DELETE FROM login_attempts WHERE window<?").bind(window - 1).run();
        await env.DB.prepare("INSERT INTO login_attempts (id,window,count) VALUES (?,?,1) ON CONFLICT(id) DO UPDATE SET count=CASE WHEN window=excluded.window THEN count+1 ELSE 1 END,window=excluded.window").bind(ip, window).run();
        const attempts = await env.DB.prepare("SELECT count FROM login_attempts WHERE id=?").bind(ip).first();
        if (attempts.count > 10) return json({ Error: "尝试次数过多，请15分钟后重试" }, 429);
        const supplied = await hash(new URLSearchParams(raw).get("password") || ""); const expected = await hash(env.DESKBOX_OWNER_PASSWORD);
        let difference = 0; for (let i = 0; i < expected.length; i++) difference |= expected.charCodeAt(i) ^ supplied.charCodeAt(i);
        if (difference !== 0) return html('<h1>登录未成功</h1><p>请使用部署脚本生成的私人云登录密码。</p><a href="/">返回重试</a>');
        const token = random(); await env.DB.prepare("DELETE FROM admin_sessions WHERE expires<?").bind(Date.now()).run();
        await env.DB.prepare("INSERT INTO admin_sessions (hash,expires) VALUES (?,?)").bind(await hash(token), Date.now() + 86400000).run();
        return new Response(null, { status: 303, headers: { ...headers, Location: "/", "Set-Cookie": `deskbox_session=${token}; HttpOnly; Secure; SameSite=Strict; Path=/; Max-Age=86400` } });
      }
      const identity = await session(request, env);
      if (url.pathname.startsWith("/api/cloud/")) return await handleProtocol(request, { ...env, DESKBOX_GATE_TOKEN: "direct-worker" }, identity);
      if (url.pathname === "/logout" && request.method === "POST") {
        const token = request.headers.get("cookie")?.match(/deskbox_session=([a-f0-9]{64})/)?.[1];
        if (token) await env.DB.prepare("DELETE FROM admin_sessions WHERE hash=?").bind(await hash(token)).run();
        return new Response(null, { status: 303, headers: { ...headers, Location: "/", "Set-Cookie": "deskbox_session=; HttpOnly; Secure; SameSite=Strict; Path=/; Max-Age=0" } });
      }
      if (url.pathname !== "/") return json({ Error: "not_found" }, 404);
      if (!identity) return html('<h1>私人云账号登录</h1><p>电脑与手机共用此私人工作空间，无需同一Wi-Fi或VPN。</p><section><form method="post" action="/login"><label>账号<input value="owner" readonly autocomplete="username"></label><label>登录密码<input type="password" name="password" required autocomplete="current-password" style="width:100%"></label><button type="submit">登录 DeskBox 云账号</button></form></section><p>使用部署时生成的随机密码。不要把此密码或设备授权发到公开群组。</p>');
      return html(`<h1>工程现场，随时连接</h1><p>已登录私人账号 owner。首次分别授权 Windows 主电脑与 iPhone，之后由应用自动同步。</p><section><h2>授权设备</h2><div class="row"><select id="role"><option value="ios">iPhone</option><option value="desktop">Windows 主电脑</option></select><input id="name" maxlength="80" placeholder="设备名称"><button id="create">生成设备授权</button></div><p id="message" role="status"></p><div id="grant" hidden><textarea id="credential" rows="5" readonly aria-label="连接凭证"></textarea><div class="row"><button id="copy">复制连接凭证</button><a class="action" id="open">导入此 iPhone</a><button id="hide">隐藏凭证</button></div></div></section><section><h2>已授权设备</h2><div id="devices"></div></section><p>电脑保持 DeskBox 运行。电脑离线时手机可读上次快照，新修改保留队列、等待电脑上线确认。当前支持一台主电脑和多台手机。</p><form action="/logout" method="post"><button>退出网页账号</button></form>
<script>
const el=id=>document.getElementById(id);let busy=false;
async function api(body){const r=await fetch('/api/cloud/devices',body?{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(body)}:{});const d=await r.json();if(!r.ok)throw Error(d.Error||'请求失败');return d}
async function refresh(){const d=await api();el('devices').replaceChildren();for(const x of d.Devices){const row=document.createElement('div');row.className='row';const text=document.createElement('p');text.textContent=x.name+' · '+x.role+' · '+(x.revoked?'已撤销':x.expires<Date.now()?'已过期':'已授权');row.append(text);if(!x.revoked){const b=document.createElement('button');b.textContent='撤销';b.onclick=async()=>{if(confirm('撤销后该设备将停止云同步，是否继续？'))await submit({revoke:x.id})};row.append(b)}el('devices').append(row)}}
async function submit(body){if(busy)return;busy=true;el('create').disabled=true;el('grant').hidden=true;el('credential').value='';try{const d=await api(body);if(d.Credential){const c=JSON.stringify(d.Credential);el('credential').value=c;el('grant').hidden=false;el('open').hidden=d.Credential.Role!=='ios';el('open').href='deskbox://cloud?data='+encodeURIComponent(btoa(c));el('message').textContent='仅本次显示，请导入自己的设备。'}await refresh()}catch(e){el('message').textContent=e.message}finally{busy=false;el('create').disabled=false}}
el('create').onclick=()=>{const name=el('name').value.trim();if(name)submit({role:el('role').value,name});else el('message').textContent='请填写设备名称'};
el('copy').onclick=async()=>{try{await navigator.clipboard.writeText(el('credential').value);el('message').textContent='已复制，请粘贴到对应应用的账号云同步设置'}catch{el('message').textContent='复制失败，请手动选择上方内容复制'}};
el('hide').onclick=()=>{el('credential').value='';el('grant').hidden=true};refresh().catch(e=>el('message').textContent=e.message);
</script>`);
    } catch { return json({ Error: "服务暂不可用，请检查部署配置" }, 500); }
  }
};
