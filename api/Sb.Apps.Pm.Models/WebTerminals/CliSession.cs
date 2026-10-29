using SB.Framework.Models;
using FreeSql.DataAnnotations;
using Sb.Apps.Pm.Models.Projects;
using System.ComponentModel.DataAnnotations;

namespace Sb.Apps.Pm.Models.WebTerminals
{
    [Table(Name = "sb_pm_webterm_session")]
    public class CliSession : IEntity, ISoftDelete
    {
        [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        [Required, StringLength(100)]
        [Column(Name = "Name")]
        public string Name { get; set; } = "";

        [Column(Name = "ProjectId")]
        public int? ProjectId { get; set; }

        [Navigate(nameof(ProjectId))]
        public Project? Project { get; set; }

        [Column(Name = "SubProjectId")]
        public int? SubProjectId { get; set; }

        [Navigate(nameof(SubProjectId))]
        public SubProject? SubProject { get; set; }

        [Required, StringLength(50)]
        [Column(Name = "OwnerUsername")]
        public string OwnerUsername { get; set; } = "";

        [Column(Name = "Status", MapType = typeof(string), StringLength = 20)]
        public CliSessionStatus Status { get; set; } = CliSessionStatus.Running;

        // PTY 子进程 PID(仅 Running 状态有意义,其它状态作为历史记录)
        [Column(Name = "Pid")]
        public int? Pid { get; set; }

        [Column(Name = "ShellPath", StringLength = 200)]
        public string? ShellPath { get; set; }

        [Column(Name = "Cwd", StringLength = 500)]
        public string? Cwd { get; set; }

        [Column(Name = "StartedAt", DbType = "timestamp", InsertValueSql = "now()")]
        public DateTime StartedAt { get; set; }

        [Column(Name = "LastActiveAt", DbType = "timestamp", IsNullable = true)]
        public DateTime? LastActiveAt { get; set; }

        [Column(Name = "ExitedAt", DbType = "timestamp", IsNullable = true)]
        public DateTime? ExitedAt { get; set; }

        [Column(Name = "ExitCode")]
        public int? ExitCode { get; set; }

        // 终止原因: user_exit / user_terminated / idle_reaped / process_died / pm_restart
        [Column(Name = "TerminationReason", StringLength = 30)]
        public string? TerminationReason { get; set; }

        // 日志文件相对路径(基于 CliLogRoot)
        [Column(Name = "LogFilePath", StringLength = 500)]
        public string? LogFilePath { get; set; }

        [Column(Name = "LogFileSize")]
        public long LogFileSize { get; set; } = 0;

        // ---- 标准字段 ----

        [Column(Name = "CreatedTime", DbType = "timestamp", InsertValueSql = "now()")]
        public DateTime CreatedTime { get; set; }

        [Column(Name = "UpdatedTime", DbType = "timestamp", IsNullable = true)]
        public DateTime? UpdatedTime { get; set; }

        [Column(Name = "IsDeleted", IsNullable = false, InsertValueSql = "false")]
        public bool IsDeleted { get; set; }
    }
}
