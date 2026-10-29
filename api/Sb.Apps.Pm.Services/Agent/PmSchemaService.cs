using SB.Framework.Core.Attribute;

namespace Sb.Apps.Pm.Services.Agent
{
    /// <summary>
    /// PM 数据模型 Schema 自省服务(任务 #90/#91)。
    /// 返回 PM 数据模型全貌(实体/字段/外键/枚举/派生规则/关联约束),
    /// 供 AI Agent 一次拿到完整模型,无需逐表试探。
    /// </summary>
    [AutoDependencyInjection]
    public class PmSchemaService
    {
        public PmSchema GetSchema()
        {
            return new PmSchema
            {
                Version = "1.0",
                Description = "赛博项目管理系统(PM)数据模型 Schema,供 AI Agent 录入/查询时参考",
                Entities = BuildEntities(),
                DerivationRules = BuildDerivationRules(),
                AssociationRules = BuildAssociationRules(),
                EnumValues = BuildEnums(),
            };
        }

        private static List<EntitySchema> BuildEntities() => new()
        {
            new EntitySchema
            {
                Name = "Project", Table = "sb_pm_project", Description = "项目",
                Fields = new()
                {
                    F("Id", "int", required: true, pk: true),
                    F("Name", "string", required: true, desc: "项目名称"),
                    F("Code", "string", required: true, desc: "项目代号(kebab-case)"),
                    F("Status", "enum:ProjectStatus", desc: "状态"),
                    F("Description", "string"),
                    F("LocalPath", "string", desc: "服务器本地路径"),
                },
            },
            new EntitySchema
            {
                Name = "Requirement", Table = "sb_pm_requirement", Description = "需求",
                Fields = new()
                {
                    F("Id", "int", required: true, pk: true),
                    F("ProjectId", "int", required: true, fk: "Project.Id"),
                    F("Title", "string", required: true),
                    F("Content", "string", desc: "详细描述(Markdown)"),
                    F("Type", "enum:RequirementType", required: true),
                    F("Status", "enum:RequirementStatus", desc: "草稿→待办→进行中→测试→完成"),
                    F("Complexity", "enum:Complexity"),
                    F("Assignee", "string", desc: "负责人"),
                    F("EstimatedHours", "decimal", desc: "预估工时(由任务汇总自动派生)"),
                    F("ActualHours", "decimal", desc: "实际工时(由任务汇总自动派生)"),
                    F("IsConfirmed", "bool", required: true, desc: "是否确认(未确认=暂存)"),
                    F("BatchId", "string", desc: "AI 分析批次 ID"),
                    F("ParentId", "int", fk: "Requirement.Id", desc: "父需求(补充需求)"),
                    F("Sort", "int", required: true),
                    F("IsDeleted", "bool", required: true),
                    F("CreatedTime", "datetime", required: true),
                },
            },
            new EntitySchema
            {
                Name = "TaskItem", Table = "sb_pm_task_item", Description = "任务",
                Fields = new()
                {
                    F("Id", "int", required: true, pk: true),
                    F("ProjectId", "int", required: true, fk: "Project.Id"),
                    F("RequirementId", "int", fk: "Requirement.Id", desc: "必须挂需求"),
                    F("FeatureModuleId", "int", fk: "FeatureModule.Id", desc: "必须挂功能模块"),
                    F("Title", "string", required: true),
                    F("Content", "string", desc: "任务描述,不允许为空"),
                    F("Type", "enum:TaskItemType", required: true),
                    F("Complexity", "enum:Complexity"),
                    F("ClaimStatus", "enum:TaskItemClaimStatus", desc: "Pending→Claimed"),
                    F("ExecStatus", "enum:TaskItemExecStatus", desc: "Idle→Running→Success/Failed"),
                    F("ClaimedBy", "string", desc: "接单方(Agent 名/auto-loop)"),
                    F("Assignee", "string", desc: "指派给,创建时默认当前用户"),
                    F("EstimatedHours", "decimal"),
                    F("ActualHours", "decimal"),
                    F("BatchId", "string", desc: "AI 拆解批次 ID"),
                    F("Sort", "int", required: true),
                    F("IsDeleted", "bool", required: true),
                },
            },
            new EntitySchema
            {
                Name = "FeatureModule", Table = "sb_pm_feature_module", Description = "功能模块",
                Fields = new()
                {
                    F("Id", "int", required: true, pk: true),
                    F("ProjectId", "int", required: true, fk: "Project.Id"),
                    F("ParentId", "int", fk: "FeatureModule.Id", desc: "两级:顶层模块→子功能点"),
                    F("Name", "string", required: true),
                    F("Description", "string"),
                    F("Sort", "int", required: true),
                    F("IsDeleted", "bool", required: true),
                },
            },
            new EntitySchema
            {
                Name = "MindMap", Table = "sb_pm_mind_map", Description = "功能地图(思维导图)",
                Fields = new()
                {
                    F("Id", "int", required: true, pk: true),
                    F("ProjectId", "int", required: true, fk: "Project.Id"),
                    F("Title", "string", required: true),
                    F("Type", "enum:MindMapType", desc: "Requirement/FeatureMap/CodeStructure..."),
                    F("MapData", "string", desc: "JSON,兼容 jsmind/brace-mind 格式"),
                    F("IsMain", "bool"),
                    F("GeneratedBy", "string"),
                    F("IsDeleted", "bool", required: true),
                },
            },
            new EntitySchema
            {
                Name = "ProjectDocument", Table = "sb_pm_project_document", Description = "项目文档",
                Fields = new()
                {
                    F("Id", "int", required: true, pk: true),
                    F("ProjectId", "int", required: true, fk: "Project.Id"),
                    F("Title", "string", required: true),
                    F("Category", "string", desc: "分类:设计文档/评估报告/API文档等"),
                    F("Content", "string", desc: "正文(Markdown)"),
                    F("FileType", "string", desc: "markdown/pdf等"),
                    F("Sort", "int", required: true),
                    F("IsDeleted", "bool", required: true),
                },
            },
        };

        private static List<DerivationRule> BuildDerivationRules() => new()
        {
            new DerivationRule
            {
                Target = "Requirement.EstimatedHours",
                Rule = "= SUM(TaskItem.EstimatedHours WHERE RequirementId = Requirement.Id)",
                Trigger = "TaskItem CRUD",
            },
            new DerivationRule
            {
                Target = "Requirement.Status",
                Rule = "所有任务 Success → Done;有任务 Running/Success → InProgress;全部 Idle → Todo",
                Trigger = "TaskItem.ExecStatus 变更后可调用 POST /admin/Requirement/SyncStatusFromTasks",
            },
            new DerivationRule
            {
                Target = "FeatureModule 进度(逻辑计算)",
                Rule = "子功能点进度 = 挂载该 FeatureModule 的 TaskItem 中 Success 数/总数",
                Trigger = "无存储字段,按需实时计算或前端聚合",
            },
            new DerivationRule
            {
                Target = "MindMap(FeatureMap 类型)",
                Rule = "可由 FeatureModule + TaskItem 数据自动生成 JSON;POST /admin/Project/GenerateFeatureMap",
                Trigger = "手动触发或 FeatureModule/TaskItem 变更后",
            },
        };

        private static List<AssociationRule> BuildAssociationRules() => new()
        {
            new AssociationRule
            {
                Rule = "TaskItem 必须同时挂 RequirementId 和 FeatureModuleId",
                Validation = "POST /admin/TaskItem/Add 时两字段均不为 null",
                ErrorCode = "MISSING_ASSOCIATION",
            },
            new AssociationRule
            {
                Rule = "TaskItem.Content 不允许为空",
                Validation = "后端 Required 校验",
                ErrorCode = "MISSING_CONTENT",
            },
            new AssociationRule
            {
                Rule = "FeatureModule 两级结构:顶层(ParentId=null)→子功能点(ParentId!=null)",
                Validation = "最多两层,任务挂子功能点",
                ErrorCode = "INVALID_MODULE_DEPTH",
            },
            new AssociationRule
            {
                Rule = "Requirement.IsConfirmed=false 时为暂存,不出现在正式列表",
                Validation = "GetList 默认 WHERE IsConfirmed=true",
                ErrorCode = "UNCONFIRMED_REQUIREMENT",
            },
        };

        private static Dictionary<string, string[]> BuildEnums() => new()
        {
            ["ProjectStatus"] = new[] { "Planning", "Active", "OnHold", "Completed", "Archived" },
            ["RequirementType"] = new[] { "Feature", "Bugfix", "Improvement", "Task", "Research" },
            ["RequirementStatus"] = new[] { "Draft", "Todo", "InProgress", "Testing", "Done", "Closed" },
            ["Complexity"] = new[] { "Low", "Medium", "High", "VeryHigh" },
            ["TaskItemType"] = new[] { "Development", "Bug", "Design", "Testing", "Docs", "Other" },
            ["TaskItemClaimStatus"] = new[] { "Pending", "Claimed" },
            ["TaskItemExecStatus"] = new[] { "Idle", "Running", "Success", "Failed", "Timeout", "Hibernated" },
            ["MindMapType"] = new[] { "Requirement", "FeatureMap", "CodeStructure", "ModuleDependency" },
        };

        private static FieldSchema F(string name, string type, bool required = false,
            bool pk = false, string? fk = null, string? desc = null)
            => new() { Name = name, Type = type, Required = required, PrimaryKey = pk, ForeignKey = fk, Description = desc };
    }

    public class PmSchema
    {
        public string Version { get; set; } = "";
        public string Description { get; set; } = "";
        public List<EntitySchema> Entities { get; set; } = new();
        public List<DerivationRule> DerivationRules { get; set; } = new();
        public List<AssociationRule> AssociationRules { get; set; } = new();
        public Dictionary<string, string[]> EnumValues { get; set; } = new();
    }

    public class EntitySchema
    {
        public string Name { get; set; } = "";
        public string Table { get; set; } = "";
        public string Description { get; set; } = "";
        public List<FieldSchema> Fields { get; set; } = new();
    }

    public class FieldSchema
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public bool Required { get; set; }
        public bool PrimaryKey { get; set; }
        public string? ForeignKey { get; set; }
        public string? Description { get; set; }
    }

    public class DerivationRule
    {
        public string Target { get; set; } = "";
        public string Rule { get; set; } = "";
        public string Trigger { get; set; } = "";
    }

    public class AssociationRule
    {
        public string Rule { get; set; } = "";
        public string Validation { get; set; } = "";
        public string ErrorCode { get; set; } = "";
    }
}
