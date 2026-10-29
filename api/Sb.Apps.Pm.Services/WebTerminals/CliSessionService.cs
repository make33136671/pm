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
}
