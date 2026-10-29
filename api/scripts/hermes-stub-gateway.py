#!/usr/bin/env python3
"""
Hermes Gateway Stub (2026-05-31, v2.1)
=========================================

PM 项目 Hermes 接入计划 Stage 0 替代方案:在真实 Hermes Gateway 未部署前,
用本脚本起一个 OpenAI 兼容的 mock,让 HermesAiProviderAdapter 能完整联调
(JSON 请求 / SSE 流式 / 鉴权).

零依赖 — 仅用 Python 3 stdlib.

# 启动 (默认监听 0.0.0.0:8080)
    python3 scripts/hermes-stub-gateway.py
    python3 scripts/hermes-stub-gateway.py --port 9000 --api-key sk-stub

# 在 PM 端配置
    appsettings.json:
      "Hermes": {
        "GatewayUrl": "http://127.0.0.1:8080",
        "ApiKey": ""        // 若启动时传了 --api-key,这里也要填
      }
    或通过 Admin UI 在 AiProvider 表填 Endpoint = "http://127.0.0.1:8080/v1"

# curl 验证
    curl -X POST http://127.0.0.1:8080/v1/chat/completions \
        -H 'Content-Type: application/json' \
        -d '{"model":"stub","messages":[{"role":"user","content":"ping"}]}'

    curl -N -X POST http://127.0.0.1:8080/v1/chat/completions \
        -H 'Content-Type: application/json' \
        -d '{"model":"stub","stream":true,"messages":[{"role":"user","content":"ping"}]}'

# 已实现的兼容性
    - POST /v1/chat/completions (同步): 返回 echo 风格 + 模型回显
    - POST /v1/chat/completions stream=true (流式): SSE,逐字符 yield
    - Authorization Bearer 校验 (--api-key 指定时,否则跳过)
    - GET /healthz: 健康检查
    - 慢响应模拟 (--delay-ms) 用于测 Timeout/Polly retry
    - 错误注入 (--fail-rate 0.3) 30% 概率返回 500,用于测 retry
"""
import argparse
import http.server
import json
import random
import socketserver
import sys
import time
from datetime import datetime, timezone


CONFIG = {
    "api_key": "",
    "delay_ms": 0,
    "fail_rate": 0.0,
    "verbose": False,
}


def now_unix() -> int:
    return int(time.time())


def log(*args):
    if CONFIG["verbose"]:
        ts = datetime.now(timezone.utc).strftime("%H:%M:%S")
        print(f"[{ts}]", *args, file=sys.stderr, flush=True)


class Handler(http.server.BaseHTTPRequestHandler):
    # 静默默认日志,改用自己的 log()
    def log_message(self, format, *args):
        if CONFIG["verbose"]:
            super().log_message(format, *args)

    def _check_auth(self) -> bool:
        if not CONFIG["api_key"]:
            return True
        got = self.headers.get("Authorization", "")
        return got == f"Bearer {CONFIG['api_key']}"

    def _send_json(self, code: int, payload: dict):
        body = json.dumps(payload, ensure_ascii=False).encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def _send_error(self, code: int, msg: str):
        self._send_json(code, {"error": {"message": msg, "type": "stub_error"}})

    # ---- routes ----

    def do_GET(self):
        if self.path == "/healthz":
            self._send_json(200, {"status": "ok", "service": "hermes-stub-gateway"})
            return
        self._send_error(404, f"unknown path {self.path}")

    def do_POST(self):
        if self.path.rstrip("/") != "/v1/chat/completions":
            self._send_error(404, f"unknown path {self.path}")
            return

        if not self._check_auth():
            self._send_error(401, "missing or invalid Authorization Bearer")
            return

        # 故障注入
        if CONFIG["fail_rate"] > 0 and random.random() < CONFIG["fail_rate"]:
            log("inject fail (rate={})".format(CONFIG["fail_rate"]))
            self._send_error(500, "injected failure")
            return

        # 慢响应
        if CONFIG["delay_ms"] > 0:
            time.sleep(CONFIG["delay_ms"] / 1000.0)

        try:
            length = int(self.headers.get("Content-Length", "0"))
            raw = self.rfile.read(length) if length else b"{}"
            req = json.loads(raw.decode("utf-8"))
        except Exception as e:
            self._send_error(400, f"invalid json: {e}")
            return

        model = req.get("model", "stub")
        messages = req.get("messages", [])
        last_user = next(
            (m.get("content", "") for m in reversed(messages) if m.get("role") == "user"),
            "",
        )
        is_stream = bool(req.get("stream", False))

        # 回显: 把 user 输入 reverse 一下作为"回答",便于肉眼判断流通
        reply = f"[stub:{model}] echo→ {last_user[::-1]}"

        log(f"request stream={is_stream} model={model} user_len={len(last_user)}")

        if is_stream:
            self._send_stream(model, reply)
        else:
            self._send_completion(model, reply)

    # ---- responses ----

    def _send_completion(self, model: str, content: str):
        self._send_json(200, {
            "id": f"stub-{now_unix()}",
            "object": "chat.completion",
            "created": now_unix(),
            "model": model,
            "choices": [{
                "index": 0,
                "message": {"role": "assistant", "content": content},
                "finish_reason": "stop",
            }],
            "usage": {"prompt_tokens": 0, "completion_tokens": len(content), "total_tokens": len(content)},
        })

    def _send_stream(self, model: str, content: str):
        # SSE 头
        self.send_response(200)
        self.send_header("Content-Type", "text/event-stream; charset=utf-8")
        self.send_header("Cache-Control", "no-cache")
        self.send_header("Connection", "keep-alive")
        self.end_headers()

        def emit(chunk: dict):
            line = "data: " + json.dumps(chunk, ensure_ascii=False) + "\n\n"
            self.wfile.write(line.encode("utf-8"))
            self.wfile.flush()

        # 第一个 chunk: role
        emit({"id": f"stub-{now_unix()}", "object": "chat.completion.chunk", "model": model,
              "choices": [{"index": 0, "delta": {"role": "assistant"}}]})

        # 每个字符一 chunk,夹一个 keep-alive 注释行测试客户端兼容性
        for i, ch in enumerate(content):
            if i and i % 8 == 0:
                self.wfile.write(b": keep-alive\n\n")
                self.wfile.flush()
            emit({"object": "chat.completion.chunk", "model": model,
                  "choices": [{"index": 0, "delta": {"content": ch}}]})
            time.sleep(0.02)

        # 完结
        emit({"object": "chat.completion.chunk", "model": model,
              "choices": [{"index": 0, "delta": {}, "finish_reason": "stop"}]})
        self.wfile.write(b"data: [DONE]\n\n")
        self.wfile.flush()


class ThreadingServer(socketserver.ThreadingMixIn, http.server.HTTPServer):
    daemon_threads = True
    allow_reuse_address = True


def main():
    ap = argparse.ArgumentParser(description="Hermes Gateway Stub (OpenAI 兼容 mock)")
    ap.add_argument("--host", default="0.0.0.0")
    ap.add_argument("--port", type=int, default=8080)
    ap.add_argument("--api-key", default="", help="若设置,客户端必须发 Authorization: Bearer <key>")
    ap.add_argument("--delay-ms", type=int, default=0, help="人为延迟,用于测 timeout")
    ap.add_argument("--fail-rate", type=float, default=0.0, help="0.0-1.0,概率注入 500,用于测 retry")
    ap.add_argument("-v", "--verbose", action="store_true")
    args = ap.parse_args()

    CONFIG["api_key"] = args.api_key
    CONFIG["delay_ms"] = args.delay_ms
    CONFIG["fail_rate"] = args.fail_rate
    CONFIG["verbose"] = args.verbose

    server = ThreadingServer((args.host, args.port), Handler)
    auth_state = f"(Bearer required: key={args.api_key[:6]}...)" if args.api_key else "(no auth)"
    print(f"[hermes-stub-gateway] listening on http://{args.host}:{args.port} {auth_state}", file=sys.stderr)
    if args.delay_ms:  print(f"[hermes-stub-gateway] artificial delay: {args.delay_ms}ms", file=sys.stderr)
    if args.fail_rate: print(f"[hermes-stub-gateway] fail rate: {args.fail_rate}", file=sys.stderr)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print("\n[hermes-stub-gateway] shutting down", file=sys.stderr)


if __name__ == "__main__":
    main()
