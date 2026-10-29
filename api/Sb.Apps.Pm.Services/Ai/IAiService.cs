namespace Sb.Apps.Pm.Services.Ai
{
    /// <summary>
    /// AI 服务业务接口 — 编排层，定义所有 AI 能力
    /// 上层业务（控制器、后台任务）通过此接口调用 AI
    /// </summary>
    public interface IAiService
    {
        Task<AiTaskResult> AnalyzeCode(AiContext context);
        Task<AiTaskResult> GenerateCode(AiContext context);
        Task<AiTaskResult> DesignFeature(AiContext context);
        Task<AiTaskResult> ReviewCode(AiContext context);
        Task<AiTaskResult> Chat(AiContext context);

        /// <summary>从项目文档中提取隐含的功能需求点(#80)。</summary>
        Task<AiTaskResult> ExtractFeatureRequirements(AiContext context);

        /// <summary>分析任务执行状态，生成进度概览(#81)。</summary>
        Task<AiTaskResult> AnalyzeTaskProgress(AiContext context);

        /// <summary>对比文档与需求，找出未覆盖/冗余(#82)。</summary>
        Task<AiTaskResult> AnalyzeRequirementCoverage(AiContext context);

        /// <summary>分析文档优缺点并给出优化建议(#83)。</summary>
        Task<AiTaskResult> AnalyzeDocuments(AiContext context);
    }
}
