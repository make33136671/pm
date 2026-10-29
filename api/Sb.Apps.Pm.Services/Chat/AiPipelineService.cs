using FreeSql;
using Microsoft.Extensions.Logging;
using SB.Framework.Core.Attribute;
using Sb.Apps.Pm.Models;

namespace Sb.Apps.Pm.Services.Chat
{
    /// <summary>
    /// AI 管线配置管理
    /// </summary>
    [AutoDependencyInjection]
    public class AiPipelineService : BaseCrudService<AiPipeline>
    {
        private readonly IFreeSql _fsql;

        public AiPipelineService(IFreeSql fsql, ILogger<AiPipelineService> logger) : base(fsql, logger)
        {
            _fsql = fsql;
        }

        /// <summary>
        /// 获取群组的管线配置（含步骤）
        /// </summary>
        public virtual Result GetByGroupBinding(int groupBindingId)
        {
            var pipeline = Select
                .Where(p => p.GroupBindingId == groupBindingId)
                .First();

            if (pipeline == null)
                return Result.Fail("未找到该群组的 AI 管线配置!");

            var steps = _fsql.Select<AiPipelineStep>()
                .Where(s => s.PipelineId == pipeline.Id)
                .OrderBy(s => s.OrderNo)
                .ToList();

            return Result.Success(new { pipeline, steps });
        }

        /// <summary>
        /// 为群组创建默认管线配置
        /// </summary>
        public virtual AiPipeline CreateDefaultPipeline(int groupBindingId)
        {
            var pipeline = new AiPipeline
            {
                GroupBindingId = groupBindingId,
                Enabled = false,
                RealtimeEnabled = false,
                CreatedTime = DateTime.Now
            };
            return this.Insert(pipeline);
        }

        /// <summary>
        /// 切换管线开关
        /// </summary>
        public virtual Result ToggleEnabled(int pipelineId, bool enabled)
        {
            _fsql.Update<AiPipeline>()
                .Set(p => p.Enabled, enabled)
                .Where(p => p.Id == pipelineId)
                .ExecuteAffrows();
            return Result.Success(enabled ? "AI 分析已开启" : "AI 分析已关闭");
        }

        /// <summary>
        /// 切换实时分析开关
        /// </summary>
        public virtual Result ToggleRealtime(int pipelineId, bool realtimeEnabled)
        {
            _fsql.Update<AiPipeline>()
                .Set(p => p.RealtimeEnabled, realtimeEnabled)
                .Where(p => p.Id == pipelineId)
                .ExecuteAffrows();
            return Result.Success(realtimeEnabled ? "实时分析已开启" : "实时分析已关闭");
        }

        // ---- 管线步骤管理 ----

        public virtual Result AddStep(AiPipelineStep step)
        {
            step = _fsql.Insert(step).ExecuteInserted().First();
            return Result.Success("步骤添加成功!", step);
        }

        public virtual Result EditStep(AiPipelineStep step)
        {
            var count = _fsql.Update<AiPipelineStep>()
                .SetSource(step)
                .UpdateColumns(s => new { s.StepType, s.AiProviderId, s.Enabled, s.OrderNo, s.Config })
                .ExecuteAffrows();
            return count > 0 ? Result.Success("步骤更新成功!") : Result.Fail("更新失败!");
        }

        public virtual Result DeleteStep(int stepId)
        {
            var count = _fsql.Delete<AiPipelineStep>().Where(s => s.Id == stepId).ExecuteAffrows();
            return count > 0 ? Result.Success("步骤删除成功!") : Result.Fail("删除失败!");
        }
    }
}
