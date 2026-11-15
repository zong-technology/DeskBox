// The existing private Site service credential is supplied via hidden stdin, never a file or argument.
const origin = "https://deskbox-account-sync.inwardchime.chatgpt.site";
if (process.stdin.isTTY) process.stdin.setRawMode(true);
process.stdout.write("Ready for private service credential on stdin (hidden).\n");
let input = "";
process.stdin.setEncoding("utf8");
process.stdin.on("data", async chunk => {
  input += chunk;
  if (!/[\r\n]/.test(input)) return;
  process.stdin.pause();
  const token = input.trim(); input = "";
  try {
    const headers = { "OAI-Sites-Authorization": "Bearer " + token };
    const admin = await fetch(origin + "/api/cloud/devices", { headers, redirect: "manual", signal: AbortSignal.timeout(20000) });
    const invalid = await fetch(origin + "/api/cloud/pull", { headers: { ...headers, Authorization: "Bearer " + "a".repeat(64), "X-Device-Id": "unregistered-test-device" }, redirect: "manual", signal: AbortSignal.timeout(20000) });
    if (!invalid.headers.get("content-type")?.includes("application/json")) {
      const page = await invalid.text(); const title = page.match(/<title>([^<]*)<\/title>/i)?.[1] ?? "Non-JSON response";
      throw new Error(`Live API check blocked: HTTP ${invalid.status}, ${title}; admin HTTP ${admin.status}`);
    }
    const body = await invalid.json();
    if (admin.status !== 401 || invalid.status !== 401 || body.Error !== "device_revoked_or_expired") throw new Error("Cloud authentication or schema check did not pass");
    console.log("PASS live private cloud: service access supplies no browser identity; unregistered device rejected; D1 schema responds");
    process.exit(0);
  } catch (error) { console.error(error.message); process.exit(1); }
});
