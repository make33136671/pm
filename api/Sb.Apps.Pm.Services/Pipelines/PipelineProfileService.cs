using FreeSql;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SB.Framework.Core.Attribute;
using SB.Framework.Core.Expand;
using SB.Framework.Core.FreeSql;
using Sb.Apps.Pm.Models;

namespace Sb.Apps.Pm.Services.Pipelines
{
    [AutoDependencyInjection]
    public class PipelineProfileService : BaseCrudService<PipelineProfile>, IModuleInitializer
    {
        #region IModuleInitializer
        public string ModuleName => "流水线配置";
        public int InitOrder => 131;
        public Type[] GetEntityTypes() => new[] { typeof(PipelineProfile) };
        public List<MenuDefinition> GetMenus() => new()
        {
            new MenuDefinition
            {
                ParentPath = "/build-release",
                Name = "PipelineProfileList", Path = "/build-release/pipeline-profile",
                Component = "/pipeline-profile/list/index",
                Title = "流水线配置", Icon = "lucide:settings-2", OrderNo = 0
            },
        };
        #endregion

        private readonly IFreeSql _fsql;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly GitLabApiService _gitLabApiService;
        private readonly BuildRecordService _buildRecordService;
        private readonly LocalBuildService _localBuildService;
        private readonly DeployExecutorService _deployExecutorService;
        private readonly IServiceScopeFactory _scopeFactory;

        public PipelineProfileService(
            IFreeSql fsql,
            ILogger<PipelineProfileService> logger,
            IHttpContextAccessor httpContextAccessor,
            GitLabApiService gitLabApiService,
            BuildRecordService buildRecordService,
            LocalBuildService localBuildService,
            DeployExecutorService deployExecutorService,
            IServiceScopeFactory scopeFactory) : base(fsql, logger)
        {
            _fsql = fsql;
            _httpContextAccessor = httpContextAccessor;
            _gitLabApiService = gitLabApiService;
            _buildRecordService = buildRecordService;
            _localBuildService = localBuildService;
            _deployExecutorService = deployExecutorService;
            _scopeFactory = scopeFactory;
        }

        /// <summary>
        /// 手动加载 PipelineProfile 的多对一导航并拼接。
        /// 规避 FreeSql 3.5.x 对本实体内联 .Include() 的物化 bug:子表列被内联进父表列中间,
        /// 叠加大量同名列(Id/Name/DefaultBranch/CreatedTime/IsDeleted)+ MapType 映射列,
        /// 导致父实体读取列错位 —— Include(SubProject) 会把 DateTime 读进 IsDeleted(bool) 直接崩溃,
        /// Include(Project/GitRepository/Environment) 则静默把 IsEnabled/BuildTimeout 等标量字段读错。
        /// </summary>
        private void LoadNavs(IReadOnlyList<PipelineProfile> profiles,
            bool project = false, bool subProject = false, bool gitRepo = false, bool environment = false)
        {
            if (profiles.Count == 0) return;

            if (project)
            {
                var ids = profiles.Select(p => p.ProjectId).Distinct().ToList();
                var map = _fsql.Select<Project>().Where(x => ids.Contains(x.Id)).ToList().ToDictionary(x => x.Id);
                foreach (var p in profiles)
                    p.Project = map.TryGetValue(p.ProjectId, out var v) ? v : null;
            }
            if (subProject)
            {
                var ids = profiles.Where(p => p.SubProjectId != null).Select(p => p.SubProjectId!.Value).Distinct().ToList();
                var map = ids.Count == 0
                    ? new Dictionary<int, SubProject>()
                    : _fsql.Select<SubProject>().Where(x => ids.Contains(x.Id)).ToList().ToDictionary(x => x.Id);
                foreach (var p in profiles)
                    p.SubProject = p.SubProjectId != null && map.TryGetValue(p.SubProjectId.Value, out var v) ? v : null;
            }
            if (gitRepo)
            {
                var ids = profiles.Select(p => p.GitRepositoryId).Distinct().ToList();
                var map = _fsql.Select<GitRepository>().Where(x => ids.Contains(x.Id)).ToList().ToDictionary(x => x.Id);
                foreach (var p in profiles)
                    p.GitRepository = map.TryGetValue(p.GitRepositoryId, out var v) ? v : null;
            }
            if (environment)
            {
                var ids = profiles.Where(p => p.EnvironmentId != null).Select(p => p.EnvironmentId!.Value).Distinct().ToList();
                var map = ids.Count == 0
                    ? new Dictionary<int, ProjectEnvironment>()
                    : _fsql.Select<ProjectEnvironment>().Where(x => ids.Contains(x.Id)).ToList().ToDictionary(x => x.Id);
                foreach (var p in profiles)
                    p.Environment = p.EnvironmentId != null && map.TryGetValue(p.EnvironmentId.Value, out var v) ? v : null;
            }
        }

        /// <summary>单条版本的导航拼接,见 <see cref="LoadNavs(IReadOnlyList{PipelineProfile}, bool, bool, bool, bool)"/>。</summary>
        private PipelineProfile? LoadNavs(PipelineProfile? profile,
            bool project = false, bool subProject = false, bool gitRepo = false, bool environment = false)
        {
            if (profile != null)
                LoadNavs(new[] { profile }, project, subProject, gitRepo, environment);
            return profile;
        }

        /// <summary>
        /// 获取配置预设列表
        /// </summary>
        public virtual Result GetList(int? projectId = null, int page = 1, int pageSize = 20)
        {
            var list = Select
                .WhereIf(projectId.HasValue, c => c.ProjectId == projectId.Value)
                .OrderBy(c => c.SortOrder)
                .OrderByDescending(c => c.Id)
                .Count(out var total).Page(page, pageSize).ToList();
            LoadNavs(list, project: true, subProject: true, gitRepo: true, environment: true);
            return Result.Page(list, total);
        }

        /// <summary>
        /// 获取详情
        /// </summary>
        public virtual Result GetDetail(int id)
        {
            var entity = LoadNavs(Select.Where(c => c.Id == id).First(),
                project: true, gitRepo: true, environment: true);
            if (entity == null) return Result.Fail("配置不存在");
            return Result.Success(entity);
        }

        /// <summary>
        /// 某子项目的所有部署方案 + 最近一次构建/部署状态(供子项目卡片的部署下拉用)
        /// </summary>
        public virtual Result GetSchemesBySubProject(int subProjectId)
        {
            var profiles = Select
                .Where(c => c.SubProjectId == subProjectId)
                .OrderBy(c => c.SortOrder)
                .OrderByDescending(c => c.Id)
                .ToList();
            LoadNavs(profiles, environment: true);

            var profileIds = profiles.Select(p => p.Id).ToList();
            var lastBuilds = profileIds.Count == 0
                ? new List<BuildRecord>()
                : _fsql.Select<BuildRecord>()
                    .Where(b => b.PipelineProfileId != null && profileIds.Contains(b.PipelineProfileId.Value))
                    .OrderByDescending(b => b.Id)
                    .ToList();

            var list = profiles.Select(p =>
            {
                var lastBuild = lastBuilds.FirstOrDefault(b => b.PipelineProfileId == p.Id);
                var lastRelease = lastBuild == null ? null
                    : _fsql.Select<ReleaseRecord>().Where(r => r.BuildRecordId == lastBuild.Id)
                        .OrderByDescending(r => r.Id).First();
                return new
                {
                    id = p.Id,
                    name = p.Name,
                    environment = p.Environment?.Name,
                    build_source = p.BuildSource.ToString(),
                    deploy_strategy = p.DeployStrategy.ToString(),
                    deploy_mode = p.DeployMode.ToString(),
                    require_approval = p.RequireApproval,
                    default_branch = p.DefaultBranch,
                    server_count = p.DeployServerIds?.Count ?? 0,
                    is_enabled = p.IsEnabled,
                    last_build_id = lastBuild?.Id,
                    last_build_status = lastBuild?.Status.ToString(),
                    last_deploy_id = lastRelease?.Id,
                    last_deploy_status = lastRelease?.Status.ToString(),
                    last_deploy_time = lastRelease?.FinishedAt,
                };
            }).ToList();

            return Result.Success(list);
        }

        // ==================== 一键部署 ====================

        /// <summary>
        /// 一键 CI/CD:建构建记录 → 后台按 BuildSource 出产物 → (无需审批则)直接部署。
        /// 立即返回 build_id,前端用 Status(buildId) 轮询全过程。
        /// </summary>
        public virtual Result OneClickDeploy(int profileId, string? branch = null, string? version = null)
        {
            var profile = LoadNavs(Select.Where(c => c.Id == profileId).First(), subProject: true);
            if (profile == null) return Result.Fail("部署方案不存在");
            if (!profile.IsEnabled) return Result.Fail("部署方案已禁用");
            // DeployMode=None 视为「纯构建」流水线:只出产物不部署,同样从这里一键跑。
            if ((profile.DeployServerIds?.Count ?? 0) == 0 && profile.DeployMode == DeployMode.SbpmDeploy)
                return Result.Fail("该方案未配置目标服务器");

            var actualBranch = branch ?? profile.SubProject?.DefaultBranch ?? profile.DefaultBranch;
            var actualVersion = version ?? GenerateVersion(profile);

            // 一键直发:构建记录直接进 Building,跳过 PendingApproval
            var record = new BuildRecord
            {
                ProjectId = profile.ProjectId,
                PipelineProfileId = profileId,
                EnvironmentId = profile.EnvironmentId,
                Version = actualVersion,
                BuildType = BuildType.Manual,
                TriggerRef = actualBranch,
                Status = BuildStatus.Building,
                StartedAt = DateTime.Now,
                OperatorName = _httpContextAccessor.HttpContext?.User?.Username(),
            };
            record = _buildRecordService.Insert(record);

            // 后台执行(独立 DI 作用域,避免请求结束后 scoped 服务被释放)
            var buildId = record.Id;
            _ = Task.Run(async () =>
            {
                using var scope = _scopeFactory.CreateScope();
                var svc = scope.ServiceProvider.GetRequiredService<PipelineProfileService>();
                try { await svc.RunPipelineAsync(buildId); }
                catch (Exception ex) { svc._logger.LogError(ex, "OneClick 流水线 build #{Id} 异常", buildId); }
            });

            return Result.Success(profile.DeployMode == DeployMode.None ? "构建已启动" : "一键部署已启动", new
            {
                build_id = buildId,
                profile_id = profileId,
                version = actualVersion,
                branch = actualBranch,
                build_source = profile.BuildSource.ToString(),
                deploy_mode = profile.DeployMode.ToString(),
                status = "building",
            });
        }

        /// <summary>
        /// 后台流水线:出产物 → 置构建状态 →(无需审批则)部署。由 OneClickDeploy 在独立作用域调用。
        /// </summary>
        public virtual async Task RunPipelineAsync(int buildId)
        {
            var build = _fsql.Select<BuildRecord>().Where(b => b.Id == buildId).First();
            if (build == null) return;
            var profile = build.PipelineProfileId.HasValue
                ? LoadNavs(Select.Where(c => c.Id == build.PipelineProfileId.Value).First(),
                    subProject: true, gitRepo: true)
                : null;
            if (profile == null) { FailBuild(build, "关联方案不存在"); return; }

            try
            {
                switch (profile.BuildSource)
                {
                    case BuildSource.LocalBuild:
                        {
                            var workingDir = profile.SubProject?.LocalPath
                                ?? _fsql.Select<Project>().Where(p => p.Id == profile.ProjectId).First()?.LocalPath;
                            var logs = new List<string>();
                            var zip = await _localBuildService.BuildAsync(profile, workingDir ?? "", build.Version ?? "0", logs);
                            build.ArtifactPath = zip;
                            build.ArtifactSize = new FileInfo(zip).Length;
                            build.BuildLog = string.Join("\n", logs);
                            MarkBuildSuccess(build);
                            break;
                        }
                    case BuildSource.LastArtifact:
                        {
                            var last = _fsql.Select<BuildRecord>()
                                .Where(b => b.PipelineProfileId == profile.Id && b.Status == BuildStatus.Success && b.Id != buildId)
                                .OrderByDescending(b => b.Id).First();
                            if (last == null) { FailBuild(build, "无可复用的历史成功产物"); return; }
                            build.ArtifactPath = last.ArtifactPath;
                            build.GitLabPipelineId = last.GitLabPipelineId;
                            build.CommitSha = last.CommitSha;
                            build.BuildLog = $"复用构建 #{last.Id} (v{last.Version}) 的产物";
                            MarkBuildSuccess(build);
                            break;
                        }
                    case BuildSource.GitLabCi:
                    default:
                        {
                            if (profile.GitRepository?.RepoId == null) { FailBuild(build, "方案未关联 GitLab 仓库"); return; }
                            var variables = new Dictionary<string, string>();
                            if (profile.PipelineVariables != null)
                                foreach (var kv in profile.PipelineVariables) variables[kv.Key] = kv.Value;
                            if (!string.IsNullOrEmpty(build.Version)) variables["PM_VERSION"] = build.Version;

                            var pipelineResult = await _gitLabApiService.TriggerPipeline(
                                profile.GitRepository.RepoId!, build.TriggerRef ?? profile.DefaultBranch, variables);
                            if (pipelineResult != null && pipelineResult.ContainsKey("id"))
                                build.GitLabPipelineId = Convert.ToInt32(pipelineResult["id"]);
                            build.BuildLog = $"GitLab Pipeline #{build.GitLabPipelineId} 已触发,等待回调...";
                            _fsql.Update<BuildRecord>().SetSource(build).ExecuteAffrows();
                            // GitLabCi 异步:由 webhook(SyncBuildRecordFromPipeline)在成功后调用 AutoDeployAfterBuild
                            return;
                        }
                }
            }
            catch (Exception ex)
            {
                FailBuild(build, ex.Message);
                return;
            }

            // 本机/复用产物:构建已成功 →(启用部署则)进入部署;DeployMode=None 为纯构建,到此收尾。
            if (profile.DeployMode == DeployMode.None) return;
            await StartDeployAsync(build, profile);
        }

        /// <summary>
        /// GitLabCi 构建成功后由 webhook 调用:无需审批则自动部署。
        /// </summary>
        public virtual async Task AutoDeployAfterBuild(int buildId)
        {
            var build = _fsql.Select<BuildRecord>().Where(b => b.Id == buildId).First();
            if (build == null || build.Status != BuildStatus.Success) return;
            var profile = build.PipelineProfileId.HasValue
                ? LoadNavs(Select.Where(c => c.Id == build.PipelineProfileId.Value).First(), gitRepo: true)
                : null;
            if (profile == null || profile.DeployMode == DeployMode.None) return;
            await StartDeployAsync(build, profile);
        }

        /// <summary>
        /// 创建发布记录并执行部署。RequireApproval=true 时仅挂起待审批,不执行。
        /// </summary>
        private async Task StartDeployAsync(BuildRecord build, PipelineProfile profile)
        {
            var release = new ReleaseRecord
            {
                ProjectId = build.ProjectId,
                EnvironmentId = profile.EnvironmentId ?? 0,
                BuildRecordId = build.Id,
                Version = build.Version ?? "",
                Status = profile.RequireApproval ? ReleaseStatus.Pending : ReleaseStatus.Deploying,
                ApprovalStatus = profile.RequireApproval ? ApprovalStatus.Pending : ApprovalStatus.Approved,
                StartedAt = profile.RequireApproval ? null : DateTime.Now,
                OperatorName = build.OperatorName,
            };
            _fsql.Insert(release).ExecuteAffrows();
            release.Id = (int)_fsql.Select<ReleaseRecord>().Max(r => r.Id);

            if (profile.RequireApproval)
            {
                _logger.LogInformation("Release #{Id} 待审批(方案 {Name} 开启了审批门)", release.Id, profile.Name);
                return; // 等待 ApproveDeploy
            }

            if (profile.DeployMode == DeployMode.GitLabDeploy)
            {
                await ExecuteGitLabDeploy(release, build, profile);
            }
            else // SbpmDeploy
            {
                await _deployExecutorService.ExecuteAsync(release.Id);
            }
        }

        private void MarkBuildSuccess(BuildRecord build)
        {
            build.Status = BuildStatus.Success;
            build.FinishedAt = DateTime.Now;
            if (build.StartedAt.HasValue)
                build.Duration = (int)(DateTime.Now - build.StartedAt.Value).TotalSeconds;
            _fsql.Update<BuildRecord>().SetSource(build).ExecuteAffrows();
        }

        private void FailBuild(BuildRecord build, string reason)
        {
            build.Status = BuildStatus.Failed;
            build.FinishedAt = DateTime.Now;
            build.BuildLog = (build.BuildLog == null ? "" : build.BuildLog + "\n") + $"[失败] {reason}";
            _fsql.Update<BuildRecord>().SetSource(build).ExecuteAffrows();
            _logger.LogWarning("BuildRecord #{Id} 失败: {Reason}", build.Id, reason);
        }

        /// <summary>
        /// 新增配置预设
        /// </summary>
        public virtual Result Add(PipelineProfile profile)
        {
            profile.CreatedTime = DateTime.Now;
            profile = this.Insert(profile);
            return Result.Success("创建成功", profile);
        }

        /// <summary>
        /// 编辑配置预设
        /// </summary>
        public virtual Result Edit(PipelineProfile profile)
        {
            var existing = Select.Where(c => c.Id == profile.Id).First();
            if (existing == null) return Result.Fail("配置不存在");

            profile.UpdatedTime = DateTime.Now;
            _fsql.Update<PipelineProfile>().SetSource(profile).IgnoreColumns(c => c.CreatedTime).ExecuteAffrows();
            return Result.Success("更新成功");
        }

        /// <summary>
        /// 删除（软删除）
        /// </summary>
        public virtual Result Del(int id)
        {
            _fsql.Update<PipelineProfile>().Where(c => c.Id == id).Set(c => c.IsDeleted, true).ExecuteAffrows();
            return Result.Success("删除成功");
        }

        /// <summary>
        /// 触发构建 — 创建 BuildRecord（PendingApproval 状态），等待审批
        /// </summary>
        public virtual Result TriggerBuild(int profileId, string? branch = null, string? version = null,
            Dictionary<string, string>? overrideVariables = null)
        {
            // 本方法只用到 profile 的标量字段,无需任何导航;切勿加 .Include()(会污染 IsEnabled 等字段)
            var profile = Select.Where(c => c.Id == profileId).First();
            if (profile == null) return Result.Fail("配置预设不存在");
            if (!profile.IsEnabled) return Result.Fail("配置预设已禁用");

            var actualBranch = branch ?? profile.DefaultBranch;
            var actualVersion = version ?? GenerateVersion(profile);

            var record = new BuildRecord
            {
                ProjectId = profile.ProjectId,
                PipelineProfileId = profileId,
                EnvironmentId = profile.EnvironmentId,
                Version = actualVersion,
                BuildType = BuildType.Manual,
                TriggerRef = actualBranch,
                Status = BuildStatus.PendingApproval,
                OperatorName = _httpContextAccessor.HttpContext?.User?.Username(),
            };
            record = _buildRecordService.Insert(record);

            return Result.Success("构建申请已提交，等待审批", new
            {
                build_id = record.Id,
                version = actualVersion,
                branch = actualBranch,
                status = record.Status.ToString(),
            });
        }

        /// <summary>
        /// 审批通过构建 — 调用 GitLab API 触发 Pipeline
        /// </summary>
        public virtual async Task<Result> ApproveBuild(int buildId, string? comment = null)
        {
            var record = _fsql.Select<BuildRecord>().Where(c => c.Id == buildId).First();
            if (record == null) return Result.Fail("构建记录不存在");
            if (record.Status != BuildStatus.PendingApproval) return Result.Fail("当前状态不可审批");

            var profile = LoadNavs(Select.Where(c => c.Id == record.PipelineProfileId).First(), gitRepo: true);
            if (profile == null) return Result.Fail("关联配置不存在");

            // 记录审批
            SaveApproval("build", buildId, "approve", comment);

            // 本机构建/复用产物:审批通过后走本机流水线(构建 →(按需)部署),不依赖 GitLab。
            if (profile.BuildSource == BuildSource.LocalBuild || profile.BuildSource == BuildSource.LastArtifact)
            {
                record.Status = BuildStatus.Building;
                record.StartedAt = DateTime.Now;
                _fsql.Update<BuildRecord>().SetSource(record).ExecuteAffrows();
                var bid = record.Id;
                _ = Task.Run(async () =>
                {
                    using var scope = _scopeFactory.CreateScope();
                    var svc = scope.ServiceProvider.GetRequiredService<PipelineProfileService>();
                    try { await svc.RunPipelineAsync(bid); }
                    catch (Exception ex) { svc._logger.LogError(ex, "ApproveBuild 本机流水线 build #{Id} 异常", bid); }
                });
                return Result.Success("审批通过，本机构建已启动", new { build_id = buildId, status = "building" });
            }

            // GitLabCi:触发 Pipeline
            if (profile.GitRepository == null) return Result.Fail("关联仓库不存在");

            // 合并变量
            var variables = new Dictionary<string, string>();
            if (profile.PipelineVariables != null)
                foreach (var kv in profile.PipelineVariables) variables[kv.Key] = kv.Value;
            if (!string.IsNullOrEmpty(record.Version))
                variables["PM_VERSION"] = record.Version;

            // 调用 GitLab 触发 Pipeline
            try
            {
                var repoId = profile.GitRepository.RepoId;
                var triggerRef = record.TriggerRef ?? profile.DefaultBranch;
                var pipelineResult = await _gitLabApiService.TriggerPipeline(
                    repoId!, triggerRef, variables);

                // 更新 BuildRecord
                record.Status = BuildStatus.Building;
                record.StartedAt = DateTime.Now;
                if (pipelineResult != null && pipelineResult.ContainsKey("id"))
                    record.GitLabPipelineId = Convert.ToInt32(pipelineResult["id"]);
                _fsql.Update<BuildRecord>().SetSource(record).ExecuteAffrows();

                return Result.Success("审批通过，Pipeline 已触发", new
                {
                    build_id = buildId,
                    status = "building",
                    pipeline_id = record.GitLabPipelineId,
                });
            }
            catch (Exception ex)
            {
                record.Status = BuildStatus.Failed;
                record.BuildLog = $"触发 Pipeline 失败: {ex.Message}";
                record.FinishedAt = DateTime.Now;
                _fsql.Update<BuildRecord>().SetSource(record).ExecuteAffrows();
                return Result.Fail($"触发 Pipeline 失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 驳回构建
        /// </summary>
        public virtual Result RejectBuild(int buildId, string? comment = null)
        {
            var record = _fsql.Select<BuildRecord>().Where(c => c.Id == buildId).First();
            if (record == null) return Result.Fail("构建记录不存在");
            if (record.Status != BuildStatus.PendingApproval) return Result.Fail("当前状态不可驳回");

            record.Status = BuildStatus.Rejected;
            record.FinishedAt = DateTime.Now;
            _fsql.Update<BuildRecord>().SetSource(record).ExecuteAffrows();

            SaveApproval("build", buildId, "reject", comment);
            return Result.Success("已驳回");
        }

        // ==================== 部署 ====================

        /// <summary>
        /// 触发部署 — 创建 ReleaseRecord（Pending 状态），等待审批
        /// </summary>
        public virtual Result TriggerDeploy(int buildId, List<int>? serverIds = null)
        {
            var build = _fsql.Select<BuildRecord>().Where(c => c.Id == buildId).First();
            if (build == null) return Result.Fail("构建记录不存在");
            if (build.Status != BuildStatus.Success) return Result.Fail("构建未成功，无法部署");

            var profile = build.PipelineProfileId.HasValue
                ? Select.Where(c => c.Id == build.PipelineProfileId.Value).First()
                : null;
            if (profile == null) return Result.Fail("关联的配置预设不存在");
            if (profile.DeployMode == DeployMode.None) return Result.Fail("该配置预设未启用部署");

            var release = new ReleaseRecord
            {
                ProjectId = build.ProjectId,
                EnvironmentId = profile.EnvironmentId ?? 0,
                BuildRecordId = buildId,
                Version = build.Version ?? "",
                Status = ReleaseStatus.Pending,
                ApprovalStatus = ApprovalStatus.Pending,
                OperatorName = _httpContextAccessor.HttpContext?.User?.Username(),
            };
            _fsql.Insert(release).ExecuteAffrows();
            release.Id = (int)_fsql.Select<ReleaseRecord>().Max(r => r.Id);

            return Result.Success("部署申请已提交，等待审批", new
            {
                deploy_id = release.Id,
                build_id = buildId,
                version = build.Version,
                status = "pending_approval",
            });
        }

        /// <summary>
        /// 审批通过部署 — 根据 DeployMode 执行部署
        /// </summary>
        public virtual async Task<Result> ApproveDeploy(int releaseId, string? comment = null)
        {
            var release = _fsql.Select<ReleaseRecord>().Where(c => c.Id == releaseId).First();
            if (release == null) return Result.Fail("发布记录不存在");
            if (release.ApprovalStatus != ApprovalStatus.Pending) return Result.Fail("当前状态不可审批");

            var build = _fsql.Select<BuildRecord>().Where(c => c.Id == release.BuildRecordId).First();
            var profile = build?.PipelineProfileId.HasValue == true
                ? LoadNavs(Select.Where(c => c.Id == build.PipelineProfileId.Value).First(), gitRepo: true)
                : null;

            // 记录审批
            release.ApprovalStatus = ApprovalStatus.Approved;
            release.ApprovedBy = _httpContextAccessor.HttpContext?.User?.Username();
            release.ApprovedAt = DateTime.Now;
            release.Status = ReleaseStatus.Deploying;
            release.StartedAt = DateTime.Now;
            _fsql.Update<ReleaseRecord>().SetSource(release).ExecuteAffrows();
            SaveApproval("deploy", releaseId, "approve", comment);

            if (profile == null)
                return Result.Success("审批通过，但未找到配置预设，请手动部署");

            // 根据 DeployMode 执行
            if (profile.DeployMode == DeployMode.GitLabDeploy)
            {
                return await ExecuteGitLabDeploy(release, build!, profile);
            }

            if (profile.DeployMode == DeployMode.SbpmDeploy)
            {
                // 后台真正执行部署(下载/上传产物可能耗时):独立 DI 作用域,避免请求结束后 scoped 服务被释放。
                // 此前这里只翻状态不执行 → 审批通过后永远卡在 Deploying,现接通 DeployExecutorService。
                var rid = release.Id;
                _ = Task.Run(async () =>
                {
                    using var scope = _scopeFactory.CreateScope();
                    var exec = scope.ServiceProvider.GetRequiredService<DeployExecutorService>();
                    try { await exec.ExecuteAsync(rid); }
                    catch (Exception ex) { _logger.LogError(ex, "ApproveDeploy 部署 release #{Id} 异常", rid); }
                });
                return Result.Success("审批通过，SB.PM 部署已启动", new
                {
                    deploy_id = releaseId,
                    status = "deploying",
                    deploy_mode = "sbpm_deploy",
                });
            }

            return Result.Success("审批通过");
        }

        /// <summary>
        /// 驳回部署
        /// </summary>
        public virtual Result RejectDeploy(int releaseId, string? comment = null)
        {
            var release = _fsql.Select<ReleaseRecord>().Where(c => c.Id == releaseId).First();
            if (release == null) return Result.Fail("发布记录不存在");
            if (release.ApprovalStatus != ApprovalStatus.Pending) return Result.Fail("当前状态不可驳回");

            release.ApprovalStatus = ApprovalStatus.Rejected;
            release.ApprovedBy = _httpContextAccessor.HttpContext?.User?.Username();
            release.ApprovedAt = DateTime.Now;
            _fsql.Update<ReleaseRecord>().SetSource(release).ExecuteAffrows();
            SaveApproval("deploy", releaseId, "reject", comment);
            return Result.Success("部署已驳回");
        }

        /// <summary>
        /// GitLabDeploy — 触发第二条 deploy Pipeline
        /// </summary>
        private async Task<Result> ExecuteGitLabDeploy(ReleaseRecord release, BuildRecord build, PipelineProfile profile)
        {
            if (profile.GitRepository == null || string.IsNullOrEmpty(profile.GitRepository.RepoId))
                return Result.Fail("仓库配置不完整");

            var deployRef = profile.DeployPipelineRef ?? profile.DefaultBranch;
            var variables = new Dictionary<string, string>
            {
                { "PM_DEPLOY", "true" },
                { "PM_VERSION", build.Version ?? "" },
                { "PM_BUILD_ID", build.Id.ToString() },
            };
            if (profile.DeployPipelineVars != null)
                foreach (var kv in profile.DeployPipelineVars) variables[kv.Key] = kv.Value;

            try
            {
                var pipelineResult = await _gitLabApiService.TriggerPipeline(
                    profile.GitRepository.RepoId!, deployRef, variables);

                release.DeployLog = $"Deploy Pipeline 已触发: #{pipelineResult["id"]}";
                _fsql.Update<ReleaseRecord>().SetSource(release).ExecuteAffrows();

                return Result.Success("审批通过，Deploy Pipeline 已触发", new
                {
                    deploy_id = release.Id,
                    status = "deploying",
                    deploy_mode = "gitlab_deploy",
                    pipeline_id = pipelineResult["id"],
                });
            }
            catch (Exception ex)
            {
                release.Status = ReleaseStatus.Failed;
                release.DeployLog = $"触发 Deploy Pipeline 失败: {ex.Message}";
                release.FinishedAt = DateTime.Now;
                _fsql.Update<ReleaseRecord>().SetSource(release).ExecuteAffrows();
                return Result.Fail($"触发 Deploy Pipeline 失败: {ex.Message}");
            }
        }

        // ==================== 回滚 ====================

        /// <summary>
        /// 发起回滚 — 创建回滚记录，等待审批
        /// </summary>
        public virtual Result TriggerRollback(int profileId, string? targetVersion = null)
        {
            var profile = Select.Where(c => c.Id == profileId).First();
            if (profile == null) return Result.Fail("配置预设不存在");

            // 查找上一个成功的部署版本
            var query = _fsql.Select<ReleaseRecord>()
                .Where(r => r.ProjectId == profile.ProjectId && r.Status == ReleaseStatus.Success);

            if (!string.IsNullOrEmpty(targetVersion))
                query = query.Where(r => r.Version == targetVersion);

            var target = query.OrderByDescending(r => r.Id).First();
            if (target == null) return Result.Fail("未找到可回滚的版本");

            // 查找当前版本
            var current = _fsql.Select<ReleaseRecord>()
                .Where(r => r.ProjectId == profile.ProjectId && r.Status == ReleaseStatus.Success)
                .OrderByDescending(r => r.Id)
                .First();

            var rollback = new ReleaseRecord
            {
                ProjectId = profile.ProjectId,
                EnvironmentId = profile.EnvironmentId ?? 0,
                BuildRecordId = target.BuildRecordId,
                Version = target.Version,
                Status = ReleaseStatus.Pending,
                ApprovalStatus = ApprovalStatus.Pending,
                RollbackFromId = current?.Id,
                ReleaseNote = $"回滚到 v{target.Version}",
                OperatorName = _httpContextAccessor.HttpContext?.User?.Username(),
            };
            _fsql.Insert(rollback).ExecuteAffrows();
            rollback.Id = (int)_fsql.Select<ReleaseRecord>().Max(r => r.Id);

            SaveApproval("rollback", rollback.Id, "pending", null);

            return Result.Success("回滚申请已提交，等待审批", new
            {
                rollback_id = rollback.Id,
                from_version = current?.Version,
                to_version = target.Version,
                status = "pending_approval",
            });
        }

        // ==================== 查询 ====================

        /// <summary>
        /// 查看历史版本
        /// </summary>
        public virtual Result GetVersions(int profileId, int limit = 10)
        {
            var profile = Select.Where(c => c.Id == profileId).First();
            if (profile == null) return Result.Fail("配置预设不存在");

            var builds = _fsql.Select<BuildRecord>()
                .Where(b => b.PipelineProfileId == profileId)
                .OrderByDescending(b => b.Id)
                .Limit(limit)
                .ToList();

            var buildIds = builds.Select(b => b.Id).ToList();
            var releases = _fsql.Select<ReleaseRecord>()
                .Where(r => buildIds.Contains(r.BuildRecordId ?? 0))
                .ToList();

            var versions = builds.Select(b =>
            {
                var release = releases.FirstOrDefault(r => r.BuildRecordId == b.Id);
                return new
                {
                    version = b.Version,
                    build_id = b.Id,
                    build_status = b.Status.ToString(),
                    deploy_id = release?.Id,
                    deploy_status = release?.Status.ToString(),
                    branch = b.TriggerRef,
                    commit_sha = b.CommitSha,
                    built_at = b.FinishedAt,
                    deployed_at = release?.FinishedAt,
                    can_rollback = b.Status == BuildStatus.Success && release?.Status == ReleaseStatus.Success,
                };
            }).ToList();

            return Result.Success(versions);
        }

        /// <summary>
        /// 获取构建或部署状态
        /// </summary>
        public virtual Result GetStatus(int? buildId = null, int? deployId = null)
        {
            if (buildId.HasValue)
            {
                var build = _fsql.Select<BuildRecord>().Where(c => c.Id == buildId.Value).First();
                if (build == null) return Result.Fail("构建记录不存在");
                // 方案名单独查(不要内联 Include PipelineProfile,见 LoadNavs 注释的 FreeSql 物化 bug)
                var profileName = build.PipelineProfileId.HasValue
                    ? _fsql.Select<PipelineProfile>().Where(c => c.Id == build.PipelineProfileId.Value).First()?.Name
                    : null;
                // 一并带出该构建关联的最近一次部署,前端全程用 buildId 轮询即可
                var rel = _fsql.Select<ReleaseRecord>().Where(r => r.BuildRecordId == build.Id)
                    .OrderByDescending(r => r.Id).First();
                return Result.Success(new
                {
                    build_id = build.Id,
                    profile_name = profileName,
                    version = build.Version,
                    branch = build.TriggerRef,
                    status = build.Status.ToString(),
                    pipeline_id = build.GitLabPipelineId,
                    started_at = build.StartedAt,
                    finished_at = build.FinishedAt,
                    duration = build.Duration,
                    build_log = build.BuildLog,
                    deploy_id = rel?.Id,
                    deploy_status = rel?.Status.ToString(),
                    approval_status = rel?.ApprovalStatus.ToString(),
                    deploy_log = rel?.DeployLog,
                    deploy_finished_at = rel?.FinishedAt,
                });
            }

            if (deployId.HasValue)
            {
                var release = _fsql.Select<ReleaseRecord>().Where(c => c.Id == deployId.Value).First();
                if (release == null) return Result.Fail("发布记录不存在");
                return Result.Success(new
                {
                    deploy_id = release.Id,
                    build_id = release.BuildRecordId,
                    version = release.Version,
                    status = release.Status.ToString(),
                    approval_status = release.ApprovalStatus.ToString(),
                    started_at = release.StartedAt,
                    finished_at = release.FinishedAt,
                });
            }

            return Result.Fail("请指定 buildId 或 deployId");
        }

        /// <summary>
        /// 根据策略生成版本号
        /// </summary>
        private string GenerateVersion(PipelineProfile profile)
        {
            switch (profile.VersionStrategy)
            {
                case VersionStrategy.Timestamp:
                    var today = DateTime.Now.ToString("yyyyMMdd");
                    var count = _fsql.Select<BuildRecord>()
                        .Where(c => c.PipelineProfileId == profile.Id && c.Version!.StartsWith(today))
                        .Count();
                    return $"{today}.{count + 1}";

                case VersionStrategy.AutoIncrement:
                    var lastVersion = _fsql.Select<BuildRecord>()
                        .Where(c => c.PipelineProfileId == profile.Id && c.Version != null)
                        .OrderByDescending(c => c.Id)
                        .First(c => c.Version);
                    if (string.IsNullOrEmpty(lastVersion)) return "1.0.1";
                    var parts = lastVersion.Split('.');
                    if (parts.Length == 3 && int.TryParse(parts[2], out var patch))
                        return $"{parts[0]}.{parts[1]}.{patch + 1}";
                    return $"{lastVersion}.1";

                default: // Semantic — 需要手动传入
                    return "";
            }
        }

        private void SaveApproval(string targetType, int targetId, string action, string? comment)
        {
            _fsql.Insert(new ApprovalRecord
            {
                TargetType = targetType,
                TargetId = targetId,
                Action = action,
                Comment = comment ?? "",
                OperatorName = _httpContextAccessor.HttpContext?.User?.Username() ?? "",
            }).ExecuteAffrows();
        }
    }
}
