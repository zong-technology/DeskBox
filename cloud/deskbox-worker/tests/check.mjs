import assert from "node:assert/strict";
import { DatabaseSync } from "node:sqlite";
import { readFileSync } from "node:fs";
import { createRequire } from "node:module";
import { fileURLToPath } from "node:url";
const require = createRequire(import.meta.url);
let esbuild; try { esbuild = require("esbuild"); } catch { esbuild = require("../../deskbox-account-sync/node_modules/esbuild"); }
const sql = new DatabaseSync(":memory:");
const schema = readFileSync(new URL("../schema.sql", import.meta.url), "utf8"); sql.exec(schema);
class Statement {
  constructor(query) { this.query = query; this.values = []; }
  bind(...values) { this.values = values; return this; }
  async first() { return sql.prepare(this.query).get(...this.values) ?? null; }
  async all() { return { results: sql.prepare(this.query).all(...this.values) }; }
  async run() { return sql.prepare(this.query).run(...this.values); }
}
const env = { DESKBOX_OWNER_PASSWORD: "a".repeat(64), DB: { prepare: query => new Statement(query), batch: async statements => { sql.exec("BEGIN"); try { const results = []; for (const statement of statements) results.push(await statement.run()); sql.exec("COMMIT"); return results; } catch (error) { sql.exec("ROLLBACK"); throw error; } } } };
const built = esbuild.buildSync({ entryPoints: [fileURLToPath(new URL("../index.ts", import.meta.url))], bundle: true, write: false, platform: "neutral", format: "esm" });
const worker = (await import("data:text/javascript;base64," + Buffer.from(built.outputFiles[0].contents).toString("base64"))).default;
let checks = 0;
function check(value) { assert(value); checks++; }
async function call(path, options = {}) { return worker.fetch(new Request("https://sync.example" + path, options), env); }
const base = { origin: "https://sync.example", "cf-connecting-ip": "test" };
check((await call("/")).status === 200);
check((await call("/")).headers.get("referrer-policy") === "same-origin");
check((await call("/login", { method: "POST", headers: { ...base, origin: "null" }, body: "password=test" })).status === 403);
check((await call("/login", { method: "POST", headers: { ...base, origin: "https://evil.example" }, body: "password=test" })).status === 403);
check((await call("/api/cloud/devices")).status === 401);
const wrong = await call("/login", { method: "POST", headers: base, body: "password=wrong" });
check(!wrong.headers.get("set-cookie"));
const login = await call("/login", { method: "POST", headers: base, body: "password=" + env.DESKBOX_OWNER_PASSWORD });
check(login.status === 303);
const cookie = login.headers.get("set-cookie").split(";")[0];
check(login.headers.get("set-cookie").includes("HttpOnly; Secure; SameSite=Strict"));
check(sql.prepare("SELECT hash FROM admin_sessions").get().hash !== cookie.split("=")[1]);
check((await call("/", { headers: { cookie } })).status === 200);
const headers = { ...base, cookie, "Content-Type": "application/json" };
async function device(name, role) { return (await (await call("/api/cloud/devices", { method: "POST", headers, body: JSON.stringify({ name, role }) })).json()).Credential; }
const desktop = await device("PC", "desktop"), phone = await device("Phone", "ios");
check(desktop?.Role === "desktop" && phone?.Role === "ios");
check(phone.GateToken === "direct-worker");
const auth = c => ({ Authorization: "Bearer " + c.Token, "X-Device-Id": c.DeviceId, "Content-Type": "application/json" });
const record = { EntityType: "task", Id: "P1", Version: 1, BaseVersion: 0, UpdatedAt: Date.now(), DeviceId: "", Deleted: false, Json: JSON.stringify({ Id: "P1", Title: "Portable cloud task" }) };
async function post(path, body, credential) { const response = await call("/api/cloud/" + path, { method: "POST", headers: auth(credential), body: JSON.stringify(body) }); return { status: response.status, value: await response.json() }; }
check((await post("push", record, phone)).value.Result === "Queued");
const pending = (await post("exchange", { Records: [], Results: [] }, desktop)).value.Pending;
check(pending.length === 1);
await post("exchange", { Records: [record], Results: [{ Id: pending[0].Id, Result: "Applied" }] }, desktop);
const writes = sql.prepare("SELECT total_changes() AS n").get().n;
await post("exchange", { Records: [record], Results: [] }, desktop);
check(sql.prepare("SELECT total_changes() AS n").get().n === writes);
check((await post("push", record, phone)).value.Result === "Applied");
check((await (await call("/api/cloud/pull", { headers: auth(phone) })).json())[0].Id === "P1");
const firstDraft={...record,Id:"draft",UpdatedAt:100,Json:JSON.stringify({Id:"draft",Title:"first"})};
const lastDraft={...firstDraft,UpdatedAt:101,Json:JSON.stringify({Id:"draft",Title:"last"})};
await post("push",firstDraft,phone);await post("push",lastDraft,phone);
const drafts=(await post("exchange",{Records:[],Results:[]},desktop)).value.Pending;
check(drafts.length===1&&JSON.parse(drafts[0].Record.Json).Title==="last");
check((await post("push",firstDraft,phone)).value.Result==="Resolved");
check(sql.prepare("SELECT COUNT(*) AS n FROM submissions WHERE result='Queued'").get().n===1);
sql.exec(schema); check(sql.prepare("SELECT COUNT(*) AS n FROM records").get().n === 1);
const jobId=crypto.randomUUID(), prompt={Id:jobId,Messages:[{Role:"user",Content:"查询今天的任务"}]};
check((await post("ai-request",prompt,phone)).status===200);
check((await post("ai-request",prompt,phone)).status===200);
check(sql.prepare("SELECT COUNT(*) AS n FROM ai_jobs").get().n===1);
check((await post("ai-request",{...prompt,Messages:[{Role:"system",Content:"bad"}]},phone)).status===400);
check((await post("ai-request",{...prompt,Messages:[null]},phone)).status===400);
check((await post("ai-request",prompt,desktop)).status===403);
const otherPhone=await device("Other phone","ios");
check((await call("/api/cloud/ai-request?id="+jobId,{headers:auth(otherPhone)})).status===404);
check((await post("ai-exchange",{Claim:true,Updates:[]},phone)).status===403);
const job=(await post("ai-exchange",{Claim:true,Updates:[]},desktop)).value.Job;
check(job.id===jobId&&JSON.parse(job.messages)[0].Content==="查询今天的任务");
check((await post("ai-exchange",{Claim:true,Updates:[]},desktop)).value.Job===null);
await post("ai-exchange",{Claim:false,Updates:[{Id:jobId,Text:"正在查询",Stage:"task.list",Status:"running",Provider:"测试",Model:"mock"}]},desktop);
let aiReply=await (await call("/api/cloud/ai-request?id="+jobId,{headers:auth(phone)})).json();
check(aiReply.response==="正在查询"&&aiReply.stage==="task.list"&&aiReply.model==="mock");
await post("ai-cancel",{Id:jobId},otherPhone);
check(sql.prepare("SELECT cancelled FROM ai_jobs WHERE id=?").get(jobId).cancelled===0);
await post("ai-cancel",{Id:jobId},phone);
check((await post("ai-exchange",{Claim:false,Updates:[]},desktop)).value.Cancelled.includes(jobId));
await post("ai-exchange",{Claim:false,Updates:[{Id:jobId,Text:"should not overwrite",Stage:"completed",Status:"completed"}]},desktop);
aiReply=await (await call("/api/cloud/ai-request?id="+jobId,{headers:auth(phone)})).json();
check(aiReply.status==="cancelled"&&aiReply.response==="正在查询");
const second=crypto.randomUUID();await post("ai-request",{...prompt,Id:second},phone);
sql.prepare("UPDATE ai_jobs SET updated=?,created=? WHERE id=?").run(Date.now()-200000,Date.now()-200000,second);
check((await (await call("/api/cloud/ai-request?id="+second,{headers:auth(phone)})).json()).status==="error");
check((await post("ai-exchange",{Claim:true,Updates:[]},desktop)).value.Job===null);
const revokeJob=crypto.randomUUID();await post("ai-request",{...prompt,Id:revokeJob},phone);
check((await call("/api/cloud/devices", { method: "POST", headers: { ...headers, origin: "https://evil.example" }, body: JSON.stringify({ revoke: phone.DeviceId }) })).status === 403);
await call("/api/cloud/devices", { method: "POST", headers, body: JSON.stringify({ revoke: phone.DeviceId }) });
check(sql.prepare("SELECT status FROM ai_jobs WHERE id=?").get(revokeJob).status==="cancelled");
check((await call("/api/cloud/pull", { headers: auth(phone) })).status === 401);
check((await call("/api/cloud/pull", { headers: auth(desktop) })).status === 200);
await call("/logout", { method: "POST", headers });
check((await call("/api/cloud/devices", { headers: { cookie } })).status === 401);
for (let i = 0; i < 10; i++) await call("/login", { method: "POST", headers: base, body: "password=wrong" });
check((await call("/login", { method: "POST", headers: base, body: "password=wrong" })).status === 429);
check(sql.prepare("SELECT COUNT(*) AS n FROM records").get().n === 1);
console.log(`Portable cloud login and sync: PASS ${checks}`);
