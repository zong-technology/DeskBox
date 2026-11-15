
const reply = (body: unknown, status = 200) => Response.json(body, { status, headers: { "Cache-Control": "no-store" } });
const digest = async (value: string) => Array.from(new Uint8Array(await crypto.subtle.digest("SHA-256", new TextEncoder().encode(value))), b => b.toString(16).padStart(2, "0")).join("");

type RecordValue = { EntityType: string; Id: string; Version: number; BaseVersion: number; UpdatedAt: number; DeviceId: string; Deleted: boolean; Json: string };
function validRecord(value: RecordValue) {
  if (!value || !["project", "task", "incident"].includes(value.EntityType) || typeof value.Id !== "string" || !value.Id || value.Id.length > 160 || !Number.isSafeInteger(value.Version) || value.Version < 1 || !Number.isSafeInteger(value.BaseVersion) || value.BaseVersion < 0 || !Number.isSafeInteger(value.UpdatedAt) || typeof value.Json !== "string" || value.Json.length > 100000 || typeof value.Deleted !== "boolean") return false;
  if (value.Deleted) return true;
  try { return JSON.parse(value.Json).Id === value.Id; } catch { return false; }
}
export async function handleProtocol(request: Request, env: any, browser: { userId: string } | null) {
  const db = env.DB;
  const action = new URL(request.url).pathname.split("/").pop();
  if (!db) return reply({ Error: "database_unavailable" }, 503);
  if (action === "devices") {
    if (!browser) return reply({ Error: "login_required" }, 401);
    if (request.method === "GET") {
      const rows = await db.prepare("SELECT id,name,role,expires,revoked,created FROM devices WHERE owner=? ORDER BY created DESC").bind(browser.userId).all();
      return reply({ Devices: rows.results });
    }
    if (request.headers.get("origin") !== new URL(request.url).origin) return reply({ Error: "invalid_origin" }, 403);
    const body = await request.json() as { role?: string; name?: string; revoke?: string };
    if (body.revoke) {
      await db.batch([db.prepare("UPDATE devices SET revoked=1 WHERE owner=? AND id=?").bind(browser.userId, body.revoke), db.prepare("UPDATE submissions SET result='Revoked' WHERE owner=? AND device=? AND result='Queued'").bind(browser.userId, body.revoke),db.prepare("UPDATE ai_jobs SET cancelled=1,status='cancelled',stage='设备已撤销',updated=? WHERE owner=? AND device=? AND status IN ('queued','running')").bind(Date.now(),browser.userId,body.revoke)]);
      return reply({ Ok: true });
    }
    if (!["desktop", "ios"].includes(body.role || "") || !body.name?.trim() || body.name.length > 80) return reply({ Error: "invalid_device" }, 400);
    const gate = (env as unknown as Record<string, string>).DESKBOX_GATE_TOKEN;
    if (!gate) return reply({ Error: "service_not_configured" }, 503);
    const count = await db.prepare("SELECT COUNT(*) AS n FROM devices WHERE owner=? AND revoked=0 AND expires>?").bind(browser.userId, Date.now()).first<{ n: number }>();
    if ((count?.n || 0) >= 10) return reply({ Error: "device_limit" }, 409);
    if (body.role === "desktop" && await db.prepare("SELECT id FROM devices WHERE owner=? AND role='desktop' AND revoked=0 AND expires>?").bind(browser.userId, Date.now()).first()) return reply({ Error: "请先撤销原电脑，再授权新的主电脑" }, 409);
    const token = Array.from(crypto.getRandomValues(new Uint8Array(32)), b => b.toString(16).padStart(2, "0")).join("");
    const id = crypto.randomUUID(); const expires = Date.now() + 365 * 86400000;
    await db.prepare("INSERT INTO devices (id,owner,name,role,hash,expires,revoked,created) VALUES (?,?,?,?,?,?,0,?)").bind(id, browser.userId, body.name.trim(), body.role, await digest(token), expires, Date.now()).run();
    return reply({ Credential: { BaseUrl: new URL(request.url).origin, DeviceId: id, Token: token, GateToken: gate, Role: body.role, ExpiresAt: expires } });
  }
  const token = request.headers.get("authorization")?.match(/^Bearer ([a-f0-9]{64})$/)?.[1];
  const deviceId = request.headers.get("x-device-id");
  if (!token || !deviceId) return reply({ Error: "device_login_required" }, 401);
  const device = await db.prepare("SELECT id,owner,role FROM devices WHERE id=? AND hash=? AND revoked=0 AND expires>?").bind(deviceId, await digest(token), Date.now()).first<{ id: string; owner: string; role: string }>();
  if (!device) return reply({ Error: "device_revoked_or_expired" }, 401);
  if(action==="ai-request"&&request.method==="GET"&&device.role==="ios"){
    const id=new URL(request.url).searchParams.get("id");
    const job=await db.prepare("SELECT id,status,response,stage,error,provider,model,updated FROM ai_jobs WHERE id=? AND owner=? AND device=?").bind(id,device.owner,device.id).first();
    if(!job)return reply({Error:"job_not_found"},404);
    if(["queued","running"].includes(String(job.status))&&Number(job.updated)<Date.now()-180000)return reply({...job,status:"error",stage:"电脑未响应",error:"电脑未在线或已暂停响应，请打开电脑DeskBox后重新发送；已有内容保留"});
    return reply(job);
  }
  if (action === "pull" && request.method === "GET") {
    const rows = await db.prepare("SELECT payload FROM records WHERE owner=? LIMIT 10000").bind(device.owner).all<{ payload: string }>();
    return reply(rows.results.map(row => JSON.parse(row.payload)));
  }
  if (request.method !== "POST") return reply({ Error: "not_found" }, 404);
  const raw = await request.text(); if (raw.length > 2000000) return reply({ Error: "payload_too_large" }, 413);
  if(action==="ai-request"&&device.role==="ios"){
    const body=JSON.parse(raw) as {Id:string;Messages:{Role:string;Content:string}[]};
    if(!body||!/^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$/i.test(body.Id||"")||!Array.isArray(body.Messages)||body.Messages.length<1||body.Messages.length>16||body.Messages.some(m=>!m||!["user","assistant"].includes(m.Role)||typeof m.Content!=="string"||m.Content.length>16000)||JSON.stringify(body.Messages).length>60000||body.Messages[body.Messages.length-1].Role!=="user")return reply({Error:"invalid_messages"},400);
    const existing=await db.prepare("SELECT id,device FROM ai_jobs WHERE id=? AND owner=?").bind(body.Id,device.owner).first();
    if(existing)return existing.device===device.id?reply({Id:body.Id}):reply({Error:"job_not_found"},404);
    const pending=await db.prepare("SELECT COUNT(*) AS n FROM ai_jobs WHERE owner=? AND status IN ('queued','running') AND cancelled=0 AND updated>?").bind(device.owner,Date.now()-180000).first<{n:number}>();
    if((pending?.n||0)>=4)return reply({Error:"AI排队已满，请等待当前任务或取消"},429);
    const now=Date.now();await db.prepare("DELETE FROM ai_jobs WHERE owner=? AND updated<?").bind(device.owner,now-604800000).run();await db.prepare("INSERT INTO ai_jobs (id,owner,device,messages,status,response,stage,error,provider,model,cancelled,created,updated) VALUES (?,?,?,?,'queued','','等待电脑接入','','','',0,?,?)").bind(body.Id,device.owner,device.id,JSON.stringify(body.Messages),now,now).run();return reply({Id:body.Id});
  }
  if(action==="ai-cancel"&&device.role==="ios"){
    const body=JSON.parse(raw) as {Id:string};await db.prepare("UPDATE ai_jobs SET cancelled=1,status='cancelled',stage='用户已取消',updated=? WHERE owner=? AND device=? AND id=?").bind(Date.now(),device.owner,device.id,body.Id).run();return reply({Ok:true});
  }
  if(action==="ai-exchange"&&device.role==="desktop"){
    const body=JSON.parse(raw) as {Claim?:boolean;Updates?:{Id:string;Text:string;Stage:string;Status:string;Error?:string;Provider?:string;Model?:string}[]};
    if(!Array.isArray(body.Updates)||body.Updates.length>1)return reply({Error:"invalid_ai_updates"},400);
    for(const update of body.Updates){if(typeof update.Text!=="string"||update.Text.length>100000||typeof update.Stage!=="string"||update.Stage.length>500||!["running","completed","error","cancelled"].includes(update.Status))return reply({Error:"invalid_ai_update"},400);
      await db.prepare("UPDATE ai_jobs SET response=?,stage=?,status=?,error=?,provider=?,model=?,updated=? WHERE owner=? AND id=? AND cancelled=0 AND status='running'").bind(update.Text,update.Stage,update.Status,String(update.Error||"").slice(0,1000),String(update.Provider||"").slice(0,100),String(update.Model||"").slice(0,150),Date.now(),device.owner,update.Id).run();}
    const cancelled=await db.prepare("SELECT id FROM ai_jobs WHERE owner=? AND cancelled=1 AND updated>?").bind(device.owner,Date.now()-86400000).all<{id:string}>();
    let job=null;if(body.Claim){job=await db.prepare("UPDATE ai_jobs SET status='running',stage='电脑已接收，准备模型上下文',updated=? WHERE id=(SELECT j.id FROM ai_jobs j JOIN devices d ON d.id=j.device AND d.revoked=0 AND d.expires>? WHERE j.owner=? AND j.status='queued' AND j.cancelled=0 AND j.created>? ORDER BY j.created LIMIT 1) AND status='queued' RETURNING id,messages").bind(Date.now(),Date.now(),device.owner,Date.now()-180000).first();}
    return reply({Job:job,Cancelled:cancelled.results.map(x=>x.id)});
  }
  if (action === "push" && device.role === "ios") {
    const record = JSON.parse(raw) as RecordValue;
    if (!validRecord(record) || record.EntityType === "project") return reply({ Ok: false, Result: "Invalid" }, 400);
    record.DeviceId = device.id;
    const id = await digest(JSON.stringify([device.id, record.EntityType, record.Id, record.Version, record.UpdatedAt, record.Deleted, record.Json]));
    const prior = await db.prepare("SELECT result FROM submissions WHERE id=? AND owner=?").bind(id, device.owner).first<{ result: string }>();
    if (prior && prior.result !== "Conflict") return reply({ Ok: prior.result !== "Queued", Result: prior.result });
    const count = await db.prepare("SELECT COUNT(*) AS n FROM submissions WHERE owner=? AND result='Queued'").bind(device.owner).first<{ n: number }>();
    if ((count?.n || 0) >= 1000) return reply({ Error: "queue_full" }, 429);
    const same = "owner=? AND device=? AND json_extract(payload,'$.EntityType')=? AND json_extract(payload,'$.Id')=? AND json_extract(payload,'$.Version')=? AND json_extract(payload,'$.BaseVersion')=?";
    const newer = await db.prepare("SELECT id FROM submissions WHERE " + same + " AND json_extract(payload,'$.UpdatedAt')>? LIMIT 1").bind(device.owner,device.id,record.EntityType,record.Id,record.Version,record.BaseVersion,record.UpdatedAt).first();
    if(newer){await db.prepare("UPDATE submissions SET result='Resolved' WHERE owner=? AND id=?").bind(device.owner,id).run();return reply({Ok:true,Result:"Resolved"});}
    await db.batch([
      db.prepare("UPDATE submissions SET result='Resolved' WHERE " + same + " AND result='Queued' AND json_extract(payload,'$.UpdatedAt')<?").bind(device.owner,device.id,record.EntityType,record.Id,record.Version,record.BaseVersion,record.UpdatedAt),
      db.prepare("INSERT INTO submissions (id,owner,device,payload,result,created) VALUES (?,?,?,?,'Queued',?) ON CONFLICT(id) DO UPDATE SET result='Queued'").bind(id, device.owner, device.id, JSON.stringify(record), Date.now()),
    ]);
    return reply({ Ok: false, Result: "Queued" });
  }
  if (action === "exchange" && device.role === "desktop") {
    const body = JSON.parse(raw) as { Records: RecordValue[]; Results: { Id: string; Result: string }[] };
    if (!Array.isArray(body.Records) || body.Records.length > 500 || body.Records.some(record => !validRecord(record)) || !Array.isArray(body.Results) || body.Results.length > 100) return reply({ Error: "invalid_exchange" }, 400);
    const total = await db.prepare("SELECT COUNT(*) AS n FROM records WHERE owner=?").bind(device.owner).first<{ n: number }>();
    if ((total?.n || 0) + body.Records.length > 10500) return reply({ Error: "record_limit" }, 429);
    const statements = body.Records.map(record => db.prepare("INSERT INTO records (owner,entity,id,version,payload) VALUES (?,?,?,?,?) ON CONFLICT(owner,entity,id) DO UPDATE SET version=excluded.version,payload=excluded.payload WHERE excluded.version>records.version OR (excluded.version=records.version AND excluded.payload<>records.payload)").bind(device.owner, record.EntityType, record.Id, record.Version, JSON.stringify(record)));
    for (const result of body.Results) {
      if (!["Applied", "Duplicate", "Resolved", "Conflict", "IgnoredOlder", "Invalid"].includes(result.Result)) return reply({ Error: "invalid_result" }, 400);
      statements.push(db.prepare("UPDATE submissions SET result=? WHERE owner=? AND id=?").bind(result.Result, device.owner, result.Id));
    }
    // Keep each D1 transaction bounded; acknowledgement is submitted only after its resulting snapshot.
    for (let i = 0; i < statements.length; i += 80) await db.batch(statements.slice(i, i + 80));
    const pending = await db.prepare("SELECT id,payload FROM submissions WHERE owner=? AND result='Queued' ORDER BY created LIMIT 100").bind(device.owner).all<{ id: string; payload: string }>();
    return reply({ Pending: pending.results.map(row => ({ Id: row.id, Record: JSON.parse(row.payload) })) });
  }
  return reply({ Error: "forbidden" }, 403);
}
