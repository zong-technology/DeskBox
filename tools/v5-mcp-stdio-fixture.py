"""Protocol test fixture. Uses real subprocess pipes; no engineering or production data."""
import json
import sys

sys.stdin.reconfigure(encoding="utf-8")
sys.stdout.reconfigure(encoding="utf-8")

for raw in sys.stdin:
    request = json.loads(raw)
    method = request["method"]
    if method == "notifications/initialized":
        continue
    if method == "initialize":
        result = {
            "protocolVersion": "2024-11-05",
            "serverInfo": {"name": "DeskBox test subprocess", "version": "1"},
            "capabilities": {"tools": {}},
        }
    elif method == "tools/list":
        result = {"tools": [
            {"name": "echo", "inputSchema": {"type": "object", "properties": {"text": {"type": "string"}}}}
        ]}
    elif method == "tools/call":
        result = {"content": [{"type": "text", "text": request["params"]["arguments"]["text"]}]}
    else:
        raise ValueError(method)
    print(json.dumps({"jsonrpc": "2.0", "id": request["id"], "result": result}, ensure_ascii=False), flush=True)
