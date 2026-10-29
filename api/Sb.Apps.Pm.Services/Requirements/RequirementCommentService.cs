using FreeSql;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SB.Framework.Core.Attribute;
using SB.Framework.Core.Expand;
using Sb.Apps.Pm.Models;

namespace Sb.Apps.Pm.Services.Requirements
{
    [AutoDependencyInjection]
    public class RequirementCommentService : BaseCrudService<RequirementComment>
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public RequirementCommentService(IFreeSql fsql, ILogger<RequirementCommentService> logger, IHttpContextAccessor httpContextAccessor) : base(fsql, logger)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public Result GetList(int requirementId, int page = 1, int pageSize = 50)
        {
            var query = Select.Where(c => c.RequirementId == requirementId).OrderByDescending(c => c.Id);
            return PagedResult(query, page, pageSize);
        }

        public Result Create(RequirementComment comment)
        {
            comment.CreatedTime = DateTime.Now;
            if (string.IsNullOrEmpty(comment.AuthorName))
                comment.AuthorName = _httpContextAccessor.HttpContext?.User?.Username();
            comment = this.Insert(comment);
            return Result.Success("评论添加成功!", comment);
        }
    }
}
