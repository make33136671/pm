using FreeSql;
using Microsoft.Extensions.Logging;
using SB.Framework.Core.Attribute;
using Sb.Apps.Pm.Models;

namespace Sb.Apps.Pm.Services.Chat
{
    /// <summary>
    /// 用户绑定管理
    /// </summary>
    [AutoDependencyInjection]
    public class ChatUserBindingService : BaseCrudService<ChatUserBinding>
    {
        private readonly IFreeSql _fsql;

        public ChatUserBindingService(IFreeSql fsql, ILogger<ChatUserBindingService> logger) : base(fsql, logger)
        {
            _fsql = fsql;
        }

        public virtual Result GetList(int? projectId = null, string? platformType = null, ChatUserRole? role = null,
            string? keyword = null, string field = "", string order = "", int page = 1, int pageSize = 20)
        {
            var query = Select
                .WhereIf(projectId.HasValue, u => u.ProjectId == projectId.Value)
                .WhereIf(!string.IsNullOrEmpty(platformType), u => u.PlatformType == platformType)
                .WhereIf(role.HasValue, u => u.Role == role.Value)
                .WhereIf(!string.IsNullOrEmpty(keyword), u =>
                    u.PlatformUserName!.Contains(keyword!) || u.ExternalName!.Contains(keyword!) || u.ExternalCompany!.Contains(keyword!));
            query = ApplySorting(query, field, order);

            var total = query.Count();
            var items = query.Page(page, pageSize).ToList();

            var projectIds = items.Select(i => i.ProjectId).Distinct().ToList();
            var adminIds = items.Where(i => i.AdminId.HasValue).Select(i => i.AdminId!.Value).Distinct().ToList();

            var projectNames = _fsql.Select<Project>().Where(p => projectIds.Contains(p.Id))
                .ToDictionary(p => p.Id, p => p.Name);
            var adminNames = adminIds.Count > 0
                ? _fsql.Select<Admin>().Where(a => adminIds.Contains(a.Id))
                    .ToDictionary(a => a.Id, a => a.Username)
                : new Dictionary<int, string>();

            var list = items.Select(i => new
            {
                i.Id, i.PlatformType, i.PlatformUserId, i.PlatformUserName,
                i.ProjectId, ProjectName = projectNames.GetValueOrDefault(i.ProjectId, ""),
                i.AdminId, AdminName = i.AdminId.HasValue ? adminNames.GetValueOrDefault(i.AdminId.Value, "") : "",
                i.ExternalName, i.ExternalCompany, i.Role, i.Status, i.CreatedTime
            });

            return Result.Page(list, total);
        }

        /// <summary>
        /// 生成验证码（内部用户绑定流程）
        /// </summary>
        public virtual Result GenerateVerifyCode(int adminId, int projectId, string platformType)
        {
            var existing = Select.Where(u => u.AdminId == adminId && u.PlatformType == platformType
                && u.ProjectId == projectId && u.Status == ChatUserBindingStatus.Active).First();
            if (existing != null)
                return Result.Fail("该用户在此项目已绑定，请先解绑!");

            var code = new Random().Next(100000, 999999).ToString();
            var binding = new ChatUserBinding
            {
                PlatformType = platformType,
                PlatformUserId = "",
                ProjectId = projectId,
                AdminId = adminId,
                Role = ChatUserRole.TeamMember,
                VerifyCode = code,
                VerifyExpireTime = DateTime.Now.AddMinutes(5),
                Status = ChatUserBindingStatus.PendingVerify,
                CreatedTime = DateTime.Now
            };
            binding = this.Insert(binding);
            return Result.Success("验证码已生成，请在 5 分钟内完成绑定", new { code, id = binding.Id });
        }

        /// <summary>
        /// 验证并完成绑定（Bot 私聊 /verify 命令触发）
        /// </summary>
        public virtual Result VerifyAndBind(string verifyCode, string platformUserId, string platformUserName)
        {
            var binding = Select.Where(u => u.VerifyCode == verifyCode
                && u.Status == ChatUserBindingStatus.PendingVerify
                && u.VerifyExpireTime > DateTime.Now).First();

            if (binding == null)
                return Result.Fail("验证码无效或已过期!");

            binding.PlatformUserId = platformUserId;
            binding.PlatformUserName = platformUserName;
            binding.Status = ChatUserBindingStatus.Active;
            binding.VerifyCode = null;
            binding.VerifyExpireTime = null;

            UpdateDiy.UpdateColumns(u => new
            {
                u.PlatformUserId, u.PlatformUserName, u.Status, u.VerifyCode, u.VerifyExpireTime
            }).SetSource(binding).ExecuteAffrows();

            return Result.Success("绑定成功!");
        }

        /// <summary>
        /// 根据平台用户 ID 查找绑定
        /// </summary>
        public virtual ChatUserBinding? FindByPlatformUserId(string platformType, string platformUserId)
        {
            return Select.Where(u => u.PlatformType == platformType
                && u.PlatformUserId == platformUserId
                && u.Status == ChatUserBindingStatus.Active).First();
        }
    }
}
