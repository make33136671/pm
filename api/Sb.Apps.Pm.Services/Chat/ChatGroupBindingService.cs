using FreeSql;
using Microsoft.Extensions.Logging;
using SB.Framework.Core.Attribute;
using Sb.Apps.Pm.Models;

namespace Sb.Apps.Pm.Services.Chat
{
    /// <summary>
    /// 群组绑定管理
    /// </summary>
    [AutoDependencyInjection]
    public class ChatGroupBindingService : BaseCrudService<ChatGroupBinding>
    {
        private readonly IFreeSql _fsql;

        public ChatGroupBindingService(IFreeSql fsql, ILogger<ChatGroupBindingService> logger) : base(fsql, logger)
        {
            _fsql = fsql;
        }

        public virtual Result GetList(int? accountId = null, int? projectId = null, ChatGroupBindingStatus? status = null,
            string field = "", string order = "", int page = 1, int pageSize = 20)
        {
            var query = Select
                .Include(g => g.Account)
                .Include(g => g.Project)
                .WhereIf(accountId.HasValue, g => g.AccountId == accountId.Value)
                .WhereIf(projectId.HasValue, g => g.ProjectId == projectId.Value)
                .WhereIf(status.HasValue, g => g.Status == status.Value);
            query = ApplySorting(query, field, order);
            return PagedResult(query, page, pageSize);
        }

        /// <summary>
        /// 创建群组绑定（自动生成绑定码）
        /// </summary>
        public virtual Result Create(ChatGroupBinding binding)
        {
            binding.BindCode = GenerateBindCode();
            binding.Status = ChatGroupBindingStatus.Pending;
            binding.CreatedTime = DateTime.Now;
            binding = this.Insert(binding);
            return Result.Success("群组绑定已创建，绑定码: " + binding.BindCode, binding);
        }

        /// <summary>
        /// 通过绑定码完成绑定
        /// </summary>
        public virtual Result BindByCode(string bindCode, string platformGroupId, string groupName)
        {
            var binding = Select.Where(g => g.BindCode == bindCode && g.Status == ChatGroupBindingStatus.Pending).First();
            if (binding == null)
                return Result.Fail("绑定码无效或已使用!");

            binding.PlatformGroupId = platformGroupId;
            binding.GroupName = groupName;
            binding.Status = ChatGroupBindingStatus.Active;
            binding.BindCode = null;

            UpdateDiy.UpdateColumns(g => new
            {
                g.PlatformGroupId, g.GroupName, g.Status, g.BindCode
            }).SetSource(binding).ExecuteAffrows();

            return Result.Success("群组绑定成功!", binding);
        }

        /// <summary>
        /// 根据平台群组 ID 查找绑定
        /// </summary>
        public virtual ChatGroupBinding? FindByPlatformGroupId(string platformGroupId)
        {
            return Select.Where(g => g.PlatformGroupId == platformGroupId && g.Status == ChatGroupBindingStatus.Active).First();
        }

        private static string GenerateBindCode()
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            var random = new Random();
            return new string(Enumerable.Range(0, 6).Select(_ => chars[random.Next(chars.Length)]).ToArray());
        }
    }
}
