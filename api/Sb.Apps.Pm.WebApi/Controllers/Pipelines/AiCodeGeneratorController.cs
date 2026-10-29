using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sb.Apps.Pm.Models;
using Sb.Apps.Pm.Services.Pipelines;

namespace Sb.Apps.Pm.WebApi.Controllers.Pipelines;

/// <summary>
/// AI 代码生成器管理端 API (#133-138)
/// </summary>
[ApiController, Authorize(AuthenticationSchemes = "JwtAdmin"), Route("[controller]/[action]"), AllowJsonBody]
public class AiCodeGeneratorController : Controller
{
    private readonly AiCodeGeneratorService _service;

    public AiCodeGeneratorController(AiCodeGeneratorService service)
    {
        _service = service;
    }

    /// <summary>
    /// 为任务触发 AI 代码生成 (#133 #135 #136)
    /// 流程: 读任务 → AI 生成 → 建分支 → 写文件 → 创建 MR
    /// </summary>
    [HttpPost]
    public async Task<Result> Generate(
        [FromForm] int taskItemId,
        [FromForm] int gitlabProjectId,
        [FromForm] string targetBranch = "main")
    {
        var result = await _service.GenerateCodeForTask(taskItemId, gitlabProjectId, targetBranch);
        return result.Success
            ? Result.Success("AI 代码生成完成", new
            {
                branchName = result.BranchName,
                changedFiles = result.ChangedFiles.Select(f => new { f.FilePath, f.Action }),
                mr = result.MrInfo,
            })
            : Result.Fail(result.ErrorMessage ?? "生成失败");
    }
}
