#!/usr/bin/env python3
"""
Hermes Worker —— 参考实现（运行在 Hermes 侧，**不在 PM 仓库内运行**）。

闭环中 PM 之外的那一环：接收 PM 的 new_task webhook → 认领 → 改代码 → 开 MR →
回写完成 → 触发 CI（强制走人工审批，不自动发布）。

依赖：pip install flask httpx
环境变量：
  PM_API_BASE          PM Agent API 基址，如 http://192.168.10.73:6020/agent
  PM_AGENT_KEY         服务账号 scoped key（sk_...），来自 PM Admin→系统→AccessToken；**勿硬编码**
  HERMES_WEBHOOK_SECRET 与 PM 系统设置 hermes_webhook_secret 一致（HMAC 校验）

鉴权：所有 PM 调用带 Authorization: Bearer ${PM_AGENT_KEY}；端点为 [FromForm]，用表单编码。
安全：CI 触发 autoApprove=false —— AI 产出必须经 PM 既有构建/发布审批,严禁自动发布。
"""
import hashlib
import hmac
import os

import httpx
from flask import Flask, request

PM_API_BASE = os.environ["PM_API_BASE"].rstrip("/")
PM_AGENT_KEY = os.environ["PM_AGENT_KEY"]
WEBHOOK_SECRET = os.environ.get("HERMES_WEBHOOK_SECRET", "")

app = Flask(__name__)
_processed: set[str] = set()  # 幂等：生产环境换成持久化存储（Redis/DB）

_client = httpx.Client(
    base_url=PM_API_BASE,
    headers={"Authorization": f"Bearer {PM_AGENT_KEY}"},
    timeout=30,
)


def pm_post(path: str, **form):
    """调 PM Agent API（表单编码，[FromForm] 绑定）。"""
    resp = _client.post(f"/{path}", data={k: v for k, v in form.items() if v is not None})
    resp.raise_for_status()
    return resp.json()


def verify_signature(body: bytes, header: str) -> bool:
    if not WEBHOOK_SECRET:
        return True  # 未配密钥则不校验（仅限内网测试）
    expect = "sha256=" + hmac.new(WEBHOOK_SECRET.encode(), body, hashlib.sha256).hexdigest()
    return hmac.compare_digest(expect, header or "")


@app.post("/webhooks/pm-task-updates")
def on_new_task():
    # 1) 验签
    if not verify_signature(request.get_data(), request.headers.get("X-PM-Signature", "")):
        return {"ok": False, "error": "bad signature"}, 401

    # 2) 幂等（taskId + claimStatus）
    idem = request.headers.get("X-PM-Idempotency-Key", "")
    if idem in _processed:
        return {"ok": True, "skipped": "duplicate"}
    _processed.add(idem)

    evt = request.get_json(silent=True) or {}
    if evt.get("type") != "new_task":
        return {"ok": True, "ignored": evt.get("type")}

    # 后台处理（生产用任务队列；这里同步示意）
    try:
        process_task(evt)
    except Exception as ex:  # 失败让 PM 侧超时后重新入队（ReportFail 也可显式上报）
        app.logger.exception("处理任务失败: %s", ex)
        return {"ok": False, "error": str(ex)}, 500
    return {"ok": True}


def process_task(evt: dict):
    project_id = evt.get("projectId")

    # 3) 认领下一个可执行任务（ClaimNext，按 key 身份）
    claimed = pm_post("task/Claim", projectId=project_id).get("data")
    if not claimed:
        app.logger.info("无可认领任务")
        return
    task_id = claimed["id"]

    # 4) 标记开始执行
    pm_post("task/Start", id=task_id)

    # 5) === Hermes 实际开发工作（占位）===
    #    - 由 PM 项目配置解析目标仓库/分支（GitRepositoryService）
    #    - git worktree 隔离检出 → 改代码 → commit 到 ai/<taskId> → push → 开 MR
    #    - 严禁直接 push 主干 / 直接发布
    branch = f"ai/{task_id}"
    mr_url = do_dev_work(claimed, branch)  # 返回 MR 链接（实现略）

    # 6) 回写完成（附 MR 信息）
    pm_post("task/Complete", id=task_id, message=f"AI 完成,已开 MR: {mr_url}")

    # 7) 触发 CI —— autoApprove=false：进 PM 构建审批,人工通过后才发布
    if claimed.get("pipelineProfileId"):
        pm_post(
            "ci/Trigger",
            profileId=claimed["pipelineProfileId"],
            branch=branch,
            autoApprove="false",
        )


def do_dev_work(task: dict, branch: str) -> str:
    """占位：worktree 隔离 → 改码 → commit → push → 开 MR，返回 MR 链接。"""
    raise NotImplementedError("接 Hermes 的代码生成 + git/MR 能力")


if __name__ == "__main__":
    app.run(host="0.0.0.0", port=8900)
