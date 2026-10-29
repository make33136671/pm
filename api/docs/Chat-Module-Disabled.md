# Chat 模块暂时停用说明

> 状态：**暂时移出编译**（2026-10-29）
> 影响范围：PM 的 Telegram / Chat 相关功能全部不可用
> 恢复方式：`dotnet build -p:EmitChatModule=true`（前提是先完成 API 迁移）

---

## 一、为什么停用

### 包改名

PM 引用的是 `SB.Chat` / `SB.Chat.Telegram`，这两个包 ID **已不存在于任何源**
（BaGet 与 nuget.org 均返回 NU1101）。

仓库 `saibo/sb-framework-chat` 现在的包 ID 是：

| 旧 | 新 |
|---|---|
| `SB.Chat` | `Sb.Framework.Chat`（0.1.0-preview.29） |
| `SB.Chat.Telegram` | `Sb.Framework.Chat.Telegram`（0.1.0-preview.1） |

### 不只是改名，是整组 API 重写

核对已发布的 `Sb.Framework.Chat 0.1.0-preview.29` 程序集，
PM 用到的类型在新包里**一个都没有**：

| PM 使用（旧 `SB.Chat`） | 新包对应物 | 性质 |
|---|---|---|
| `ChatMessageEvent` | `TelegramRequestMessageBase`（抽象基类） | 事件模型 → 继承体系 |
| `ChatMessageType`（枚举） | `RequestMessageType` + `ServiceMessageType` | 枚举拆分 |
| `ChatGroupEvent` | `RequestMessageGroupEvent` + `GroupEventType` | 类型重命名 |
| `ChatCallbackEvent` | `RequestMessageCallback` | 类型重命名 |
| `IChatBot` / `IChatClient` | `ITelegramAdapter` | 接口合并 |
| `ChatMediaInfo` | `MediaBase` / `ImageMedia` / `VideoMedia` / `VoiceMedia` / `FileMedia` / `StickerMedia` | 单一结构体 → 多态类族 |

新包还多了 `MessageDirection`、`MediaReference`、`TelegramAdapterResolver` 等概念，
整体是**面向 adapter 的重构**，不是简单替换。

因此不能靠改 `using` 解决，必须按新 API 重写业务逻辑。

---

## 二、被停用的内容

### 服务（`Sb.Apps.Pm.Services`）

`Compile Remove="Chat\**\*.cs"` 排除整个目录：

| 文件 | 行数 |
|---|---|
| `TelegramBotCommandHandler.cs` | 831 |
| `ChatConnectorManager.cs` | 355 |
| `ChatAnalysisBatchService.cs` | 352 |
| `TelegramWebhookHandler.cs` | 277 |
| `ChatMediaService.cs` | 181 |
| `ChatMessageService.cs` | 166 |
| `ChatNotificationService.cs` | 124 |
| `ChatUserBindingService.cs` | 119 |
| `ChatAccountService.cs` | 115 |
| `AiPipelineService.cs` | 103 |
| `TelegramPollingTask.cs` | 92 |
| `ChatGroupBindingService.cs` | 83 |
| `ChatRequirementService.cs` | 73 |
| `AiPipelineExecutor.cs` | — |
| `Modules/ChatModuleInitializer.cs` | 49 |

合计约 **3300 行**。

### 控制器（`Sb.Apps.Pm.WebApi`）

`Compile Remove` 排除：

- `Controllers/Chat/**`：ChatAccount / ChatAnalysisBatch / ChatGroup / ChatMessage /
  ChatRequirement / ChatUser / TelegramWebhook
- `Controllers/Ai/AiPipelineController.cs`（依赖 `AiPipelineService`）

### 外部耦合点（用 `#if PM_CHAT_MODULE` 包住）

| 文件 | 改动 |
|---|---|
| `Sb.Apps.Pm.Services/Tasks/TaskItemService.cs` | 字段、构造参数、赋值、两处 `NotifyTaskCompleted` 调用 |
| `Sb.Apps.Pm.Services/GlobalUsings.cs` | `global using Sb.Apps.Pm.Services.Chat;` |
| `Sb.Apps.Pm.WebApi/GlobalUsings.cs` | 同上 |

**`Sb.Apps.Pm.Models.Chat` 不受影响**（实体不依赖旧包，表和菜单照常建）。

---

## 三、恢复步骤

### 1. 按新 API 重写 Chat 业务代码

参考本地仓库 `/root/saibo/sb/sb-framework-chat`：

```bash
cd /root/saibo/sb/sb-framework-chat
cat Sb.Framework.Chat/FEATURES.md        # 能力清单
sed -n '1,140p' Sb.Framework.Chat/Model/TelegramRequestMessageBase.cs   # 事件模型
sed -n '1,60p' Sb.Framework.Chat/Adapter/ITelegramAdapter.cs            # adapter 契约
```

### 2. 打开编译开关

```bash
cd api
dotnet build -p:EmitChatModule=true
```

该开关会同时：
- 加回 `Sb.Framework.Chat` / `Sb.Framework.Chat.Telegram` 包引用
- 取消 `Compile Remove`
- 定义 `PM_CHAT_MODULE` 常量，激活 `TaskItemService` 里的 `#if` 分支

### 3. 全绿后删掉开关

迁移完成后，建议直接删掉所有条件编译与 `Compile Remove`，
把 `Sb.Framework.Chat` 变成无条件依赖，避免长期留开关。

---

## 四、当前状态

```bash
cd api
dotnet restore   # 0 error
dotnet build     # 0 warning / 0 error
cd ../tests/Sb.Apps.Pm.FileManager.Tests && dotnet run   # 82/82 通过
```

除 Chat 外的所有模块（项目管理 / 需求 / 任务 / 文档 / 服务器 / 网页终端 /
文件管理 / AI / Pipeline）均正常编译。
