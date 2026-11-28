"""Validate the draft with an in-process fixture. No network or DeskBox data access."""

from copy import deepcopy
from datetime import datetime
from itertools import product
import json
from pathlib import Path
import unittest

from jsonschema import Draft202012Validator, FormatChecker, ValidationError


CONTRACT = Path(__file__).resolve().parents[1] / "protocol" / "eco-deskbox"
SCHEMA = json.loads((CONTRACT / "contract.schema.json").read_text(encoding="utf-8"))
EXAMPLES = json.loads((CONTRACT / "examples.json").read_text(encoding="utf-8"))
ECHO = json.loads((CONTRACT / "example-capability.schema.json").read_text(encoding="utf-8"))
VERSION = SCHEMA["$defs"]["version"]["const"]
CLOCK = "2026-01-01T00:00:01Z"


def validator(definition=None, schema=SCHEMA):
    target = schema if definition is None else {"$defs": schema["$defs"], "$ref": f"#/$defs/{definition}"}
    return Draft202012Validator(target, format_checker=FormatChecker())


def request(operation, request_id, params):
    return {
        "kind": "request",
        "protocolVersion": VERSION,
        "requestId": request_id,
        "callerId": "fixture-eco",
        "timestamp": CLOCK,
        "operation": operation,
        "params": params,
    }


class TaskFixture:
    """Synthetic task endpoint with an injected principal and a manually advanced worker."""

    def __init__(self):
        self.grants = {"fixture-eco": {"fixture-project-a"}}
        self.accepted = {}
        self.tasks = {}

    def handle(self, message, principal):
        validator("request").validate(message)
        operation = message["operation"]
        params = message["params"]
        response = {
            "kind": "response",
            "protocolVersion": VERSION,
            "requestId": message["requestId"],
            "serviceId": "fixture-deskbox",
            "timestamp": CLOCK,
            "operation": operation,
        }
        key = (principal, operation, message["requestId"])
        deadline = message.get("deadline")

        if not principal or principal != message["callerId"]:
            response["error"] = self.error("unauthenticated")
        elif params.get("projectId") not in self.grants.get(principal, set()):
            response["error"] = self.error("access_denied")
        elif key not in self.accepted and deadline and datetime.fromisoformat(deadline) <= datetime.fromisoformat(CLOCK):
            response["error"] = self.error("deadline_exceeded")
        elif operation == "tasks.submit":
            response.update(self.submit(key, params))
        elif operation in {"tasks.status", "tasks.cancel"}:
            task = self.tasks.get(params["taskId"])
            if task is None or task["projectId"] != params["projectId"]:
                response["error"] = self.error("not_found")
            else:
                if operation == "tasks.cancel":
                    if task["status"] in {"pending", "awaiting_approval"}:
                        self.advance(task["taskId"], "cancelled")
                    elif task["status"] == "running" and not task.get("cancelRequested"):
                        task["cancelRequested"] = True
                        task["revision"] += 1
                response["result"] = deepcopy(task)
        else:
            raise ValueError("This test fixture implements task messages only")

        validator().validate(response)
        return response

    def submit(self, key, params):
        if key in self.accepted:
            previous_params, accepted = self.accepted[key]
            if previous_params != params:
                return {"error": self.error("request_conflict")}
            return {"result": deepcopy(accepted)}
        if params["capabilityId"] != "fixture.local_echo" or params["capabilityVersion"] != "1.0.0":
            return {"error": self.error("capability_unavailable")}
        arguments = params["arguments"]
        if not validator("input", ECHO).is_valid(arguments):
            return {"error": self.error("invalid_request")}
        task_id = f"fixture-task-{len(self.tasks) + 1}"
        task = {
            "taskId": task_id,
            "projectId": params["projectId"],
            "capabilityId": params["capabilityId"],
            "capabilityVersion": params["capabilityVersion"],
            "status": "pending",
            "revision": 1,
            "updatedAt": CLOCK,
        }
        self.tasks[task_id] = task
        self.accepted[key] = (deepcopy(params), deepcopy(task))
        return {"result": deepcopy(task)}

    def execute(self, principal, request_id):
        params, accepted = self.accepted[(principal, "tasks.submit", request_id)]
        task_id = accepted["taskId"]
        self.advance(task_id, "running")
        result = deepcopy(params["arguments"])
        validator("result", ECHO).validate(result)
        self.advance(task_id, "succeeded", result)

    def advance(self, task_id, status, result=None):
        task = self.tasks[task_id]
        validator("transition").validate({"from": task["status"], "to": status})
        updated = {**task, "status": status, "revision": task["revision"] + 1}
        if result is not None:
            updated["result"] = result
        validator("task").validate(updated)
        task.update(updated)

    @staticmethod
    def error(code):
        return {"code": code, "message": code, "retryable": False}


class ContractTests(unittest.TestCase):
    def setUp(self):
        self.fixture = TaskFixture()
        self.submit = deepcopy(EXAMPLES[0])

    def call(self, message):
        return self.fixture.handle(message, message["callerId"])

    def test_examples_and_meta_schema(self):
        Draft202012Validator.check_schema(SCHEMA)
        Draft202012Validator.check_schema(ECHO)
        for message in EXAMPLES:
            with self.subTest(kind=message["kind"], reference=message.get("requestId")):
                validator().validate(message)
        self.assertEqual(EXAMPLES[0]["requestId"], EXAMPLES[1]["requestId"])
        validator("input", ECHO).validate(EXAMPLES[0]["params"]["arguments"])
        validator("result", ECHO).validate(EXAMPLES[2]["result"]["result"])

    def test_event_batch_and_discovery_responses(self):
        response = {
            **EXAMPLES[1], "operation": "events.poll",
            "result": {"events": [EXAMPLES[4]], "nextSequence": 1},
        }
        validator().validate(response)
        self.assertFalse(validator().is_valid({**response, "result": {"events": [EXAMPLES[4]]}}))
        discovery = {
            **response, "operation": "service.discover",
            "result": {"supportedVersions": [VERSION]},
        }
        validator().validate(discovery)
        self.assertFalse(validator().is_valid({**discovery, "result": {"supportedVersions": []}}))

    def test_request_operation_parameters_are_enforced(self):
        for operation, params in [
            ("service.discover", {}),
            ("service.health", {}),
            ("capabilities.list", {"projectId": "fixture-project-a"}),
            ("capabilities.register", {
                "capabilityId": "fixture.local_echo", "version": "1.0.0", "mode": "task",
                "inputSchemaRef": "example-capability.schema.json#/$defs/input",
                "resultSchemaRef": "example-capability.schema.json#/$defs/result"
            }),
            ("data.query", EXAMPLES[0]["params"]),
            ("events.subscribe", {"projectId": "fixture-project-a"}),
            ("events.poll", {"subscriptionId": "fixture-subscription-1", "afterSequence": 0}),
            ("events.unsubscribe", {"subscriptionId": "fixture-subscription-1"}),
        ]:
            with self.subTest(operation=operation):
                valid = request(operation, "example", params)
                validator().validate(valid)
                self.assertFalse(validator().is_valid({**valid, "params": {**params, "extra": 1}}))

    def test_malformed_envelopes_and_task_records_are_rejected(self):
        bad = [
            {**self.submit, "protocolVersion": "2.0.0"},
            {**self.submit, "timestamp": "2026-02-31T00:00:00Z"},
            {**EXAMPLES[1], "result": EXAMPLES[2]["result"]},
            {**EXAMPLES[2], "result": {**EXAMPLES[2]["result"], "result": None}},
            {**EXAMPLES[3], "result": EXAMPLES[1]["result"]},
            {**EXAMPLES[4], "task": {**EXAMPLES[4]["task"], "result": {"value": "private"}}},
        ]
        for index, message in enumerate(bad):
            with self.subTest(index=index):
                self.assertFalse(validator().is_valid(message))
        failed = {**EXAMPLES[1]["result"], "status": "failed"}
        self.assertFalse(validator("task").is_valid(failed))
        validator("task").validate({**failed, "error": TaskFixture.error("outcome_unknown")})

    def test_every_state_transition(self):
        allowed = {
            "pending": {"running", "awaiting_approval", "failed", "cancelled"},
            "running": {"awaiting_approval", "succeeded", "failed", "cancelled"},
            "awaiting_approval": {"pending", "failed", "cancelled"},
        }
        states = {"pending", "running", "awaiting_approval", "succeeded", "failed", "cancelled"}
        self.assertEqual(states, set(SCHEMA["$defs"]["state"]["enum"]))
        for source, target in product(states, repeat=2):
            with self.subTest(source=source, target=target):
                self.assertEqual(
                    target in allowed.get(source, set()),
                    validator("transition").is_valid({"from": source, "to": target}),
                )

    def test_submission_replay_and_conflict(self):
        accepted = self.call(self.submit)
        self.assertEqual("pending", accepted["result"]["status"])
        self.assertEqual(accepted, self.call(self.submit))
        self.submit["params"]["arguments"]["value"] = "changed input"
        self.assertEqual("request_conflict", self.call(self.submit)["error"]["code"])
        self.assertEqual(1, len(self.fixture.tasks))
        self.submit["requestId"] = "new-submit"
        self.submit["params"]["arguments"]["value"] = 17
        self.assertEqual("invalid_request", self.call(self.submit)["error"]["code"])
        self.assertEqual(1, len(self.fixture.tasks))

    def test_fixture_approval_then_execution_produces_a_result(self):
        task = self.call(self.submit)["result"]
        task_id = task["taskId"]
        params = {"taskId": task_id, "projectId": task["projectId"]}
        status = request("tasks.status", "status", params)
        self.fixture.advance(task_id, "awaiting_approval")
        waiting = self.call(status)["result"]
        self.assertEqual("awaiting_approval", waiting["status"])
        self.assertNotIn("result", waiting)
        with self.assertRaises(ValidationError):
            self.fixture.advance(task_id, "running")
        self.fixture.advance(task_id, "pending")
        self.fixture.execute(self.submit["callerId"], self.submit["requestId"])
        final = self.call(status)["result"]
        self.assertEqual("succeeded", final["status"])
        self.assertEqual({"value": "synthetic input"}, final["result"])
        self.assertGreater(final["revision"], waiting["revision"])

    def test_principal_revocation_and_project_boundary(self):
        response = self.fixture.handle(self.submit, "different-device")
        self.assertEqual("unauthenticated", response["error"]["code"])
        denied = deepcopy(self.submit)
        denied["params"]["projectId"] = "fixture-project-b"
        self.assertEqual("access_denied", self.call(denied)["error"]["code"])
        self.assertFalse(self.fixture.tasks)
        task = self.call(self.submit)["result"]
        self.fixture.grants["fixture-eco"] = {"fixture-project-b"}
        self.assertEqual("access_denied", self.call(self.submit)["error"]["code"])
        params = {"projectId": "fixture-project-b", "taskId": task["taskId"]}
        self.assertEqual("not_found", self.call(request("tasks.status", "other", params))["error"]["code"])

    def test_expired_submission_does_not_enqueue(self):
        expired = {**self.submit, "deadline": "2026-01-01T00:00:00Z"}
        self.assertEqual("deadline_exceeded", self.call(expired)["error"]["code"])
        self.assertFalse(self.fixture.tasks)
        accepted = self.call(self.submit)
        self.assertEqual(accepted, self.call(expired))

    def test_cancellation_does_not_report_unconfirmed_terminal_state(self):
        task = self.call(self.submit)["result"]
        task_id = task["taskId"]
        params = {"projectId": task["projectId"], "taskId": task_id}
        with self.assertRaises(ValidationError):
            self.fixture.advance(task_id, "succeeded", {"value": "output"})
        self.fixture.advance(task_id, "running")
        cancel = request("tasks.cancel", "cancel", params)
        cancelled = self.call(cancel)["result"]
        self.assertEqual("running", cancelled["status"])
        self.assertTrue(cancelled["cancelRequested"])
        self.assertEqual(cancelled, self.call(cancel)["result"])
        self.fixture.advance(task_id, "cancelled")
        self.assertEqual("cancelled", self.call(request("tasks.status", "status", params))["result"]["status"])
        with self.assertRaises(ValidationError):
            self.fixture.advance(task_id, "succeeded", {"value": "late result"})


if __name__ == "__main__":
    unittest.main()
