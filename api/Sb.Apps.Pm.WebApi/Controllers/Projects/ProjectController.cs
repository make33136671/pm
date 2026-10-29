using FreeSql;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Sb.Apps.Pm.Services;
using Sb.Apps.Pm.Services.Projects;
using Sb.Apps.Pm.Models;
using Sb.Apps.Pm.Models.Projects;
using Sb.Apps.Pm.Services.Ai;

namespace Sb.Apps.Pm.WebApi.Controllers.Projects;

[ApiController, Authorize(AuthenticationSchemes = "JwtAdmin"), Route("[controller]/[action]")]
public class ProjectController : Controller
{
    private readonly ProjectService _service;
    private readonly IFreeSql _fsql;
    private readonly WorkspaceService _workspaceService;
    private readonly ProjectLintService _lint;
    public ProjectController(ProjectService service, IFreeSql fsql, WorkspaceService workspaceService, ProjectLintService lint)
    {
        _service = service;
        _fsql = fsql;
        _workspaceService = workspaceService;
        _lint = lint;
    }

    [HttpGet]
    public Result List(string? keyword = null, ProjectStatus? status = null,
        string field = "", string order = "", int page = 1, int pageSize = 20)
        => _service.GetList(keyword, status, field, order, page, pageSize);

    [HttpGet]
    public Result ListWithStats(string? keyword = null, ProjectStatus? status = null,
        string field = "", string order = "", int page = 1, int pageSize = 20)
        => _service.GetListWithStats(keyword, status, field, order, page, pageSize);

    [HttpGet]
    public Result Detail(int id) => _service.Detail(id);

    [HttpPost]
    public Result Add([FromForm] Project project) => _service.Create(project);

    [HttpPost]
    public Result Edit([FromForm] Project project) => _service.Edit(project);

    [HttpPost]
    public Result Del([FromForm] int id) => _service.Del(id);

    [HttpGet]
    public Result Overview(int id) => _service.Overview(id);

    /// <summary>
    /// 主项目 + 子项目 树状结构,供网页终端「关联项目」级联选择器使用
    /// 每个节点附带 computedLocalPath: 用 WorkspaceService 按当前命名规则算出的目录
    /// workspace_root 未配置时 computedLocalPath 返回 null(前端按需展示)
    /// </summary>
    [HttpGet]
    public Result CheckPathExists(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return Result.Success(new { exists = false });
        return Result.Success(new { exists = Directory.Exists(path), path });
    }

    [HttpGet]
    public Result Tree()
    {
        var projects = _fsql.Select<Project>()
            .OrderBy(p => p.Sort).OrderBy(p => p.Id)
            .ToList(p => new { p.Id, p.Name, p.Code });
        var subs = _fsql.Select<SubProject>()
            .OrderBy(s => s.Sort).OrderBy(s => s.Id)
            .ToList(s => new { s.Id, s.Name, s.Code, s.ProjectId });

        var nodes = projects.Select(p =>
        {
            string? projectPath = null;
            try { projectPath = _workspaceService.GetProjectLocalPath(p.Id); } catch { }

            var children = subs.Where(s => s.ProjectId == p.Id).Select(s =>
            {
                string? subPath = null;
                try { subPath = _workspaceService.GetLocalPath(s.Id); } catch { }
                return new { id = s.Id, name = s.Name, code = s.Code, computedLocalPath = subPath };
            }).ToList();

            return new { id = p.Id, name = p.Name, code = p.Code, computedLocalPath = projectPath, children };
        }).ToList();

        return Result.Success(nodes);
    }

    /// <summary>项目完整度校验(Lint):列出数据缺口(#94)。</summary>
    [HttpGet]
    public Result Lint(int id) => _lint.Lint(id);

    /// <summary>功能地图从 FeatureModule+TaskItem 数据派生并写入 MindMap(#98)。</summary>
    [HttpPost]
    public Result GenerateFeatureMap([FromForm] int id) => _service.GenerateFeatureMap(id);

    /// <summary>一站式项目蓝图导入(#96):需求+功能模块+任务一次建好并自动关联外键。</summary>
    [HttpPost, AllowJsonBody]
    public Result ImportBlueprint([FromQuery] int projectId, [FromServices] ProjectBlueprintService blueprintService,
        [FromBody] BlueprintInput input)
        => blueprintService.Import(projectId, input);
}
