using System.Diagnostics;
using System.IO.Compression;
using Microsoft.Extensions.Logging;
using SB.Framework.Core.Attribute;
using Sb.Apps.Pm.Models;

namespace Sb.Apps.Pm.Services.Pipelines
{
    /// <summary>
    /// 本机构建源(BuildSource.LocalBuild)
    /// 在子项目工作目录执行 profile.BuildCommand,把 ArtifactDir 打包成 zip 供部署
    /// </summary>
    [AutoDependencyInjection]
    public class LocalBuildService
    {
        private readonly ILogger<LocalBuildService> _logger;

        public LocalBuildService(ILogger<LocalBuildService> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// 同步构建:执行构建命令 → 打包产物目录 → 返回临时 zip 路径
        /// </summary>
        /// <param name="profile">流水线方案(提供 BuildCommand / ArtifactDir)</param>
        /// <param name="workingDir">构建工作目录(子项目本地路径)</param>
        /// <param name="version">版本号,用于命名 zip</param>
        /// <param name="logs">日志收集</param>
        public async Task<string> BuildAsync(PipelineProfile profile, string workingDir, string version, List<string> logs)
        {
            if (string.IsNullOrWhiteSpace(workingDir) || !Directory.Exists(workingDir))
                throw new DirectoryNotFoundException($"构建工作目录不存在: {workingDir}");
            if (string.IsNullOrWhiteSpace(profile.BuildCommand))
                throw new InvalidOperationException("未配置 BuildCommand(本机构建命令)");

            logs.Add($"[构建] 工作目录: {workingDir}");
            logs.Add($"[构建] 命令: {profile.BuildCommand}");

            var (exitCode, output) = await RunShellAsync(workingDir, profile.BuildCommand!, profile.BuildTimeout * 60_000);
            if (!string.IsNullOrEmpty(output))
                logs.Add(TrimLog(output));
            if (exitCode != 0)
                throw new InvalidOperationException($"构建命令退出码 {exitCode}");

            // 定位产物目录
            var artifactDirRel = string.IsNullOrWhiteSpace(profile.ArtifactDir) ? "." : profile.ArtifactDir!.Trim();
            var artifactDir = Path.GetFullPath(Path.Combine(workingDir, artifactDirRel));
            if (!Directory.Exists(artifactDir))
                throw new DirectoryNotFoundException($"产物目录不存在: {artifactDir}");

            // 打包为 zip
            var tempDir = Path.Combine(Path.GetTempPath(), "sb_pm_artifacts");
            Directory.CreateDirectory(tempDir);
            var safeVer = version.Replace('/', '_').Replace(':', '_');
            var zipPath = Path.Combine(tempDir, $"local_{profile.Id}_{safeVer}.zip");
            if (File.Exists(zipPath)) File.Delete(zipPath);

            ZipFile.CreateFromDirectory(artifactDir, zipPath, CompressionLevel.Fastest, includeBaseDirectory: false);
            var size = new FileInfo(zipPath).Length;
            logs.Add($"[构建] 产物已打包: {Path.GetFileName(zipPath)} ({size / 1024} KB)");

            _logger.LogInformation("LocalBuild profile #{Id} 构建完成: {Zip}", profile.Id, zipPath);
            return zipPath;
        }

        /// <summary>
        /// 在工作目录执行 shell 命令,返回 (退出码, 合并的 stdout+stderr)
        /// </summary>
        private async Task<(int exitCode, string output)> RunShellAsync(string workingDir, string command, int timeoutMs)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                WorkingDirectory = workingDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            // 必须用 ArgumentList 逐个传 argv,让 command 作为单一参数原样进 `bash -c`。
            // 不能用 Arguments 字符串 + 单引号转义:.NET 解析 Arguments 走 Windows 规则、只认双引号,
            // 单引号被当普通字符,`-lc 'pnpm build:antd'` 会被误拆成 -lc / 'pnpm / build:antd',
            // bash 收到未闭合单引号脚本直接语法错误、退出码 2(本机构建一直失败的真因)。
            psi.ArgumentList.Add("-lc");
            psi.ArgumentList.Add(command);

            using var process = new Process { StartInfo = psi };
            process.Start();

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            var completed = await Task.Run(() => process.WaitForExit(timeoutMs));
            if (!completed)
            {
                try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
                throw new TimeoutException($"构建超时({timeoutMs / 1000}s)");
            }

            var stdout = await outputTask;
            var stderr = await errorTask;
            var merged = string.Join("\n", new[] { stdout, stderr }.Where(s => !string.IsNullOrWhiteSpace(s)));
            return (process.ExitCode, merged);
        }

        private static string TrimLog(string s, int max = 4000)
            => s.Length <= max ? s : "...(略)\n" + s[^max..];
    }
}
