using System.Text.Json.Serialization;

namespace Sb.Apps.Pm.Models
{
    // ==================== 基础枚举 ====================

    public enum AdminStatus
    {
        Active = 0,
        Disabled = 1,
    }

    // ==================== 项目相关 ====================

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ProjectStatus { Planning, Active, OnHold, Completed, Archived }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum CustomerStatus { Active, Expired, Terminated }

    // ==================== 需求相关 ====================

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum RequirementType { Feature, Bugfix, Improvement, Task, Research }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum RequirementStatus { Draft, Todo, InProgress, Testing, Done, Closed }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum RequirementSource { Manual, Telegram, AI, Git, Hermes }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum Complexity { Low, Medium, High, VeryHigh }

    // ==================== 里程碑（保留枚举，实体未删除） ====================

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum MilestoneStatus { Open, InProgress, Completed, Cancelled }

    // ==================== 配置相关 ====================

    public enum ConfigDisplayType { Input = 0, TextArea = 1, Password = 2, Select = 3 }

    // ==================== Git 相关 ====================

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum GitPlatform { GitLab, GitHub }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum GitSyncStatus { Idle, Syncing, Error }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum PipelineStatus { Pending, Running, Success, Failed, Cancelled }

    // ==================== 构建发布 ====================

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum BuildType { Manual, CI, Scheduled }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum BuildStatus { PendingApproval, Approved, Building, Success, Failed, Rejected }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ReleaseStatus { Pending, Approved, Deploying, Success, Failed, Rollback }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ApprovalStatus { None, Pending, Approved, Rejected }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum DeployMethod { FTP, SSH, BtPanel, Manual, BuiltInApi }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum BackupType { Auto, Manual }

    // ==================== 流水线配置 ====================

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum VersionStrategy { AutoIncrement, Semantic, Timestamp }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum TriggerMode { Manual, MergeRequest, TagPush }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum DeployMode { None, GitLabDeploy, SbpmDeploy }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum BuildSource { GitLabCi, LocalBuild, LastArtifact }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum DeployStrategy { Replace, BlueGreen, Rolling, Canary }

    // ==================== 变更日志 / 导图 ====================

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum GeneratedBy { Manual, Auto }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum MindMapType { Requirement, CodeStructure, ModuleDependency, FeatureMap }

    // ==================== 聊天平台 ====================

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ChatAccountType { Bot, Personal }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ChatAccountStatus { Active, Disabled }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum AppEnv { All, Development, Production }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ChatGroupBindingStatus { Pending, Active, Disabled }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ChatUserRole { Admin, TeamMember, Client }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ChatUserBindingStatus { Active, PendingVerify, Disabled }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ChatMessageType { Text, Image, File, Audio, Video, Sticker }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ChatRequirementStatus { Draft, PendingConfirm, Confirmed, Rejected }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum AiPipelineStepType { Filter, RequirementExtract, ImageRecognition, AudioTranscription }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ChatBatchStatus { Pending, Running, Completed, Failed }

    // ==================== AI ====================

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum AiMemoryScope { Global, Project, Permanent }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum AiTaskType { AnalyzeChat, ExtractRequirement, FixBug, GenerateChangelog, GenerateMindMap, AnalyzeCode, ReviewCode, GenerateCode, DesignFeature }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum AiTaskStatus { Pending, Running, Completed, Failed }

    // ==================== 服务器管理 ====================

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ServerStatus { Active, Disabled, Unreachable }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ServerType { Cloud, Dedicated, Local, VPS }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum DatabaseType { MySQL, PostgreSQL, SQLite, MSSQL, MongoDB }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum PaymentCycle { Monthly, Quarterly, Yearly, OneTime }

    // ==================== DB 迁移 / 定时任务 ====================

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum MigrationStatus { Pending, Executed, Failed, Rollback }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ScheduledTaskType { GitSync, Backup, Build, CleanBackup, AiAnalyze }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ScheduledTaskStatus { Active, Paused, Disabled }

    // ==================== 功能模块 ====================

    /// <summary>
    /// 功能模块实现状态（手动覆盖用）。null = 按关联需求自动派生，
    /// 非空 = 手动锁定。展示中文：Planning=规划中/InProgress=开发中/Done=已上线/Deprecated=已废弃
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum FeatureModuleStatus { Planning, InProgress, Done, Deprecated }

    // ==================== 接口管理 ====================

    /// <summary>接口 HTTP 方法</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ApiMethod { GET, POST, PUT, DELETE, PATCH }

    /// <summary>接口归属端：Api=后端提供，Admin=管理端调用，H5=H5端调用</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ApiSide { Api, Admin, H5 }

    /// <summary>接口实现状态</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ApiEndpointStatus { Planned, Developing, Done, Deprecated }

    // ==================== 任务系统 ====================

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum TaskItemType { Development, Bug, Design, Testing, Docs, Other }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum TaskItemClaimStatus { Pending, Claimed, Delivered }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum TaskItemExecStatus { Idle, Running, PendingReview, Rejected, Success, Failed, Timeout, Hibernated }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum TaskItemExecMode { Api, Ai }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum TaskItemLogAction { Created, Claimed, ExecStart, ExecSuccess, ExecFailed, ExecTimeout, Hibernated, Resumed, Cancelled, SubmittedReview, ReviewApproved, ReviewRejected }

    // ==================== AI 提供商 ====================

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum AiProviderStatus { Active, Disabled }

    // ==================== 网页 CLI ====================

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum CliSessionStatus { Running, Exited, Terminated, Reaped }
}
