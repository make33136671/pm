using FreeSql;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SB.Framework.Core.Expand;
using SB.Framework.Core.FreeSql;
using Sb.Apps.Pm.Models.Projects;
using Sb.Apps.Pm.Services.Ai;

namespace Sb.Apps.Pm.Services.WebTerminals;

[AutoDependencyInjection]
public class CliSessionService : BaseCrudService<CliSession>
{
    private readonly IFreeSql _fsql;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly PtyManager _pty;
    private readonly CliConfigService _configService;
    private readonly WorkspaceService _workspaceService;

    public CliSessionService(
        IFreeSql fsql,
        ILogger<CliSessionService> logger,
        IHttpContextAccessor httpContextAccessor,
        PtyManager pty,
        CliConfigService configService,
        WorkspaceService workspaceService) : base(fsql, logger)
    {
        _fsql = fsql;
        _httpContextAccessor = httpContextAccessor;
        _pty = pty;
        _configService = configService;
        _workspaceService = workspaceService;
    }

    public Result GetList(
        int? projectId = null,
        CliSessionStatus? status = null,
        string? owner = null,
        string field = "",
        string order = "",
        int page = 1,
        int pageSize = 20)
    {
        var query = Select
            .WhereIf(projectId.HasValue, c => c.ProjectId == projectId.Value)
            .WhereIf(status.HasValue, c => c.Status == status!.Value)
            .WhereIf(!string.IsNullOrEmpty(owner), c => c.OwnerUsername == owner)
            .Include(c => c.Project);
        query = ApplySorting(query, field, order);

        // 把活跃池里的实时状态合并进结果(数据库里的 Status 可能滞后)
        var paged = PagedResult(query, page, pageSize);
        return paged;
    }

    public override Result Detail(int id)
    {
        var s = Select.Where(c => c.Id == id).Include(c => c.Project).First();
        if (s == null) return Result.Fail("会话不存在");
        return Result.Success(s);
    }

    /// <summary>创建会话 + 立即启动 PTY 进程。返回会话实体(含 Id 和 Pid)。</summary>
    public Result Create(string? name, int? projectId, int? subProjectId = null, int cols = 120, int rows = 30, string? cwd = null)
    {
        var owner = _httpContextAccessor.HttpContext?.User?.Username() ?? "unknown";

        // 自动联动: subProjectId 优先于 projectId(子项目隐含其主项目)
        Project? proj = null;
        SubProject? sub = null;
        if (subProjectId.HasValue)
        {
            sub = _fsql.Select<SubProject>().Where(x => x.Id == subProjectId.Value).First();
            if (sub != null && !projectId.HasValue) projectId = sub.ProjectId;
        }
        if (projectId.HasValue)
            proj = _fsql.Select<Project>().Where(x => x.Id == projectId.Value).First();

        // name 没填 → 根据选中项目自动拼
        if (string.IsNullOrWhiteSpace(name))
        {
            var time = DateTime.Now.ToString("HHmmss");
            name = (proj, sub) switch
            {
                ({ } p, { } s2) => $"{p.Name}/{s2.Name}-{time}",
                ({ } p, null) => $"{p.Name}-{time}",
                _ => $"Session-{time}",
            };
        }

        // cwd 没填 → 根据选中项目自动算(子项目目录优先于主项目目录)
        if (string.IsNullOrWhiteSpace(cwd))
        {
            try
            {
                if (subProjectId.HasValue) cwd = _workspaceService.GetLocalPath(subProjectId.Value);
                else if (projectId.HasValue) cwd = _workspaceService.GetProjectLocalPath(projectId.Value);
            }
            catch (Exception ex)
            {
                // workspace_root 没配或非绝对路径 — 落回 DefaultCwd 而不是阻断会话创建
                _logger.LogWarning(ex, "自动推断 cwd 失败,落回 DefaultCwd: projectId={ProjectId}, subProjectId={SubProjectId}", projectId, subProjectId);
            }
        }

        var s = new CliSession
        {
            Name = name!,
            ProjectId = projectId,
            SubProjectId = subProjectId,
            OwnerUsername = owner,
            Status = CliSessionStatus.Running,
            StartedAt = DateTime.Now,
            LastActiveAt = DateTime.Now,
        };
        s = Insert(s);

        try
        {
            var cfg = _configService.GetDecryptedForRuntime();
            int pid = _pty.Start(s.Id, cfg, cols, rows, cwd);
            s.Pid = pid;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "启动 PTY 失败 session {Id}", s.Id);
            // 标记为已退出 + 异常原因
            _fsql.Update<CliSession>()
                .Set(c => c.Status, CliSessionStatus.Terminated)
                .Set(c => c.ExitedAt, DateTime.Now)
                .Set(c => c.TerminationReason, "spawn_failed: " + ex.Message)
                .Where(c => c.Id == s.Id)
                .ExecuteAffrows();
            return Result.Fail("启动会话失败: " + ex.Message);
        }
        return Result.Success("会话已启动", s);
    }

    /// <summary>强制 kill PTY + 标记 Terminated。</summary>
    public Result Destroy(int id)
    {
        var s = Select.Where(c => c.Id == id).First();
        if (s == null) return Result.Fail("会话不存在");
        if (s.Status == CliSessionStatus.Running)
            _pty.Kill(id, "user_terminated");
        return Result.Success("已发送终止信号");
    }

    /// <summary>清空当前会话日志文件(保留会话记录)。</summary>
    public Result Rename(int id, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return Result.Fail("会话名不能为空");
        var count = UpdateDiy
            .Set(c => c.Name, name.Trim())
            .Set(c => c.UpdatedTime, DateTime.Now)
            .Where(c => c.Id == id)
            .ExecuteAffrows();
        return count > 0 ? Result.Success("重命名成功") : Result.Fail("会话不存在");
    }

    public Result ClearLog(int id)
    {
        var s = Select.Where(c => c.Id == id).First();
        if (s == null) return Result.Fail("会话不存在");
        var abs = _pty.GetLogFileAbsPath(s.LogFilePath);
        if (abs != null && File.Exists(abs))
        {
            try
            {
                if (s.Status == CliSessionStatus.Running)
                {
                    // 还在跑,truncate 而不是 delete(避免 PtyManager 写入丢失)
                    using var fs = new FileStream(abs, FileMode.Truncate, FileAccess.Write);
                }
                else
                {
                    File.Delete(abs);
                }
            }
            catch (Exception ex) { return Result.Fail("清理失败: " + ex.Message); }
        }
        UpdateDiy.UpdateColumns(c => new { c.LogFileSize })
            .Set(c => c.LogFileSize, 0)
            .Where(c => c.Id == id)
            .ExecuteAffrows();
        return Result.Success("日志已清空");
    }

    public string? GetLogAbsPath(int id)
    {
        var s = Select.Where(c => c.Id == id).First();
        return s == null ? null : _pty.GetLogFileAbsPath(s.LogFilePath);
    }

    public override Result Del(int id)
    {
        var s = Select.Where(c => c.Id == id).First();
        if (s == null) return Result.Fail("会话不存在");
        if (s.Status == CliSessionStatus.Running) _pty.Kill(id, "user_terminated");

        var abs = _pty.GetLogFileAbsPath(s.LogFilePath);
        if (abs != null && File.Exists(abs))
            try { File.Delete(abs); } catch { }

        return base.Del(id);
    }

    /// <summary>
    /// 彻底删除（物理删除，不可恢复）。
    ///
    /// 与 <see cref="Del"/> 的区别：CliSession 实现了 ISoftDelete，Del 只把
    /// IsDeleted 置 true，记录仍留在表里。这里绕开软删除直接物理删除，
    /// 并清掉对应的日志文件与空目录。
    ///
    /// 规则：**运行中的会话不会被删除**（需要先结束），避免误删正在用的。
    /// </summary>
    /// <param name="id">指定会话；为 null 且 purgeAll=true 时删除全部已结束会话</param>
    /// <param name="purgeAll">清空所有已结束会话的历史</param>
    public Result Purge(int? id, bool purgeAll = false)
    {
        // ⚠️ 软删除的坑：
        // CliSession 实现 ISoftDelete，框架把 IsDeleted=false 做成了**全局过滤**，
        // 连 _fsql.Select<T>() 也会自动带上，所以被软删除的记录查不出、删不掉
        // ——那正是「历史残留占库」的根源。
        // 这里直接用原生 SQL（_fsql.Ado）绕开过滤器。
        List<CliSession> targets;

        if (purgeAll)
        {
            targets = _fsql.Ado.Query<CliSession>(
                "SELECT * FROM sb_pm_webterm_session").ToList();
        }
        else if (id != null)
        {
            var s = _fsql.Ado.Query<CliSession>(
                "SELECT * FROM sb_pm_webterm_session WHERE \"Id\" = @id",
                new { id = id.Value }).FirstOrDefault();
            if (s == null) return Result.Fail("会话不存在");
            targets = [s];
        }
        else
        {
            return Result.Fail("缺少参数");
        }

        // 只保护「PTY 进程真的还活着」的会话；
        // 不用数据库 Status 字段判断——pm 重启后该字段可能是残留的 Running。
        var protectedCount = targets.Count(t => _pty.IsAlive(t.Id));
        targets = targets.Where(t => !_pty.IsAlive(t.Id)).ToList();

        if (targets.Count == 0) return Result.Success("没有可清除的历史会话", 0);

        var deletedLogs = 0;
        var ids = new List<int>(targets.Count);

        foreach (var s in targets)
        {
            ids.Add(s.Id);
            var abs = _pty.GetLogFileAbsPath(s.LogFilePath);
            if (abs != null && File.Exists(abs))
            {
                try { File.Delete(abs); deletedLogs++; } catch { }
            }
        }

        // 物理删除：原生 SQL，绕过软删除全局过滤（Delete<T> 同样会被拦截）
        var removed = 0;
        if (ids.Count > 0)
        {
            removed = _fsql.Ado.ExecuteNonQuery(
                $"DELETE FROM sb_pm_webterm_session WHERE \"Id\" IN ({string.Join(",", ids)})");
        }

        CleanupEmptyLogDirs();

        _logger.LogInformation(
            "[webterm] 彻底删除会话 {Count} 条, 日志 {Logs} 个, 跳过运行中 {Skip} 个, purgeAll={All}",
            removed, deletedLogs, protectedCount, purgeAll);

        var msg = $"已彻底删除 {removed} 条会话记录（含 {deletedLogs} 个日志文件）";
        if (protectedCount > 0) msg += $"，跳过 {protectedCount} 个运行中的会话";
        return Result.Success(msg, removed);
    }

    /// <summary>清理 logs/cli/YYYYMM/ 下变空的月份目录</summary>
    private void CleanupEmptyLogDirs()
    {
        try
        {
            var root = _pty.LogRoot;
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return;
            foreach (var dir in Directory.GetDirectories(root))
            {
                try
                {
                    if (Directory.GetFiles(dir).Length == 0 &&
                        Directory.GetDirectories(dir).Length == 0)
                    {
                        Directory.Delete(dir);
                    }
                }
                catch { }
            }
        }
        catch { }
    }
}
