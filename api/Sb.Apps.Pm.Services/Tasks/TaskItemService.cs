using FreeSql;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SB.Framework.Core.Attribute;
using SB.Framework.Core.Expand;
using Sb.Apps.Pm.Models;
using Sb.Apps.Pm.Models.Requirements;
using Sb.Apps.Pm.Services.Ai;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Sb.Apps.Pm.Services.Tasks
{
    [AutoDependencyInjection]
    public class TaskItemService : BaseCrudService<TaskItem>, IModuleInitializer
    {
        #region IModuleInitializer
        public string ModuleName => "任务管理";
        public int InitOrder => 115;
        public Type[] GetEntityTypes() => new[]
        {
            typeof(TaskItem), typeof(TaskItemLog), typeof(TaskItemAttachment),
        };
        public List<MenuDefinition> GetMenus() => new()
        {
            // 顶级路由容器（隐藏，实际入口在项目管理下）
            new MenuDefinition
            {
                Name = "TaskItem", Path = "/task-item", Component = "BasicLayout",
                Redirect = "/task-item/list", Title = "任务管理",
                Icon = "lucide:check-square", OrderNo = 4, HideInMenu = true,
            },
            // 挂载到项目管理下
            new MenuDefinition { ParentPath = "/project", Name = "TaskItemList", Path = "/project/task-item/list", Component = "/task-item/list/index", Title = "任务列表", Icon = "lucide:list", OrderNo = 8 },
            new MenuDefinition { ParentPath = "/project", Name = "TaskItemForm", Path = "/project/task-item/form", Component = "/task-item/form/index", Title = "任务表单", OrderNo = 9, HideInMenu = true },
        };
        public List<SystemSettingDefinition> GetSystemSettings() => new()
        {
            new SystemSettingDefinition { Key = "TaskAgentApiKey", Name = "Agent 接单 API Key", CategoryName = "全局配置", CategoryIcon = "lucide:globe", CategorySort = 1, DisplayType = SystemSettingDisplayType.Input, DefaultValue = "", CantDelete = true, Sort = 7 },
            new SystemSettingDefinition { Key = "hermes_webhook_url", Name = "Hermes Worker Webhook URL", CategoryName = "全局配置", CategoryIcon = "lucide:globe", CategorySort = 1, DisplayType = SystemSettingDisplayType.Input, DefaultValue = "", CantDelete = true, Sort = 8 },
            new SystemSettingDefinition { Key = "hermes_webhook_secret", Name = "Hermes Webhook 签名密钥(HMAC)", CategoryName = "全局配置", CategoryIcon = "lucide:globe", CategorySort = 1, DisplayType = SystemSettingDisplayType.Input, DefaultValue = "", CantDelete = true, Sort = 9 },
        };
        #endregion

        private readonly IFreeSql _fsql;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly AiProviderFactory _aiFactory;
        private readonly DocVersionService _docVersionService;
        private readonly SystemSettingService _settingService;
        private readonly ChatNotificationService _chatNotify;
        private readonly Pipelines.GitLabApiService _gitLabApi;
        private readonly Pipelines.GitRepositoryService _gitRepoService;

        // Webhook 推送用,按 PM 约定走静态 HttpClient（不走 DI 注入）
        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };

        public TaskItemService(IFreeSql fsql, ILogger<TaskItemService> logger,
            IHttpContextAccessor httpContextAccessor, AiProviderFactory aiFactory, DocVersionService docVersionService,
            SystemSettingService settingService, ChatNotificationService chatNotify,
            Pipelines.GitLabApiService gitLabApi, Pipelines.GitRepositoryService gitRepoService) : base(fsql, logger)
        {
            _fsql = fsql;
            _httpContextAccessor = httpContextAccessor;
            _aiFactory = aiFactory;
            _docVersionService = docVersionService;
            _settingService = settingService;
            _chatNotify = chatNotify;
            _gitLabApi = gitLabApi;
            _gitRepoService = gitRepoService;
        }

        #region 管理端 CRUD

        public Result GetList(int? projectId = null, int? requirementId = null, int? featureModuleId = null,
            TaskItemClaimStatus? claimStatus = null, TaskItemExecStatus? execStatus = null,
            TaskItemType? type = null,
            string? assignee = null, string? keyword = null,
            string field = "", string order = "", int page = 1, int pageSize = 20,
            string? source = null)
        {
            var query = Select.Include(c => c.Project).Include(c => c.Requirement).Include(c => c.FeatureModule);
            query = query.WhereIf(projectId.HasValue, c => c.ProjectId == projectId.Value);
            query = query.WhereIf(requirementId.HasValue, c => c.RequirementId == requirementId.Value);
            query = query.WhereIf(featureModuleId.HasValue, c => c.FeatureModuleId == featureModuleId.Value);
            query = query.WhereIf(claimStatus.HasValue, c => c.ClaimStatus == claimStatus.Value);
            query = query.WhereIf(execStatus.HasValue, c => c.ExecStatus == execStatus.Value);
            query = query.WhereIf(type.HasValue, c => c.Type == type.Value);
            query = query.WhereIf(!string.IsNullOrEmpty(assignee), c => c.Assignee == assignee);
            query = query.WhereIf(!string.IsNullOrEmpty(keyword), c => c.Title.Contains(keyword));
            query = query.WhereIf(!string.IsNullOrEmpty(source), c => c.Source == source);
            query = ApplySorting(query, field, order);
            return PagedResult(query, page, pageSize);
        }

        public override Result Detail(int id)
        {
            var entity = Select.Include(c => c.Project).Include(c => c.Requirement).Include(c => c.FeatureModule)
                .Where(c => c.Id == id).First();
            if (entity == null) return Result.Fail("任务不存在!");
            return Result.Success(entity);
        }

        public Result Create(TaskItem task)
        {
            // #97 字段约束:必填校验
            if (string.IsNullOrWhiteSpace(task.Title))
                return Result.Fail("任务标题不能为空");
            if (string.IsNullOrWhiteSpace(task.Content))
                return Result.Fail("任务描述(Content)不能为空，请填写具体的任务说明");
            if (task.RequirementId == null)
                return Result.Fail("任务必须关联需求(RequirementId)");
            if (task.FeatureModuleId == null)
                return Result.Fail("任务必须挂功能模块(FeatureModuleId)");

            task.CreatedTime = DateTime.Now;
            var username = _httpContextAccessor.HttpContext?.User?.Username();
            if (!string.IsNullOrEmpty(username) && string.IsNullOrEmpty(task.Assignee))
                task.Assignee = username;
            // 继承需求的 ProjectId
            if (task.ProjectId == 0 && task.RequirementId.HasValue)
            {
                var req = _fsql.Select<Requirement>().Where(r => r.Id == task.RequirementId.Value).First();
                if (req != null) task.ProjectId = req.ProjectId;
            }
            task = this.Insert(task);
            WriteLog(task.Id, TaskItemLogAction.Created, "任务创建", username);
            _docVersionService.SaveVersion("TaskItem", task.Id, DocVersionService.BuildTaskItemMd(task), "创建任务", username);

            // 自动认领任务 → 推送给 Hermes Worker（HMAC 签名 + 幂等键，后台异步，失败不影响创建）
            TryNotifyHermes(task);

            return Result.Success("任务创建成功!", task);
        }

        /// <summary>
        /// 仅当任务可被自动执行（AutoAssign + 未认领 + 空闲）时,推送 new_task 事件给 Hermes Worker。
        /// 同步取配置后 fire-and-forget,不阻塞任务创建;未配 hermes_webhook_url 则跳过。
        /// </summary>
        private void TryNotifyHermes(TaskItem task)
        {
            if (!task.AutoAssign
                || task.ClaimStatus != TaskItemClaimStatus.Pending
                || task.ExecStatus != TaskItemExecStatus.Idle)
                return;

            var url = _settingService.GetSystemSettingByKey<string>("hermes_webhook_url");
            if (string.IsNullOrWhiteSpace(url)) return;
            var secret = _settingService.GetSystemSettingByKey<string>("hermes_webhook_secret") ?? "";

            var payload = JsonSerializer.Serialize(new
            {
                type = "new_task",
                taskId = task.Id,
                title = task.Title,
                projectId = task.ProjectId,
                requirementId = task.RequirementId,
                claimStatus = task.ClaimStatus.ToString(),
                execStatus = task.ExecStatus.ToString(),
            });
            // 幂等键:Worker 据此去重(同一任务同一认领状态只处理一次)
            var idempotencyKey = $"task:{task.Id}:{task.ClaimStatus}";

            _ = PostWebhookAsync(url!, secret, payload, idempotencyKey, task.Id);
        }

        private async Task PostWebhookAsync(string url, string secret, string payload, string idempotencyKey, int taskId)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(payload, Encoding.UTF8, "application/json"),
                };
                if (!string.IsNullOrEmpty(secret))
                {
                    using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
                    var sig = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
                    req.Headers.TryAddWithoutValidation("X-PM-Signature", $"sha256={sig}");
                }
                req.Headers.TryAddWithoutValidation("X-PM-Idempotency-Key", idempotencyKey);

                var resp = await _http.SendAsync(req);
                if (!resp.IsSuccessStatusCode)
                    _logger.LogWarning("Hermes webhook 推送任务 #{TaskId} 返回 {Code}", taskId, (int)resp.StatusCode);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Hermes webhook 推送任务 #{TaskId} 失败", taskId);
            }
        }

        public Result Edit(TaskItem task)
        {
            task.UpdatedTime = DateTime.Now;
            int count = UpdateDiy.UpdateColumns(c => new
            {
                c.Title, c.Content, c.Type, c.Complexity, c.Assignee,
                c.RequirementId, c.FeatureModuleId, c.ParentId,
                c.ExecMode, c.MaxRetryCount, c.TimeoutSeconds,
                c.EstimatedHours, c.ActualHours, c.DueDate, c.Sort, c.UpdatedTime,
                c.AutoAssign, c.ClaimStatus, c.ExecStatus,
                c.RouteHint, c.FilePathHint, c.LineHint
            }).SetSource(task).ExecuteAffrows();
            if (count > 0)
            {
                var username = _httpContextAccessor.HttpContext?.User?.Username();
                _docVersionService.SaveVersion("TaskItem", task.Id, DocVersionService.BuildTaskItemMd(task), "编辑任务", username);
                // 联动:手动改任务状态后同步关联需求(#95)。RequirementId 可能未随表单提交，回库取真实值
                var reqId = task.RequirementId ?? Select.Where(c => c.Id == task.Id).First(c => c.RequirementId);
                SyncRequirementStatus(reqId);
            }
            return count > 0 ? Result.Success("任务更新成功!") : Result.Fail("更新失败!");
        }

        public override Result Del(int id)
        {
            var task = Select.Where(c => c.Id == id).First();
            if (task == null) return Result.Fail("任务不存在!");
            var reqId = task.RequirementId;
            var result = base.Del(id);
            if (result.Code == 0)
            {
                WriteLog(id, TaskItemLogAction.Cancelled, "任务删除");
                // 联动:删任务后重算需求状态(#95)
                SyncRequirementStatus(reqId);
            }
            return result;
        }

        public Result Kanban(int projectId)
        {
            var tasks = Select.Where(c => c.ProjectId == projectId)
                .OrderBy(c => c.Sort)
                .ToList(c => new
                {
                    c.Id, c.Title, c.ClaimStatus, c.ExecStatus, c.Type,
                    c.Assignee, c.ClaimedBy, c.DueDate, c.FailCount, c.MaxRetryCount
                });
            var groups = tasks.GroupBy(c => c.ExecStatus.ToString()).ToDictionary(g => g.Key, g => g.ToList());
            return Result.Success(groups);
        }

        #endregion

        #region 从需求生成任务

        /// <summary>快速生成：每个需求 1:1 创建一个任务</summary>
        public Result QuickGenerateFromRequirements(List<int> requirementIds, int projectId)
        {
            var requirements = _fsql.Select<Requirement>()
                .Where(r => requirementIds.Contains(r.Id)).ToList();

            var tasks = requirements.Select(r => new TaskItem
            {
                ProjectId = projectId,
                RequirementId = r.Id,
                Title = r.Title,
                Content = r.Content,
                Type = r.Type == RequirementType.Bugfix ? TaskItemType.Bug : TaskItemType.Development,
                Complexity = r.Complexity,
                DueDate = r.DueDate,
            }).ToList();

            return Result.Success(tasks);
        }

        /// <summary>批量确认创建任务</summary>
        public Result ConfirmCreate(List<TaskItem> tasks)
        {
            var username = _httpContextAccessor.HttpContext?.User?.Username();
            foreach (var task in tasks)
            {
                task.CreatedTime = DateTime.Now;
                if (string.IsNullOrEmpty(task.Assignee) && !string.IsNullOrEmpty(username))
                    task.Assignee = username;

                // 逐条插入以获取自增 Id
                task.Id = (int)_fsql.Insert(task).ExecuteIdentity();
                WriteLog(task.Id, TaskItemLogAction.Created, "从需求批量生成", username);
                _docVersionService.SaveVersion("TaskItem", task.Id, DocVersionService.BuildTaskItemMd(task), "从需求生成", username);
            }

            return Result.Success($"已创建 {tasks.Count} 个任务");
        }

        #endregion

        #region 恢复 / 取消

        public Result Resume(int id)
        {
            var task = Select.Where(c => c.Id == id).First();
            if (task == null) return Result.Fail("任务不存在!");
            if (task.ExecStatus != TaskItemExecStatus.Hibernated)
                return Result.Fail("只有休眠状态的任务才能恢复!");

            var username = _httpContextAccessor.HttpContext?.User?.Username();
            _fsql.Update<TaskItem>().Where(c => c.Id == id)
                .Set(c => c.FailCount, 0)
                .Set(c => c.ExecStatus, TaskItemExecStatus.Idle)
                .Set(c => c.ClaimStatus, TaskItemClaimStatus.Pending)
                .Set(c => c.ClaimedBy, null)
                .Set(c => c.ClaimedTime, null)
                .Set(c => c.UpdatedTime, DateTime.Now)
                .ExecuteAffrows();

            WriteLog(id, TaskItemLogAction.Resumed, "用户恢复休眠任务", username);
            return Result.Success("任务已恢复!");
        }

        public Result Cancel(int id)
        {
            var task = Select.Where(c => c.Id == id).First();
            if (task == null) return Result.Fail("任务不存在!");

            var username = _httpContextAccessor.HttpContext?.User?.Username();
            _fsql.Update<TaskItem>().Where(c => c.Id == id)
                .Set(c => c.ExecStatus, TaskItemExecStatus.Failed)
                .Set(c => c.UpdatedTime, DateTime.Now)
                .ExecuteAffrows();

            WriteLog(id, TaskItemLogAction.Cancelled, "用户取消任务", username);
            return Result.Success("任务已取消!");
        }

        #endregion

        #region Agent 接单

        /// <summary>Agent 接单：查找最早的 Pending 任务并标记为 Claimed</summary>
        public Result ClaimNext(string agentName, int? projectId = null)
        {
            var query = Select
                .Where(c => c.ClaimStatus == TaskItemClaimStatus.Pending && c.ExecStatus == TaskItemExecStatus.Idle);
            if (projectId.HasValue) query = query.Where(c => c.ProjectId == projectId.Value);

            var task = query.OrderBy(c => c.Id).First();
            if (task == null) return Result.Fail("暂无可接单的任务");

            _fsql.Update<TaskItem>().Where(c => c.Id == task.Id)
                .Set(c => c.ClaimStatus, TaskItemClaimStatus.Claimed)
                .Set(c => c.ClaimedBy, agentName)
                .Set(c => c.ClaimedTime, DateTime.Now)
                .Set(c => c.UpdatedTime, DateTime.Now)
                .ExecuteAffrows();

            WriteLog(task.Id, TaskItemLogAction.Claimed, $"Agent 接单: {agentName}", agentName);

            // 重新读取返回完整数据
            return Detail(task.Id);
        }

        /// <summary>上报开始执行</summary>
        public Result ReportStart(int taskItemId, string agentName)
        {
            var task = Select.Where(c => c.Id == taskItemId).First();
            if (task == null) return Result.Fail("任务不存在!");
            if (task.ClaimedBy != agentName) return Result.Fail("非接单者不能上报!");

            _fsql.Update<TaskItem>().Where(c => c.Id == taskItemId)
                .Set(c => c.ExecStatus, TaskItemExecStatus.Running)
                .Set(c => c.ExecStartTime, DateTime.Now)
                .Set(c => c.UpdatedTime, DateTime.Now)
                .ExecuteAffrows();

            WriteLog(taskItemId, TaskItemLogAction.ExecStart, "开始执行", agentName);
            return Result.Success("已标记为执行中");
        }

        /// <summary>上报成功</summary>
        public Result ReportSuccess(int taskItemId, string agentName, string? message = null)
        {
            var task = Select.Where(c => c.Id == taskItemId).First();
            if (task == null) return Result.Fail("任务不存在!");

            var duration = task.ExecStartTime.HasValue ? (int)(DateTime.Now - task.ExecStartTime.Value).TotalSeconds : 0;
            _fsql.Update<TaskItem>().Where(c => c.Id == taskItemId)
                .Set(c => c.ExecStatus, TaskItemExecStatus.Success)
                .Set(c => c.ExecEndTime, DateTime.Now)
                .Set(c => c.CompletedDate, DateTime.Now)
                .Set(c => c.UpdatedTime, DateTime.Now)
                .ExecuteAffrows();

            WriteLog(taskItemId, TaskItemLogAction.ExecSuccess, message ?? "执行成功", agentName, duration);

            // 联动:同步关联需求状态(#95)
            SyncRequirementStatus(task.RequirementId);

            // 闭环:任务完成 → 回 TG 群通知（fire-and-forget，依赖单例 fsql/connectorManager，安全）
            _ = _chatNotify.NotifyTaskCompleted(task);

            return Result.Success("执行成功");
        }

        /// <summary>上报失败 — FailCount++ → 达到 MaxRetryCount 则自动休眠</summary>
        public Result ReportFail(int taskItemId, string agentName, string reason)
        {
            var task = Select.Where(c => c.Id == taskItemId).First();
            if (task == null) return Result.Fail("任务不存在!");

            var duration = task.ExecStartTime.HasValue ? (int)(DateTime.Now - task.ExecStartTime.Value).TotalSeconds : 0;
            int newFailCount = task.FailCount + 1;
            bool shouldHibernate = newFailCount >= task.MaxRetryCount;

            var update = _fsql.Update<TaskItem>().Where(c => c.Id == taskItemId)
                .Set(c => c.FailCount, newFailCount)
                .Set(c => c.ExecEndTime, DateTime.Now)
                .Set(c => c.UpdatedTime, DateTime.Now);

            if (shouldHibernate)
            {
                update = update.Set(c => c.ExecStatus, TaskItemExecStatus.Hibernated);
            }
            else
            {
                // 允许重新接单
                update = update
                    .Set(c => c.ExecStatus, TaskItemExecStatus.Idle)
                    .Set(c => c.ClaimStatus, TaskItemClaimStatus.Pending)
                    .Set(c => c.ClaimedBy, null)
                    .Set(c => c.ClaimedTime, null);
            }
            update.ExecuteAffrows();

            WriteLog(taskItemId, TaskItemLogAction.ExecFailed, $"执行失败: {reason} (第{newFailCount}次)", agentName, duration);

            // 联动:同步关联需求状态(#95)
            SyncRequirementStatus(task.RequirementId);

            if (shouldHibernate)
            {
                WriteLog(taskItemId, TaskItemLogAction.Hibernated,
                    $"失败次数达到上限({task.MaxRetryCount})，任务已休眠，等待用户处理", agentName);
                return Result.Success($"任务已休眠，失败{newFailCount}次达到上限{task.MaxRetryCount}");
            }

            return Result.Success($"已记录失败(第{newFailCount}/{task.MaxRetryCount}次)，任务将重新进入待接单");
        }

        /// <summary>上报超时（逻辑同失败）</summary>
        public Result ReportTimeout(int taskItemId, string agentName)
        {
            return ReportFail(taskItemId, agentName, "执行超时");
        }

        /// <summary>获取可接单任务列表</summary>
        public Result GetAvailable(int? projectId = null)
        {
            var query = Select
                .Where(c => c.ClaimStatus == TaskItemClaimStatus.Pending && c.ExecStatus == TaskItemExecStatus.Idle);
            if (projectId.HasValue) query = query.Where(c => c.ProjectId == projectId.Value);

            var tasks = query.OrderBy(c => c.Id)
                .ToList(c => new { c.Id, c.ProjectId, c.Title, c.Type, c.TimeoutSeconds });
            return Result.Success(tasks);
        }

        /// <summary>
        /// 提交验收 — Running → PendingReview（#107 #104）。
        /// 可附带 Git commit 信息：提交者拉最新→改完→commit→上报 commitSha+分支，
        /// PM 自动拉取该 commit 改动文件列表存库，形成交付追溯。
        /// </summary>
        public async Task<Result> SubmitForReview(int taskItemId, string agentName, string fixedDescription,
            string? commitSha = null, string? commitBranch = null)
        {
            if (string.IsNullOrWhiteSpace(fixedDescription))
                return Result.Fail("修复说明不能为空");

            var task = Select.Where(c => c.Id == taskItemId).First();
            if (task == null) return Result.Fail("任务不存在!");
            if (task.ExecStatus != TaskItemExecStatus.Running)
                return Result.Fail($"当前状态 {task.ExecStatus} 不允许提交验收，需先处于 Running 状态");

            // 若上报了 commit，尝试拉取改动文件列表 + 提交者
            string? changedFilesJson = null;
            string? commitAuthor = null;
            if (!string.IsNullOrWhiteSpace(commitSha))
            {
                try
                {
                    var repo = _gitRepoService.GetProjectRepo(task.ProjectId);
                    if (repo != null && int.TryParse(repo.RepoId, out var glProjectId))
                    {
                        var changes = await _gitLabApi.GetCommitChanges(glProjectId, commitSha);
                        changedFilesJson = System.Text.Json.JsonSerializer.Serialize(
                            changes.Select(c => new { path = c.NewPath, status = c.Status }));

                        var detail = await _gitLabApi.GetCommitDetail(glProjectId, commitSha);
                        commitAuthor = detail?.GetType().GetProperty("authorName")?.GetValue(detail)?.ToString();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "拉取 commit 改动失败: sha={Sha}", commitSha);
                }
            }

            _fsql.Update<TaskItem>().Where(c => c.Id == taskItemId)
                .Set(c => c.ExecStatus, TaskItemExecStatus.PendingReview)
                .Set(c => c.ClaimStatus, TaskItemClaimStatus.Delivered)  // 提交验收 → 接单状态置为已交付
                .Set(c => c.FixedDescription, fixedDescription)
                .SetIf(!string.IsNullOrWhiteSpace(commitSha), c => c.CommitSha, commitSha)
                .SetIf(!string.IsNullOrWhiteSpace(commitBranch), c => c.CommitBranch, commitBranch)
                .SetIf(changedFilesJson != null, c => c.ChangedFilesList, changedFilesJson)
                .SetIf(commitAuthor != null, c => c.CommitAuthor, commitAuthor)
                .Set(c => c.UpdatedTime, DateTime.Now)
                .ExecuteAffrows();

            var logMsg = $"提交验收: {fixedDescription}";
            if (!string.IsNullOrWhiteSpace(commitSha))
                logMsg += $"\ncommit: {commitSha[..Math.Min(8, commitSha.Length)]}" +
                          (commitBranch != null ? $" @{commitBranch}" : "");
            WriteLog(taskItemId, TaskItemLogAction.SubmittedReview, logMsg, agentName);
            return Result.Success("已提交验收，等待审核");
        }

        /// <summary>验收通过 — 待验收/已完成 → Success，Verified=true（#102）</summary>
        public Result ReviewApprove(int taskItemId, string operatorName, string? comment = null)
        {
            var task = Select.Where(c => c.Id == taskItemId).First();
            if (task == null) return Result.Fail("任务不存在!");
            // 放宽：执行过的任务（待验收/已完成/失败）都允许验收通过；
            // 未开始执行的任务（Idle/Running/Hibernated）无意义，拦截
            if (task.ExecStatus == TaskItemExecStatus.Idle || task.ExecStatus == TaskItemExecStatus.Running)
                return Result.Fail($"任务尚未提交（当前 {task.ExecStatus}），无法验收");

            var duration = task.ExecStartTime.HasValue ? (int)(DateTime.Now - task.ExecStartTime.Value).TotalSeconds : 0;
            _fsql.Update<TaskItem>().Where(c => c.Id == taskItemId)
                .Set(c => c.ExecStatus, TaskItemExecStatus.Success)
                .Set(c => c.Verified, true)
                .Set(c => c.CompletedDate, DateTime.Now)
                .Set(c => c.ExecEndTime, DateTime.Now)
                .Set(c => c.UpdatedTime, DateTime.Now)
                .ExecuteAffrows();

            WriteLog(taskItemId, TaskItemLogAction.ReviewApproved, comment ?? "验收通过", operatorName, duration);

            // 联动:同步关联需求状态(#95)
            SyncRequirementStatus(task.RequirementId);

            _ = _chatNotify.NotifyTaskCompleted(task);
            return Result.Success("验收通过");
        }

        /// <summary>
        /// 退回重修 — 已提交/已完成的任务都可退回，重置为待接单（#107）
        /// 业务场景：操作员对已提交（待验收）或已完成（Success/Failed）的任务发现问题，
        /// 退回后清空接单信息 + 重置执行状态，让其他人重新接单执行。
        /// </summary>
        public Result ReviewReject(int taskItemId, string operatorName, string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
                return Result.Fail("退回原因不能为空");

            var task = Select.Where(c => c.Id == taskItemId).First();
            if (task == null) return Result.Fail("任务不存在!");
            // 放宽：只拦截"还没提交过"的任务（Idle/Running）；
            // 待验收 / 已完成 / 失败 / 超时 / 休眠 都允许退回重做
            if (task.ExecStatus == TaskItemExecStatus.Idle || task.ExecStatus == TaskItemExecStatus.Running)
                return Result.Fail($"任务尚未提交（当前 {task.ExecStatus}），无需退回；如需中止请用取消");

            _fsql.Update<TaskItem>().Where(c => c.Id == taskItemId)
                .Set(c => c.ExecStatus, TaskItemExecStatus.Idle)
                .Set(c => c.ClaimStatus, TaskItemClaimStatus.Pending)
                .Set(c => c.ClaimedBy, (string?)null)
                .Set(c => c.ClaimedTime, (DateTime?)null)
                .Set(c => c.Verified, false)
                .Set(c => c.CompletedDate, (DateTime?)null)
                .Set(c => c.RejectReason, reason)
                .Set(c => c.FixedDescription, (string?)null)
                .Set(c => c.UpdatedTime, DateTime.Now)
                .ExecuteAffrows();

            WriteLog(taskItemId, TaskItemLogAction.ReviewRejected, $"退回重修: {reason}", operatorName);

            // 联动:任务退回 → 需求回退到 InProgress/Todo(#95)
            SyncRequirementStatus(task.RequirementId);

            return Result.Success("已退回，任务重新进入待接单");
        }

        /// <summary>
        /// AI 自动验证 — 调 AI 判断 FixedDescription 是否满足任务要求 (#108)
        /// 已提交/已完成的任务都可触发；AI 通过则自动调 ReviewApprove，否则调 ReviewReject
        /// </summary>
        public async Task<Result> AutoVerify(int taskItemId, IAiProvider aiProvider)
        {
            var task = Select.Where(c => c.Id == taskItemId).First();
            if (task == null) return Result.Fail("任务不存在!");
            if (task.ExecStatus == TaskItemExecStatus.Idle || task.ExecStatus == TaskItemExecStatus.Running)
                return Result.Fail($"任务尚未提交（当前 {task.ExecStatus}），无法验证");

            var prompt = $"你是一个任务验收员，负责判断任务是否完成。\n\n" +
                $"## 任务标题\n{task.Title}\n\n" +
                $"## 任务要求\n{task.Content ?? "（无详细描述）"}\n\n" +
                $"## 执行者提交的修复说明\n{task.FixedDescription ?? "（未提交说明）"}\n\n" +
                "请根据以上信息判断：任务要求是否已被满足？\n" +
                "只输出以下 JSON，不要其他文字：\n" +
                "{\"approved\": true/false, \"reason\": \"一句话说明判断依据\"}";

            var request = new Ai.AiRequest
            {
                SystemPrompt = "你是严格的任务验收助手，只基于提供的信息做判断，不做假设。",
                Messages = [new Ai.AiMessage { Role = "user", Content = prompt }],
                Temperature = 0.1f,
            };

            var response = await aiProvider.SendAsync(request);
            if (!response.Success)
                return Result.Fail($"AI 自动验证失败: {response.Error}");

            try
            {
                var content = response.Content ?? "";
                // 提取 JSON
                var start = content.IndexOf('{');
                var end = content.LastIndexOf('}');
                if (start < 0 || end < 0)
                    return Result.Fail("AI 返回格式不正确，无法解析验证结果");

                var json = System.Text.Json.JsonDocument.Parse(content.Substring(start, end - start + 1));
                var approved = json.RootElement.GetProperty("approved").GetBoolean();
                var reason = json.RootElement.TryGetProperty("reason", out var r) ? r.GetString() ?? "" : "";

                if (approved)
                {
                    ReviewApprove(taskItemId, "AI-AutoVerify", $"AI 自动通过: {reason}");
                    return Result.Success($"AI 验证通过: {reason}");
                }
                else
                {
                    ReviewReject(taskItemId, "AI-AutoVerify", $"AI 判断不合格: {reason}");
                    return Result.Success($"AI 验证退回: {reason}");
                }
            }
            catch (Exception ex)
            {
                return Result.Fail($"解析 AI 验证结果失败: {ex.Message}");
            }
        }

        #endregion

        #region AI 执行

        /// <summary>AI 直接执行（非流式，同步等待）</summary>
        public async Task<Result> ExecByAi(int taskItemId, int aiProviderId)
        {
            var task = Select.Where(c => c.Id == taskItemId).First();
            if (task == null) return Result.Fail("任务不存在!");

            var config = _fsql.Select<AiProvider>().Where(c => c.Id == aiProviderId).First();
            if (config == null) return Result.Fail("AI 提供商不存在!");

            var adapter = _aiFactory.GetAdapter(config.ProviderType);
            if (adapter == null) return Result.Fail($"未找到适配器: {config.ProviderType}");

            // 标记开始
            _fsql.Update<TaskItem>().Where(c => c.Id == taskItemId)
                .Set(c => c.ExecMode, TaskItemExecMode.Ai)
                .Set(c => c.ExecStatus, TaskItemExecStatus.Running)
                .Set(c => c.AiProviderId, aiProviderId)
                .Set(c => c.ExecStartTime, DateTime.Now)
                .Set(c => c.ClaimStatus, TaskItemClaimStatus.Claimed)
                .Set(c => c.ClaimedBy, $"AI:{config.Name}")
                .Set(c => c.ClaimedTime, DateTime.Now)
                .Set(c => c.UpdatedTime, DateTime.Now)
                .ExecuteAffrows();
            WriteLog(taskItemId, TaskItemLogAction.ExecStart, $"AI 开始执行 (Provider: {config.Name})", $"AI:{config.Name}");

            try
            {
                var prompt = BuildTaskPrompt(task);
                var result = await adapter.GenerateAsync(config, prompt);
                var duration = task.ExecStartTime.HasValue ? (int)(DateTime.Now - task.ExecStartTime.Value).TotalSeconds : 0;

                _fsql.Update<TaskItem>().Where(c => c.Id == taskItemId)
                    .Set(c => c.ExecStatus, TaskItemExecStatus.Success)
                    .Set(c => c.ExecEndTime, DateTime.Now)
                    .Set(c => c.CompletedDate, DateTime.Now)
                    .Set(c => c.UpdatedTime, DateTime.Now)
                    .ExecuteAffrows();

                WriteLog(taskItemId, TaskItemLogAction.ExecSuccess, result, $"AI:{config.Name}", duration);
                return Result.Success("AI 执行完成", new { result, duration });
            }
            catch (Exception ex)
            {
                ReportFail(taskItemId, $"AI:{config.Name}", ex.Message);
                return Result.Fail($"AI 执行失败: {ex.Message}");
            }
        }

        /// <summary>获取 AI 适配器和配置（供 Controller SSE 端点使用）</summary>
        public (IAiProviderAdapter? adapter, AiProvider? config, TaskItem? task, string? error) PrepareAiExec(int taskItemId, int aiProviderId)
        {
            var task = Select.Where(c => c.Id == taskItemId).First();
            if (task == null) return (null, null, null, "任务不存在!");

            var config = _fsql.Select<AiProvider>().Where(c => c.Id == aiProviderId).First();
            if (config == null) return (null, null, null, "AI 提供商不存在!");

            var adapter = _aiFactory.GetAdapter(config.ProviderType);
            if (adapter == null) return (null, null, null, $"未找到适配器: {config.ProviderType}");
            if (!adapter.SupportStream) return (null, null, null, "该 AI 提供商不支持流式输出");

            // 标记开始
            _fsql.Update<TaskItem>().Where(c => c.Id == taskItemId)
                .Set(c => c.ExecMode, TaskItemExecMode.Ai)
                .Set(c => c.ExecStatus, TaskItemExecStatus.Running)
                .Set(c => c.AiProviderId, aiProviderId)
                .Set(c => c.ExecStartTime, DateTime.Now)
                .Set(c => c.ClaimStatus, TaskItemClaimStatus.Claimed)
                .Set(c => c.ClaimedBy, $"AI:{config.Name}")
                .Set(c => c.ClaimedTime, DateTime.Now)
                .Set(c => c.UpdatedTime, DateTime.Now)
                .ExecuteAffrows();
            WriteLog(taskItemId, TaskItemLogAction.ExecStart, $"AI 流式执行 (Provider: {config.Name})", $"AI:{config.Name}");

            return (adapter, config, task, null);
        }

        /// <summary>AI 流式执行完成后更新状态</summary>
        public void FinishAiExec(int taskItemId, bool success, string? message, string operatorName)
        {
            var task = Select.Where(c => c.Id == taskItemId).First();
            var duration = task?.ExecStartTime.HasValue == true ? (int)(DateTime.Now - task.ExecStartTime.Value).TotalSeconds : 0;

            if (success)
            {
                _fsql.Update<TaskItem>().Where(c => c.Id == taskItemId)
                    .Set(c => c.ExecStatus, TaskItemExecStatus.Success)
                    .Set(c => c.ExecEndTime, DateTime.Now)
                    .Set(c => c.CompletedDate, DateTime.Now)
                    .Set(c => c.UpdatedTime, DateTime.Now)
                    .ExecuteAffrows();
                WriteLog(taskItemId, TaskItemLogAction.ExecSuccess, message, operatorName, duration);
                // 联动:同步关联需求状态(#95)
                SyncRequirementStatus(task?.RequirementId);
            }
            else
            {
                ReportFail(taskItemId, operatorName, message ?? "AI 执行失败");
            }
        }

        public string BuildTaskPrompt(TaskItem task)
        {
            return $"任务标题：{task.Title}\n\n任务描述：\n{task.Content ?? "无详细描述"}\n\n" +
                   $"任务类型：{task.Type}\n\n请执行此任务并返回结果。";
        }

        #endregion

        #region 附件管理 (#103)

        /// <summary>上传附件（验收/提交/退回时）</summary>
        public async Task<Result> UploadAttachment(int taskItemId, Microsoft.AspNetCore.Http.IFormFile file, string stage, string? uploadedBy = null)
        {
            if (file == null || file.Length == 0)
                return Result.Fail("文件不能为空");

            var task = Select.Where(t => t.Id == taskItemId).First();
            if (task == null) return Result.Fail("任务不存在!");

            var uploadPath = AppGlobal.AppSettings.UploadConfig.UploadPath;
            var savePath = Path.Combine(Directory.GetCurrentDirectory(), $"wwwroot{uploadPath}", "task-attachments");
            if (!Directory.Exists(savePath))
                Directory.CreateDirectory(savePath);

            var ext = Path.GetExtension(file.FileName);
            var fileName = $"{taskItemId}_{stage}_{Guid.NewGuid():N}{ext}";
            var filePath = Path.Combine(savePath, fileName);

            await using (var stream = File.Create(filePath))
                await file.CopyToAsync(stream);

            var fileUrl = $"{uploadPath}task-attachments/{fileName}";
            var attachment = new TaskItemAttachment
            {
                TaskItemId = taskItemId,
                Stage = stage,
                FileName = file.FileName,
                FileUrl = fileUrl,
                FileSize = file.Length,
                MimeType = file.ContentType ?? "application/octet-stream",
                UploadedBy = uploadedBy,
                CreatedTime = DateTime.Now,
            };

            attachment.Id = (int)_fsql.Insert(attachment).ExecuteIdentity();
            return Result.Success("上传成功", new { id = attachment.Id, fileUrl, fileName = file.FileName });
        }

        /// <summary>获取任务附件列表</summary>
        public Result GetAttachments(int taskItemId, string? stage = null)
        {
            var query = _fsql.Select<TaskItemAttachment>().Where(a => a.TaskItemId == taskItemId);
            if (!string.IsNullOrEmpty(stage))
                query = query.Where(a => a.Stage == stage);
            var list = query.OrderByDescending(a => a.Id).ToList();
            return Result.Success(list);
        }

        /// <summary>删除附件</summary>
        public Result DeleteAttachment(int attachmentId, string? operatorName = null)
        {
            var att = _fsql.Select<TaskItemAttachment>().Where(a => a.Id == attachmentId).First();
            if (att == null) return Result.Fail("附件不存在");

            var physicalPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", att.FileUrl.TrimStart('/'));
            if (System.IO.File.Exists(physicalPath))
                System.IO.File.Delete(physicalPath);

            _fsql.Delete<TaskItemAttachment>().Where(a => a.Id == attachmentId).ExecuteAffrows();
            return Result.Success("删除成功");
        }

        #endregion

        #region 快速补录

        /// <summary>快速补录：AI 直接修复后，一键创建已完成的需求+任务记录</summary>
        public Result LogQuickFix(int projectId, string title, string? content, string type, decimal? actualHours)
        {
            var now = DateTime.Now;
            var username = _httpContextAccessor.HttpContext?.User?.Username();

            // 1. 创建需求（直接完成状态）
            var reqType = Enum.TryParse<RequirementType>(type, true, out var rt) ? rt : RequirementType.Bugfix;
            var req = new Requirement
            {
                ProjectId = projectId,
                Title = title,
                Content = content,
                Type = reqType,
                Status = RequirementStatus.Done,
                Source = RequirementSource.AI,
                IsConfirmed = true,
                Assignee = username,
                ActualHours = actualHours,
                CompletedDate = now,
                CreatedTime = now,
            };
            req.Id = (int)_fsql.Insert(req).ExecuteIdentity();

            // 2. 创建任务（直接完成状态）
            var taskType = reqType == RequirementType.Bugfix ? TaskItemType.Bug : TaskItemType.Development;
            var task = new TaskItem
            {
                ProjectId = projectId,
                RequirementId = req.Id,
                Title = title,
                Content = content,
                Type = taskType,
                ExecMode = TaskItemExecMode.Ai,
                ExecStatus = TaskItemExecStatus.Success,
                ClaimStatus = TaskItemClaimStatus.Claimed,
                ClaimedBy = "AI-QuickFix",
                ClaimedTime = now,
                ExecStartTime = now,
                ExecEndTime = now,
                CompletedDate = now,
                Assignee = username,
                ActualHours = actualHours,
                CreatedTime = now,
            };
            task.Id = (int)_fsql.Insert(task).ExecuteIdentity();

            // 3. 写日志
            WriteLog(task.Id, TaskItemLogAction.Created, "快速补录（AI 修复）", username);
            WriteLog(task.Id, TaskItemLogAction.ExecSuccess, content ?? "AI 修复完成", "AI-QuickFix");

            // 4. 文档版本
            _docVersionService.SaveVersion("Requirement", req.Id, DocVersionService.BuildRequirementMd(req), "快速补录", username);
            _docVersionService.SaveVersion("TaskItem", task.Id, DocVersionService.BuildTaskItemMd(task), "快速补录", username);

            return Result.Success("补录成功!", new { requirementId = req.Id, taskItemId = task.Id });
        }

        #endregion

        #region 日志

        public Result GetLogs(int taskItemId, int page = 1, int pageSize = 20)
        {
            var query = _fsql.Select<TaskItemLog>()
                .Where(c => c.TaskItemId == taskItemId)
                .OrderByDescending(c => c.Id);
            var data = query.Count(out long total).Page(page, pageSize).ToList();
            return Result.Page(data, total);
        }

        public void WriteLog(int taskItemId, TaskItemLogAction action, string? message, string? operatorName = null, int? duration = null)
        {
            try
            {
                _fsql.Insert(new TaskItemLog
                {
                    TaskItemId = taskItemId,
                    Action = action,
                    Message = message,
                    Operator = operatorName,
                    Duration = duration,
                    CreatedTime = DateTime.Now,
                }).ExecuteAffrows();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "写入任务日志失败: TaskItemId={TaskItemId}", taskItemId);
            }
        }

        #endregion

        #region 关联需求联动(#95 自动触发)

        /// <summary>
        /// 任务状态变化后，自动派生其关联需求的状态与工时汇总。
        /// 规则：全部 Success → Done；有 Running/Success → InProgress；全 Idle → Todo。
        /// 在 ReportSuccess / ReviewApprove / ReviewReject / ReportFail / FinishAiExec 等
        /// 改变 ExecStatus 的方法末尾调用，实现任务-需求数据联动。
        /// </summary>
        private void SyncRequirementStatus(int? requirementId)
        {
            if (requirementId is null or 0) return;
            try
            {
                var req = _fsql.Select<Requirement>().Where(r => r.Id == requirementId.Value && !r.IsDeleted).First();
                if (req == null) return;

                var tasks = _fsql.Select<TaskItem>()
                    .Where(t => t.RequirementId == requirementId.Value && !t.IsDeleted)
                    .ToList(t => new { t.ExecStatus, t.EstimatedHours, t.ActualHours });
                if (tasks.Count == 0) return;

                int total = tasks.Count;
                int done = tasks.Count(t => t.ExecStatus == TaskItemExecStatus.Success);
                int running = tasks.Count(t => t.ExecStatus == TaskItemExecStatus.Running
                    || t.ExecStatus == TaskItemExecStatus.PendingReview);
                decimal estHours = tasks.Sum(t => t.EstimatedHours ?? 0m);
                decimal actHours = tasks.Sum(t => t.ActualHours ?? 0m);

                var newStatus = done == total ? RequirementStatus.Done
                    : (done + running) > 0 ? RequirementStatus.InProgress
                    : RequirementStatus.Todo;

                // 已 Closed 的需求不被自动回退；其余按派生结果更新
                if (req.Status == RequirementStatus.Closed) return;

                var update = _fsql.Update<Requirement>().Where(r => r.Id == requirementId.Value)
                    .Set(r => r.Status, newStatus)
                    .Set(r => r.EstimatedHours, estHours)
                    .Set(r => r.ActualHours, actHours)
                    .Set(r => r.UpdatedTime, DateTime.Now);
                if (newStatus == RequirementStatus.Done && req.CompletedDate == null)
                    update = update.Set(r => r.CompletedDate, DateTime.Now);
                update.ExecuteAffrows();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "联动需求状态失败: RequirementId={RequirementId}", requirementId);
            }
        }

        #endregion

        #region 审计日志(#99)

        /// <summary>外部调用写入审计日志(供 pm.sh / auto-loop 等直接记录)。</summary>
        public Result WriteLogExternal(int taskItemId, string action, string? message, string? op)
        {
            if (Select.Where(t => t.Id == taskItemId).First() == null)
                return Result.Fail("任务不存在");
            var username = op ?? _httpContextAccessor.HttpContext?.User?.Username() ?? "System";
            if (!Enum.TryParse<TaskItemLogAction>(action, true, out var logAction))
                logAction = TaskItemLogAction.Resumed; // 未知 Action 用 Resumed 兜底
            WriteLog(taskItemId, logAction, message, username);
            return Result.Success("日志已写入");
        }

        #endregion

        #region AI 任务拆解暂存

        /// <summary>查询 AI 拆解暂存批次中的任务列表（未正式入库,BatchId 非空）。</summary>
        public List<TaskItem> GetBatchedTasks(string batchId)
            => _fsql.Select<TaskItem>()
                .Where(t => t.BatchId == batchId && t.IsDeleted == false)
                .OrderBy(t => t.Sort)
                .ToList();

        #endregion

    }
}
