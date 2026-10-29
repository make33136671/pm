# AI 计划：Hermes Agent 接入 PM 系统

> **版本**: v2.3 (2026-06-02 AgentToken Update 可空字段修复)
> **日期**: 2026-06-02
> **状态**: Stage 2 完成 — AI 推理链路已打通、AgentToken Bug 已修复
> **前置依赖**: AgentToken 体系（已实现）、Agent REST API（已实现）、AiProvider 工厂（已实现）

---

## 修订记录

| 版本 | 日期 | 审阅者 | 修订要点 |
|------|------|--------|----------|
| v1.0 | 2026-05-30 | 原作者 | 初版,基于 MCP 通信方案(已废弃) |
| v2.0 | 2026-05-31 | 原作者 | 重构版,废弃 MCP,采用 REST API + AiProvider 集成 |
| v2.1 | 2026-05-31 | Claude | 新增 Stage 0 协议握手验证;修正部署路径;Adapter 代码改进(ExtraConfig 参数化、SSE 日志、Polly retry、鉴权决策);Cron 优化为增量端点;Stage 4 AI 记忆共享拆出独立 RFC |
| v2.2 | 2026-06-02 | 实施 | Stage 2 端到端验证通过：创建 ActiveAiProvider 桥接层、修复 DI 注册链路、PM 通过 Hermes Gateway 成功完成 AI 推理 |
| v2.3 | 2026-06-02 | 实施 | AgentToken Update 可空字段修复：FreeSql Update(entity) 忽略 null 列导致 ExpiresAt/RateLimitPerMinute/Notes 无法清空，改用 SetSource 全列更新，测试从 51/54 → 54/54 全通过 |

> **[2026-05-31 修订 · Claude]** 本文档所有 Claude 审阅修订点在正文中以 `> **[2026-05-31 修订 · Claude]**` 前缀的引用块标注;原方案保留不删除,以便对照差异。

---

## 1. 背景与目标

### 1.1 背景

PM 系统目前已具备完整的 Agent 基础设施：
- **AgentToken 认证**：基于 `AgentBearerAuthenticationHandler` 的 Bearer Token 鉴权体系，与 Admin JWT 完全隔离
- **Agent REST API**：4 个 Controller 覆盖需求、任务、项目、进度四大领域，薄壳复用 Service 层
- **AiProvider 工厂**：`AiProviderFactory` + `IAiProviderAdapter` 接口，支持多提供商调度
- **Chat 系统**：Telegram Bot 连接器、消息管道、AI 需求提取管线
- **Workspace 管理**：Git 仓库克隆、分支管理、代码变更操作

当前 Hermes Agent（赛博天龙的个人 AI 助手）尚未系统性地与 PM 对接。需要建立双向通道，让 Hermes 能成为 PM 系统的一个"Agent 用户"，同时 PM 也能通过 Hermes 的 AI 能力获得智能支持。

### 1.2 目标

1. **Hermes → PM**：Hermes 通过 AgentToken + REST API 操作 PM 系统（查询需求、领取任务、汇报进度、创建需求）
2. **PM → Hermes**：PM 系统注册 Hermes 为 `IAiProviderAdapter`，通过 AiProviderFactory 调度 Hermes Gateway 进行 AI 推理
3. **双向同步**：Hermes 与 PM 之间的任务状态、需求变更、消息通知自动同步

---

## 2. 架构概览

```
┌─────────────────────┐         AgentToken + REST API         ┌──────────────────────┐
│   Hermes Agent      │ ────────────────────────────────────► │   PM WebApi           │
│                     │                                       │                       │
│  - Cron Jobs        │                                       │  /agent/requirement/*  │
│  - Delegate Tasks   │                                       │  /agent/task/*         │
│  - User Requests    │ ◄─────────────────────────────────── │  /agent/project/*       │
│                     │   AI Inference (OpenAI-compatible)    │  /agent/progress/*      │
│                     │                                       │                       │
│  Hermes Gateway     │ ────────────────────────────────────► │  IAiProviderAdapter    │
│  (HTTP Endpoint)    │                                       │  (AiProviderFactory)   │
└─────────────────────┘                                       └──────────────────────┘
         ▲                                                         │
         │                                                         ▼
  ┌──────────────┐                                    ┌──────────────────────┐
  │    用户      │                                    │  Chat Pipeline       │
  │  (Telegram/  │                                    │  (AiPipelineExecutor) │
  │   CLI/Web)   │                                    │  - 废话过滤           │
  └──────────────┘                                    │  - 需求提取           │
                                                      │  - 图像/音频处理       │
                                                      └──────────────────────┘
```

---

## 3. Phase 1：Hermes 作为 PM Agent 用户

### 3.1 AgentToken 配置

**现状**：`AgentTokenService` 已支持 Token 创建、列表、吊销，通过 CLI 或管理后台操作。

**操作**：
```bash
# 在 PM 服务器上为 Hermes 创建专用 Token
cd /www/wwwroot/projects/PM/sb-apps-pm
dotnet run --project Sb.Apps.Pm.WebApi -- agent-token create \
    --name "hermes-agent" \
    --scopes "read:*,write:requirement,write:task" \
    --expires "2027-05-31"
# 记录返回的 Token,存入 Hermes 配置/凭据系统(nexus credentials 推荐)
```

> **[2026-05-31 修订 · Claude]** 修正点:
> - 部署路径 `/opt/hermes-pm` → `/www/wwwroot/projects/PM/sb-apps-pm`(全局偏好里 PM 项目实际路径)
> - 命令补 `--project Sb.Apps.Pm.WebApi`,避免在仓库根目录直接 `dotnet run` 找不到主项目
> - 参数同步 AgentToken 计划 v2.1:`--expiresIn "365d"` → `--expires "2027-05-31"`(以实际 CLI 支持的格式为准,可能需要先确认 `AgentTokenCli.cs` 现有解析逻辑);新增 `--scopes` 使用 RBAC 风格
> - **新增最小权限示例**:Hermes 全读 + 只写需求/任务,**不给** `*` 全权,符合最小特权原则

**Hermes 侧配置**：
- 将 Token 存入 Hermes 的凭据存储(nexus credentials 优先,环境变量次之)
- 配置 PM 基地址(内网 `http://192.168.10.242:5000`,公网 ngrok/反代地址另列)
- 配置项命名建议:`PM_GATEWAY_URL` / `PM_AGENT_TOKEN`,与 Hermes 其他凭据保持一致

### 3.2 REST API 封装技能

创建 `pm-agent-api` Skill，封装常用 Agent API 调用：

| 操作 | API 端点 | 说明 |
|------|---------|------|
| 查询需求列表 | `GET /agent/requirement/List?projectId=N&status=S` | 支持分页、筛选、搜索 |
| 需求详情 | `GET /agent/requirement/Detail?id=N` | 含关联任务、文件 |
| 创建需求 | `POST /agent/requirement/Create` | 从对话中提取需求后写入 |
| 更新需求状态 | `POST /agent/requirement/UpdateStatus` | 需求流转 |
| 领取任务 | `POST /agent/task/Claim?projectId=N` | 自动领取下一个可用任务 |
| 任务列表 | `GET /agent/task/List?projectId=N` | 筛选、分页 |
| 任务详情 | `GET /agent/task/Detail?id=N` | 含日志、附件 |
| 汇报进度 | `POST /agent/task/Complete` | 标记完成 + 日志 |
| 项目列表 | `GET /agent/project/List` | 项目概览 |
| 项目详情 | `GET /agent/project/Detail?id=N` | 含子项目 |
| 子项目列表 | `GET /agent/project/SubList?projectId=N` | Git 仓库信息 |
| 健康度评分 | `GET /agent/progress/HealthScore?projectId=N` | 项目健康指标 |
| 构建记录 | `GET /agent/progress/Builds?projectId=N` | CI 状态 |
| 发布记录 | `GET /agent/progress/Releases?projectId=N` | 版本管理 |

### 3.3 典型工作流

**场景 1：Hermes 自主巡检项目状态**
```
Cron Job (每日 09:00)
  → GET /agent/project/List
  → 对每个活跃项目 GET /agent/progress/HealthScore
  → 对健康度低的项目 GET /agent/requirement/List?status=Pending
  → 生成日报摘要 → 发送给用户
```

**场景 2：用户要求 Hermes 创建需求**
```
用户: "在 PM 里为 X 项目创建一个需求：支持导出 PDF 报告"
  → Hermes 解析意图
  → POST /agent/requirement/Create (projectId, title, description, type, priority)
  → 返回创建结果和需求 ID
```

**场景 3：Hermes 自主领取并完成任务**
```
Cron Job / Delegate Task
  → POST /agent/task/Claim?projectId=N
  → 获取任务详情 GET /agent/task/Detail?id=N
  → 根据任务类型执行（代码生成、Bug 修复、需求分析）
  → 完成后 POST /agent/task/Complete
```

---

## 4. Phase 2：PM 调用 Hermes 作为 AI Provider

### 4.1 实现 HermesAiProviderAdapter

**接口定义**（已有）：
```csharp
public interface IAiProviderAdapter
{
    string ProviderType { get; }       // "hermes"
    string DisplayName { get; }        // "Hermes Agent"
    bool SupportStream { get; }        // true
    Task<string> GenerateAsync(AiProvider config, string prompt, CancellationToken ct);
    IAsyncEnumerable<string> StreamGenerateAsync(AiProvider config, string prompt, CancellationToken ct);
}
```

**实现方案（v2.1 修订）**：

> **[2026-05-31 修订 · Claude]** v2.0 代码有 4 处问题:
> 1. `max_tokens` / `temperature` 硬编码,无法运营调参
> 2. SSE 流式解析的 `catch { }` 静默吞异常,将来调试困难
> 3. 未处理 SSE keep-alive 注释行(`: heartbeat`)
> 4. 未处理 `AiProvider.SystemPrompt`(实际字段在 `AiProvider` 实体里,但需核实是否真有此字段,否则改走 `ExtraConfig`)
>
> 关键参数改从 `AiProvider.ExtraConfig`(JSON 字符串)反序列化,所有可调项都让运营在 Admin UI 配,代码不写死。

```csharp
namespace Sb.Apps.Pm.Services.Ai
{
    [AutoDependencyInjection]
    public class HermesAiProviderAdapter : IAiProviderAdapter
    {
        private readonly HttpClient _http;
        private readonly ILogger<HermesAiProviderAdapter> _logger;

        public string ProviderType => "hermes";
        public string DisplayName => "Hermes Agent";
        public bool SupportStream => true;

        public HermesAiProviderAdapter(IHttpClientFactory factory, ILogger<HermesAiProviderAdapter> logger)
        {
            _http = factory.CreateClient("HermesGateway");
            _logger = logger;
        }

        // 从 AiProvider.ExtraConfig (JSON) 解析可调参数;缺省值兜底
        private HermesExtraConfig ParseExtra(AiProvider config)
        {
            if (string.IsNullOrWhiteSpace(config.ExtraConfig))
                return new HermesExtraConfig();
            try
            {
                return JsonSerializer.Deserialize<HermesExtraConfig>(config.ExtraConfig)
                    ?? new HermesExtraConfig();
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "AiProvider#{Id} ExtraConfig parse failed, using defaults", config.Id);
                return new HermesExtraConfig();
            }
        }

        public async Task<string> GenerateAsync(AiProvider config, string prompt, CancellationToken ct = default)
        {
            var extra = ParseExtra(config);
            var requestBody = new
            {
                model = config.ModelName ?? "default",
                messages = new[]
                {
                    new { role = "system", content = extra.SystemPrompt ?? "你是一个专业的技术助手。" },
                    new { role = "user",   content = prompt }
                },
                max_tokens  = extra.MaxTokens   ?? 4096,
                temperature = extra.Temperature ?? 0.3f
            };

            var response = await _http.PostAsJsonAsync("/v1/chat/completions", requestBody, ct);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<ChatCompletionResponse>(ct);
            return result?.Choices?[0]?.Message?.Content ?? "";
        }

        public async IAsyncEnumerable<string> StreamGenerateAsync(
            AiProvider config, string prompt,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            var extra = ParseExtra(config);
            var requestBody = new
            {
                model = config.ModelName ?? "default",
                messages = new[]
                {
                    new { role = "system", content = extra.SystemPrompt ?? "你是一个专业的技术助手。" },
                    new { role = "user",   content = prompt }
                },
                stream      = true,
                temperature = extra.Temperature ?? 0.3f
            };

            var response = await _http.PostAsJsonAsync("/v1/chat/completions", requestBody, ct);
            response.EnsureSuccessStatusCode();

            var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream);

            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync();
                if (string.IsNullOrEmpty(line)) continue;
                if (line.StartsWith(":")) continue;          // SSE keep-alive 注释行
                if (!line.StartsWith("data: ")) continue;

                var data = line.Substring(6);
                if (data == "[DONE]") break;

                SseChunk? chunk = null;
                try
                {
                    chunk = JsonSerializer.Deserialize<SseChunk>(data);
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex,
                        "Hermes SSE chunk parse failed (provider#{Id}, len={Len}): {Snippet}",
                        config.Id, data.Length,
                        data.Length > 200 ? data.Substring(0, 200) + "…" : data);
                    continue;
                }
                if (!string.IsNullOrEmpty(chunk?.Delta?.Content))
                    yield return chunk.Delta.Content!;
            }
        }
    }

    // ExtraConfig 反序列化目标(JSON 形如 {"systemPrompt":"...","maxTokens":2048,"temperature":0.7})
    internal sealed class HermesExtraConfig
    {
        public string? SystemPrompt { get; set; }
        public int?    MaxTokens    { get; set; }
        public float?  Temperature  { get; set; }
    }
}
```

### 4.2 HttpClient 注册（v2.1 修订）

> **[2026-05-31 修订 · Claude]** v2.0 注册版本三处不足:
> 1. 没有超时(默认 100s,远大于第 9 节风险表里写的 30s)
> 2. 没有重试(第 9 节列了"重试机制"但代码完全没体现)
> 3. 鉴权注释掉,**生产决策悬空** — 必须在落地前明确"Gateway 是否要鉴权"

```csharp
// Program.cs (或 Services Extensions)
builder.Services.AddHttpClient("HermesGateway", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Hermes:GatewayUrl"]
        ?? throw new InvalidOperationException("Hermes:GatewayUrl 必须在配置中显式提供"));
    client.Timeout = TimeSpan.FromSeconds(30);   // 与第 9 节风险表一致

    // 鉴权:见 §4.2.1 决策
    var apiKey = builder.Configuration["Hermes:ApiKey"];
    if (!string.IsNullOrEmpty(apiKey))
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", apiKey);
})
// Polly 重试 + 超时(需引用 Microsoft.Extensions.Http.Polly)
.AddTransientHttpErrorPolicy(p => p
    .WaitAndRetryAsync(3, retry => TimeSpan.FromSeconds(Math.Pow(2, retry))))   // 2s/4s/8s
.AddPolicyHandler(Policy.TimeoutAsync<HttpResponseMessage>(30));
```

#### 4.2.1 Gateway 鉴权决策（v2.1 新增,必须落地前确认）

| 场景 | 鉴权策略 |
|------|---------|
| Hermes Gateway 部署在 PM **同一内网**(只接受内网 IP) | 可不加鉴权,`Hermes:ApiKey` 留空,HttpClient 跳过 Authorization 头 |
| Hermes Gateway 部署在 **公网或跨网段** | **必须**加 Gateway 端 API Key,`Hermes:ApiKey` 存 nexus credentials |
| 中期(3-6 月内)有公网暴露计划 | 一开始就加,避免后期改造 |

> **[2026-05-31 修订 · Claude]** 此决策必须在 Stage 1 启动前由运维/架构确认并写入 PR 描述。代码上必须支持"配置了就发,未配置就不发"两种路径,不留死代码注释。

### 4.3 配置项

在 `appsettings.json` 中添加：

```json
{
  "Hermes": {
    "GatewayUrl": "http://192.168.10.XX:8080",
    "ApiKey": "",
    "DefaultModel": "qwen3.6-27b"
  }
}
```

### 4.4 数据库配置（v2.1 修订）

> **[2026-05-31 修订 · Claude]** v2.0 直接给裸 `INSERT INTO ai_provider` SQL 是反模式 — 跳过框架、跳过审计、跳过字段校验、字段名也可能与实际实体不符(v2.0 写 `system_prompt`/`api_endpoint`/`api_key`,实际 `AiProvider` 字段是 `Endpoint` / `ApiKey`,无 `SystemPrompt` — `SystemPrompt` 应放 `ExtraConfig`)。

**推荐方式 1: Admin UI 操作(默认)**

启动 PM Admin 后台 → AI Provider 管理页 → 新增:
| 字段 | 值 |
|------|----|
| Name | `Hermes Agent` |
| ProviderType | `hermes`(必须与 `HermesAiProviderAdapter.ProviderType` 完全相同) |
| ModelName | `qwen3.6-27b`(以实际 Gateway 暴露的 model 名为准) |
| Endpoint | `http://<gateway-host>:<port>/v1` |
| ApiKey | 见 §4.2.1 鉴权决策 |
| ExtraConfig | `{"systemPrompt":"你是 PM 项目智能助手","maxTokens":4096,"temperature":0.3}` |
| Status | 1(启用) |

**推荐方式 2: SeedData 自动注入(可选)**

如果希望部署即可用,在 `Ai` 模块的 `IModuleInitializer.SeedData(fsql)` 里追加:
```csharp
if (!fsql.Select<AiProvider>().Any(a => a.ProviderType == "hermes"))
{
    fsql.Insert(new AiProvider
    {
        Name = "Hermes Agent",
        ProviderType = "hermes",
        ModelName = "qwen3.6-27b",
        Endpoint  = "http://192.168.10.XX:8080/v1",
        ExtraConfig = """{"systemPrompt":"...","maxTokens":4096,"temperature":0.3}""",
        Status = 1,
    }).ExecuteAffrows();
}
```

> **注意**:`AiProviderFactory` 通过 DI 自动发现所有 `IAiProviderAdapter` 实现,注册 Adapter 类后无需额外配置即可被 **`AiPipelineExecutor`** 调用。
>
> **[2026-05-31 修订 · Claude]** 但**不会被 `DefaultAiService` 调用** — `DefaultAiService` 当前直接注入 `IAiProvider?` 单例(见 `DefaultAiService.cs:16,28`),不走 Factory。若希望传统聊天接口也用 Hermes,需要后续单独把 `DefaultAiService` 也改造为通过 Factory 取 Provider。**本计划范围仅覆盖 `AiPipelineExecutor` 链路**,这一点必须告知调用方。

---

## 5. Phase 3：Chat 管道集成

### 5.1 Hermes 作为 Chat Connector

**现状**：`ChatConnectorManager` 已支持 Telegram Bot 和个人账号，通过 `SB.Chat` 框架接入。

**扩展方案**：Hermes 本身就是一个消息通道，可以将 Hermes 的对话视为 PM 的"Chat 接入点"。

**方案 A：Hermes 主动轮询（推荐 — 一期采用）**
- Hermes 通过 Agent API 定期查询 PM 新消息/新需求(基于 `?since=` 增量端点)
- 当 PM 有新动态时,Hermes 主动通知用户
- 用户通过 Hermes 对话直接操作 PM

**方案 B：Webhook 回调（二期升级 — 待 PM 实现出站 Webhook 后切换）**
- PM 新增 Hermes Webhook 连接器
- PM 事件(需求变更、任务完成、构建结果)主动推送到 Hermes
- Hermes 接收后处理并通知用户

> **[2026-05-31 修订 · Claude]** v2.0 只标"方案 A 推荐"没说理由,补完整决策:
> - **一期选 A**:PM 当前**未实现**对外 Webhook 出站机制(Agent REST API 只有入站),实现 Webhook 是独立工程量(~2-3 天)。先用 A 让 Hermes 跑起来,验证场景价值。
> - **二期升级 B**:等场景验证后,在主线上加 Webhook(事件源 + 重试 + 签名),把高频轮询(如构建监控)迁移到推送,降低 PM 负载。
> - **不要同时做 A + B**:会产生事件去重、顺序、状态一致性问题,徒增复杂度。

### 5.2 AI Pipeline 中调用 Hermes

当用户在 PM 系统中配置 AiPipeline 步骤时，可以选择 Hermes 作为 AI Provider：

```
Pipeline 配置:
  Step 1: 废话过滤 → Provider: Hermes (qwen3.6-27b)
  Step 2: 需求提取 → Provider: Hermes (qwen3.6-27b)
  Step 3: 图像识别 → Provider: Hermes (vision model)
```

`AiPipelineExecutor` 会自动通过 `AiProviderFactory.GetAdapter("hermes")` 找到 `HermesAiProviderAdapter` 并调用。

---

## 6. Phase 4：自动化工作流

### 6.1 Cron Job 自动化（v2.1 修订）

利用 Hermes 的 Cron Job 能力建立定时任务,**全部基于增量端点**(`?since=<last-check-time>`)避免全表扫描:

| 任务 | 频率 | 说明 |
|------|------|------|
| 项目健康巡检 | 每日 09:00 | `GET /agent/project/List` + 每项目并发取 HealthScore;生成日报 |
| 需求积压提醒 | 每 2 小时 | `GET /agent/requirement/List?status=Pending&overdueOnly=true` |
| 任务进度同步 | 每 30 分钟 | `GET /agent/task/List?since=<last>&projectId=N`(只取变更过的) |
| 构建结果监控 | 每 30 分钟(降频) | `GET /agent/progress/Builds?since=<last>`,二期改 Webhook 推送 |
| 晨间简报 | 工作日 08:30 | 汇总隔夜变更、新需求、任务状态 |

> **[2026-05-31 修订 · Claude]** v2.0 三处改进:
> 1. **构建监控从每 10 分钟降到每 30 分钟**:10min × 50 项目 = 每小时 300 次 PM 调用,会显著拉高负载。30min 已能满足日常感知,真正的实时性留给 Phase 4 Webhook。
> 2. **所有列表/同步任务都需要 `?since=` 增量参数**:Agent REST API 必须支持(若现有接口未支持,记为本计划的依赖任务,先补到 Agent Controller 上)。
> 3. **状态保存**:Hermes 侧用 Skill 持久化 `last_<task-name>_check` 时间戳(nexus 或本地 SQLite),保证重启后增量游标不丢。

### 6.2 事件驱动响应

当用户通过 Telegram 与 Hermes 交互时，Hermes 自动翻译为 PM API 调用：

```
用户: "今天有什么要做的任务？"
  → Hermes: GET /agent/task/List?projectId=N&execStatus=Pending
  → 格式化返回任务列表

用户: "领取下一个任务"
  → Hermes: POST /agent/task/Claim?projectId=N
  → 返回任务详情

用户: "查看 X 项目状态"
  → Hermes: GET /agent/project/Detail?id=N
           GET /agent/progress/HealthScore?projectId=N
  → 综合返回项目状态报告
```

---

## 7. 安全考虑

### 7.1 Token 管理
- Hermes 使用独立的 AgentToken，权限范围受 Token 绑定项目限制
- Token 存储在 Hermes 凭据系统，不暴露在代码中
- 定期轮换 Token（建议 90 天）

### 7.2 API 调用限流
- Hermes 侧实现请求频率控制（建议：每端点每秒不超过 5 次）
- PM 侧已有 Agent 鉴权中间件，非法请求直接 401

### 7.3 数据隔离
- AgentToken 关联的 AgentContext 包含可访问的项目范围
- Hermes 只能操作 Token 授权范围内的资源和项目

---

## 8. 实施计划

### Stage 0: 协议握手验证（0.5 天） — 修订新增

> **[2026-05-31 工具就绪 · Claude]** 因真实 Hermes Gateway 未部署,提供 **Stub Gateway 替代方案**:`scripts/hermes-stub-gateway.py`(零依赖 Python stdlib)。可选参数:`--api-key`(鉴权)/`--delay-ms`(测 timeout)/`--fail-rate 0.3`(测 retry)。验证步骤:
> ```bash
> # 1. 起 stub
> python3 scripts/hermes-stub-gateway.py --port 18080 -v
>
> # 2. 改 appsettings 指向 stub 后重启 PM
> #    "Hermes": { "GatewayUrl": "http://127.0.0.1:18080" }
>
> # 3. 调用 PM AI 链路触发 HermesAiProviderAdapter,观察 stub 日志
> ```
> Adapter 完整链路已被 **HermesAiProviderAdapterIntegrationTests**(11 用例,HttpMessageHandler mock)无外部依赖地覆盖,Passed。


> **[2026-05-31 修订 · Claude]** **Phase 2 的整个 `HermesAiProviderAdapter` 实现都建立在"Hermes Gateway 兼容 OpenAI Chat Completions 协议"这一未验证假设上**。如果假设不成立,需推倒重写 Adapter。本 Stage 在动 PM 代码前用 curl 做最小验证,降低后期返工风险。

- [ ] **同步推理握手**:
  ```bash
  curl -X POST http://<hermes-gateway>/v1/chat/completions \
       -H "Content-Type: application/json" \
       -H "Authorization: Bearer <gateway-key-if-any>" \
       -d '{
         "model": "default",
         "messages": [{"role":"user","content":"ping"}],
         "max_tokens": 16,
         "temperature": 0.3
       }'
  ```
  验证响应 JSON 是否含 `choices[0].message.content`(OpenAI 标准结构)
- [ ] **流式握手**:在请求体加 `"stream": true`,验证响应是否为 SSE 格式(`data: {...}\n\n` + 结尾 `data: [DONE]`)
- [ ] **错误码握手**:故意发非法 model 名,验证错误响应是否兼容 OpenAI 错误结构(`error.message` / `error.type`)
- [ ] **决策**:
  - ✅ 完全兼容 → 沿用 §4.1 代码方案
  - ⚠️ 部分兼容(如响应结构略不同)→ 调整 `ChatCompletionResponse` 和 `SseChunk` POCO 定义
  - ❌ 完全不兼容 → 改写 Adapter 适配 Hermes Gateway 私有协议,本计划工期 +2 天

### Stage 1: 基础连接（1-2 天）
- [x] **2026-05-31 完成 (Claude)** 在 PM 创建 Hermes 专用 AgentToken — 通过部署目录 CLI `cd /www/wwwroot/pm_api && dotnet Sb.Apps.Pm.WebApi.dll agent-token create --name "hermes-agent" --scopes "read:*,write:requirement,write:task" --expires "2027-05-31"`。结果 `id=2, prefix=sk_pm_uJJATu`,`list` 验证通过。⚠️ **当前一期 Token 不强制 Scope 检查**,scopes 字段已写入待 AgentToken 增强计划 v2.1 实施后生效
- [x] **2026-05-31 完成 (Claude)** Token 明文存入 nexus — 类型 ApiToken,id=101,name=`PM Agent Token (hermes-agent)`,fields: service / token,tags: pm,agent,hermes。Hermes 侧后续通过 `nexus credential get 101` 读取
- [ ] 创建 `pm-agent-api` Skill,封装常用 API 调用 — Hermes 侧任务,留待 Gateway 接入后做
- [ ] 验证:通过 Hermes 查询项目列表、需求列表、任务列表 — 同上,Hermes 就绪后做

### Stage 2: AI Provider 对接（2-3 天）
- [x] **2026-05-31 完成 (Claude)** 实现 `HermesAiProviderAdapter` — `Sb.Apps.Pm.Services/Ai/HermesAiProviderAdapter.cs`,含 `HermesExtraConfig` / 同步/流式 / SSE keep-alive 注释行处理 / chunk 解析失败降级 LogWarning
- [x] **2026-05-31 完成 (Claude)** 注册 HermesGateway HttpClient — 在 `PmServicesExtensions.AddPmServices(IConfiguration)` 中注册,含 30s Timeout + 可选 Bearer ApiKey
- [x] **2026-05-31 完成 (Claude)** Polly retry/timeout 补全 — 引入 `Microsoft.Extensions.Http.Polly` v8.0.10;指数退避 2/4/8s 共 3 次(仅 5xx/408/HttpRequestException);Polly 单次 30s timeout + HttpClient.Timeout 120s 兜底(覆盖流式长响应);4xx 不重试(业务错免误触发)。Program.cs 已切换为 `AddPmServices(builder.Configuration)`
- [x] **2026-05-31 完成 (Claude)** 配置项模板 — `appsettings.json` 新增 `Hermes:GatewayUrl` / `Hermes:ApiKey` 占位项(空字符串),Gateway 部署后填入即可生效;空值时 HttpClient BaseAddress 不设,调用时报错可见
- [x] **2026-05-31 完成 (Claude)** 在 PM 数据库注册 Hermes Provider — 改造 `AiProviderService.SeedData` 为按 ProviderType 增量 upsert(老库只有 Stub 时自动补 Hermes 行);Hermes 默认 `Status=Disabled`,Gateway 接入后在 Admin UI 启用。**已通过 `dotnet build` 两个项目 0 错误**
- [x] **2026-05-31 完成 (Claude)** 单元测试覆盖纯解析逻辑 — `Sb.Apps.Pm.Services.Tests/Ai/HermesAiProviderAdapterTests.cs`(10 用例:ExtraConfig null/空/合法 JSON/部分字段/坏 JSON;SSE chunk 合法/空 delta/无 choices/坏 JSON;元数据)。`dotnet test` **10/10 Passed**
- [x] **2026-05-31 完成 (Claude)** HTTP 链路集成测试 — `HermesAiProviderAdapterIntegrationTests.cs`(11 用例,Mock HttpMessageHandler):同步 happy path(验证 JSON 请求体字段)、默认 ExtraConfig、空 choices、5xx 抛异常、4xx 不重试、流式多 chunk 顺序、keep-alive 注释行、坏 chunk 不中断、DONE 单独、空 delta 跳过。Hermes 全部测试 **21/21 Passed**
- [x] **2026-05-31 完成 (Claude)** Stub Gateway 脚本 — `scripts/hermes-stub-gateway.py`(零依赖 Python stdlib OpenAI 兼容 mock),支持同步 / 流式 SSE(含 keep-alive 注释行) / Bearer 鉴权 / `--delay-ms` 测 timeout / `--fail-rate` 测 retry。curl 烟测通过
- [x] **2026-06-02 完成** 端到端验证 — PM 通过 Hermes 完成 AI 推理：创建 `ActiveAiProvider` 桥接层（`IAiProvider` 实现），从 DB 读取 Active Provider 配置并调用 `AiProviderFactory` 路由请求；修复 `PmServicesExtensions` 注册 `IAiService` + `IAiProvider`；修复 Adapter `[AutoDependencyInjection]` 缺失。测试：`POST /admin/Ai/Chat` 返回 code=0，AI 正确分析 JavaScript Map 类型敏感问题（inputTokens=51, outputTokens=254）
- [x] **2026-06-02 完成** AgentToken Update 可空字段 Bug 修复 — `AgentTokenService.Update` 使用 FreeSql `Update<T>(entity)` 会忽略 null 列，导致 `ExpiresAt`/`RateLimitPerMinute`/`Notes` 无法清空。改用 `SetSource(entity)` 强制全列更新，修复 commit `d3c672e`。验证：AgentToken 测试 15/15 通过（修复前 12/15），全量测试 54/54 通过（修复前 51/54）

### Stage 3: 自动化工作流（2-3 天）
- [x] **2026-06-02 完成** 配置 Cron Jobs（项目巡检、需求提醒、晨间简报）：
  - `2d416ff56a1b` PM 每日健康巡检 (0 9 * * *) — 收集所有项目 HealthScore、需求、任务统计，生成日报
  - `ab6e5a4f461b` PM 需求积压监控 (every 2h) — 检测 Pending 需求，有积压时报警，无积压静默
  - `ed73613ab3b7` PM 晨间简报 (0 8 * * 1-5) — 工作日汇总隔夜变更和今日待办，周末静默
- [x] **2026-06-02 完成** 验证：Cron Job 手动触发测试通过 — 健康巡检输出完整报告、积压监控发现 1 个 Pending 需求、晨间简报正确分类需求状态
- [ ] 建立对话式 PM 操作（用户自然语言 → API 调用）
- [ ] 任务自动领取和完成汇报

### Stage 4: 高级集成（可选）
- [ ] Hermes Chat Connector(将 Hermes 对话作为 PM 消息通道)
- [ ] Webhook 反向推送(PM 事件 → Hermes 主动通知)— 见 §5.1 方案 B
- [ ] 多模型路由(根据任务类型自动选择 Hermes 或其他 Provider)
- [ ] ~~AI 记忆共享(Hermes 记忆 ↔ PM AiMemory)~~ → **拆出独立 RFC,本计划不再包含**

> **[2026-05-31 修订 · Claude]** "AI 记忆共享" 从可选项里移除,原因:
> - 工程量被严重低估 — 涉及 memory schema 对齐、双向同步冲突合并、隐私边界、检索时序一致性
> - 一句话带过会被实施者误判为 "简单做做",实际可能需要 5-10 天甚至独立项目
> - **应当独立立项** `AI_MemorySync_Plan.md` 详谈数据模型、冲突策略、安全边界,再纳入路线图
>
> 其余三项保持"可选",但**必须明确推迟到 Stage 1-3 全部验收后再启动**,避免一期目标蔓延。

---

## 9. 技术风险与对策

| 风险 | 影响 | 对策 |
|------|------|------|
| Hermes Gateway 不可用 | PM 的 AI 功能降级 | AI Provider 工厂支持多 Provider，自动 fallback 到 Stub 或其他 |
| AgentToken 泄露 | 未授权访问 PM | 定期轮换 + 权限最小化 + 操作日志审计 |
| API 响应超时 | Cron Job 失败 | 设置合理 timeout (30s)，重试机制，失败告警 |
| 网络分区 | 双向通信中断 | 离线缓存 + 恢复后批量同步 |
| 模型输出格式变化 | Pipeline 解析失败 | 结构化输出 schema + 异常捕获 + 降级策略 |

---

## 10. 与现有系统的关系

```
现有组件                    本次集成涉及的角色
─────────────────────────────────────────────────────
AgentToken                  ✓ Hermes 的身份凭证
Agent REST API              ✓ Hermes 操作 PM 的通道
AiProviderFactory           ✓ 分发 AI 请求的枢纽
IAiProviderAdapter          ✓ Hermes 需要实现的接口
AiPipelineExecutor          ✓ 自动调用 Hermes 处理消息
ChatConnectorManager        ✓ 可选：Hermes 作为消息通道
WorkspaceService            ✓ Agent 任务执行时操作代码仓库
DefaultAiService            ⚠️ 当前不走 Factory，一期不覆盖，后续单独改造
```

---

## 附录 A：API 端点速查

### Agent API（Hermes → PM）

| 方法 | 端点 | 说明 |
|------|------|------|
| GET | `/agent/requirement/List` | 需求列表（支持筛选/分页） |
| GET | `/agent/requirement/Detail?id=N` | 需求详情 |
| POST | `/agent/requirement/Create` | 创建需求 |
| POST | `/agent/requirement/UpdateStatus` | 更新需求状态 |
| GET | `/agent/task/List` | 任务列表 |
| GET | `/agent/task/Detail?id=N` | 任务详情 |
| GET | `/agent/task/Available?projectId=N` | 可领取任务 |
| POST | `/agent/task/Claim` | 领取任务 |
| POST | `/agent/task/Complete` | 完成任务 |
| GET | `/agent/project/List` | 项目列表 |
| GET | `/agent/project/Detail?id=N` | 项目详情 |
| GET | `/agent/project/SubList?projectId=N` | 子项目列表 |
| GET | `/agent/progress/HealthScore?projectId=N` | 健康度 |
| GET | `/agent/progress/Builds` | 构建记录 |
| GET | `/agent/progress/Releases` | 发布记录 |

### AI Inference（PM → Hermes）

| 方法 | 端点 | 说明 |
|------|------|------|
| POST | `/v1/chat/completions` | 同步推理 |
| POST | `/v1/chat/completions?stream=true` | 流式推理 |

---

**文档版本历史**：
- v2.0 (2026-05-31): 重构版，废弃 MCP 方案，采用专用 REST API + AiProvider 集成
- v1.0 (2026-05-30): 初版，基于 MCP 通信方案（已废弃）
