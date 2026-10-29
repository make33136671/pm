using SB.Framework.Models;
using FreeSql.DataAnnotations;
using System.ComponentModel.DataAnnotations;

namespace Sb.Apps.Pm.Models.Chat
{
    /// <summary>
    /// AI 管线步骤
    /// </summary>
    [Table(Name = "sb_pm_ai_pipeline_step")]
    public class AiPipelineStep : IEntity
    {
        [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        /// <summary>关联管线</summary>
        [Column(Name = "PipelineId")]
        public int PipelineId { get; set; }
        [Navigate(nameof(PipelineId))]
        public AiPipeline? Pipeline { get; set; }

        /// <summary>步骤类型</summary>
        [Column(Name = "StepType", MapType = typeof(string), StringLength = 30)]
        public AiPipelineStepType StepType { get; set; }

        /// <summary>引用的 AI 提供商 ID（复用现有 AiProvider 体系）</summary>
        [Column(Name = "AiProviderId")]
        public int AiProviderId { get; set; }

        /// <summary>启用</summary>
        [Column(Name = "Enabled")]
        public bool Enabled { get; set; } = true;

        /// <summary>执行顺序</summary>
        [Column(Name = "OrderNo")]
        public int OrderNo { get; set; }

        /// <summary>步骤特定配置 JSON</summary>
        [Column(Name = "Config", StringLength = -1)]
        public string? Config { get; set; }
    }
}
