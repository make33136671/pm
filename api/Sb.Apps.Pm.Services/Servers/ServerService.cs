using FreeSql;
using Microsoft.Extensions.Logging;
using SB.Framework.Core.Attribute;
using SB.Framework.Core.FreeSql;
using SB.Framework.Core.Util;
using Sb.Apps.Pm.Models;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Sb.Apps.Pm.Services.Servers
{
    [AutoDependencyInjection]
    public class ServerService : BaseCrudService<Server>, IModuleInitializer
    {
        #region IModuleInitializer

        public string ModuleName => "服务器管理";
        public int InitOrder => 135;
        public Type[] GetEntityTypes() => new[] { typeof(Server), typeof(ProjectServer) };
        public List<MenuDefinition> GetMenus() => new()
        {
            new MenuDefinition
            {
                ParentPath = "/build-release",
                Name = "ServerList",
                Path = "/build-release/server",
                Component = "/server/list/index",
                Title = "服务器管理",
                Icon = "lucide:server",
                OrderNo = 4
            }
        };

        #endregion

        private readonly IFreeSql _fsql;

        public ServerService(IFreeSql fsql, ILogger<ServerService> logger) : base(fsql, logger)
        {
            _fsql = fsql;
        }

        public Result GetList(string? keyword = null, ServerStatus? status = null,
            string field = "", string order = "", int page = 1, int pageSize = 20)
        {
            var query = Select
                .WhereIf(!string.IsNullOrEmpty(keyword), c => c.Name.Contains(keyword!) || c.Host.Contains(keyword!))
                .WhereIf(status.HasValue, c => c.Status == status.Value);
            query = ApplySorting(query, field, order);
            var data = query.Count(out long total).Page(page, pageSize).ToList();
            foreach (var s in data) MaskSensitiveFields(s);
            return Result.Page(data, total);
        }

        public override Result Detail(int id)
        {
            var entity = Select.Where(c => c.Id == id).First();
            if (entity == null) return Result.Fail("服务器不存在!");
            MaskSensitiveFields(entity);
            return Result.Success(entity);
        }

        /// <summary>
        /// 下拉列表用（含部署配置自动填充所需的非敏感字段）
        /// </summary>
        public Result SimpleList()
        {
            var list = Select.OrderBy(c => c.Sort).ToList(c => new
            {
                c.Id, c.Name, c.Host, c.SshPort, c.Status,
                c.BtPanelUrl,
                c.DbType, c.DbHost, c.DbPort,
                c.FtpHost, c.FtpPort,
            });
            return Result.Success(list);
        }

        public Result Create(Server server)
        {
            EncryptSensitiveFields(server);
            server.CreatedTime = DateTime.Now;
            server = this.Insert(server);
            return Result.Success("服务器创建成功!", server);
        }

        public Result Edit(Server server)
        {
            var existing = Select.Where(c => c.Id == server.Id).First();
            if (existing == null) return Result.Fail("服务器不存在!");

            // 敏感字段：****** 或空值保留原加密值，否则重新加密
            server.SshPassword = PreserveSensitive(server.SshPassword, existing.SshPassword);
            server.BtApiKey = PreserveSensitive(server.BtApiKey, existing.BtApiKey);
            server.BtPassword = PreserveSensitive(server.BtPassword, existing.BtPassword);
            server.DbRootPassword = PreserveSensitive(server.DbRootPassword, existing.DbRootPassword);
            server.FtpPassword = PreserveSensitive(server.FtpPassword, existing.FtpPassword);

            server.UpdatedTime = DateTime.Now;
            int count = UpdateDiy.UpdateColumns(c => new
            {
                c.Name, c.Description, c.Status, c.Tags,
                c.Provider, c.ProviderContact, c.Price, c.PaymentCycle, c.ExpiryDate,
                c.ServerType, c.IsVirtual, c.PhysicalHost,
                c.Host, c.SshPort, c.SshUsername, c.SshPassword,
                c.BtPanelUrl, c.BtApiKey, c.BtUsername, c.BtPassword,
                c.DbType, c.DbHost, c.DbPort, c.DbRootUsername, c.DbRootPassword, c.DbVersion,
                c.FtpHost, c.FtpPort, c.FtpUsername, c.FtpPassword,
                c.Sort, c.UpdatedTime
            }).SetSource(server).ExecuteAffrows();

            return count > 0 ? Result.Success("服务器更新成功!") : Result.Fail("更新失败!");
        }

        /// <summary>
        /// 解密单个敏感字段
        /// </summary>
        public Result DecryptField(int serverId, string fieldName)
        {
            var allowed = new HashSet<string> { "SshPassword", "BtApiKey", "BtPassword", "DbRootPassword", "FtpPassword" };
            if (!allowed.Contains(fieldName)) return Result.Fail("不允许解密该字段!");

            var server = Select.Where(c => c.Id == serverId).First();
            if (server == null) return Result.Fail("服务器不存在!");

            var encrypted = fieldName switch
            {
                "SshPassword" => server.SshPassword,
                "BtApiKey" => server.BtApiKey,
                "BtPassword" => server.BtPassword,
                "DbRootPassword" => server.DbRootPassword,
                "FtpPassword" => server.FtpPassword,
                _ => null
            };

            if (string.IsNullOrEmpty(encrypted)) return Result.Success("");

            try
            {
                var decrypted = EncryptionUtil.AesDecrypt(encrypted);
                return Result.Success(decrypted);
            }
            catch
            {
                return Result.Success(encrypted); // 未加密的旧数据直接返回
            }
        }

        /// <summary>
        /// 通过宝塔 API 自动探测服务器信息
        /// </summary>
        public async Task<Result> DiscoverFromBaota(int serverId)
        {
            var server = Select.Where(c => c.Id == serverId).First();
            if (server == null) return Result.Fail("服务器不存在!");
            if (string.IsNullOrEmpty(server.BtPanelUrl) || string.IsNullOrEmpty(server.BtApiKey))
                return Result.Fail("请先配置宝塔面板地址和 API Key!");

            string apiKey;
            try { apiKey = EncryptionUtil.AesDecrypt(server.BtApiKey); }
            catch { apiKey = server.BtApiKey; }

            var panelUrl = server.BtPanelUrl.TrimEnd('/');
            var discoveryLog = new Dictionary<string, object>();

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

            try
            {
                // 获取系统信息
                var sysResult = await CallBtApi(http, panelUrl, "/system?action=GetSystemTotal", apiKey);
                if (sysResult != null)
                    discoveryLog["system"] = sysResult;

                // 获取数据库列表
                var dbResult = await CallBtApi(http, panelUrl, "/data?action=getData&table=databases", apiKey);
                if (dbResult != null)
                {
                    discoveryLog["databases"] = dbResult;
                    // 自动填充数据库类型
                    if (server.DbType == null)
                    {
                        server.DbType = DatabaseType.MySQL;
                        server.DbHost = "127.0.0.1";
                        server.DbPort = 3306;
                    }
                }

                // 获取 FTP 列表
                var ftpResult = await CallBtApi(http, panelUrl, "/data?action=getData&table=ftps", apiKey);
                if (ftpResult != null)
                {
                    discoveryLog["ftps"] = ftpResult;
                    if (string.IsNullOrEmpty(server.FtpHost))
                    {
                        server.FtpHost = server.Host;
                        server.FtpPort = 21;
                    }
                }

                // 获取站点列表
                var siteResult = await CallBtApi(http, panelUrl, "/data?action=getData&table=sites", apiKey);
                if (siteResult != null)
                    discoveryLog["sites"] = siteResult;

                server.LastDiscoveryTime = DateTime.Now;
                server.DiscoveryLog = JsonSerializer.Serialize(discoveryLog);
                server.UpdatedTime = DateTime.Now;

                _fsql.Update<Server>().Where(c => c.Id == serverId)
                    .Set(c => c.DbType, server.DbType)
                    .Set(c => c.DbHost, server.DbHost)
                    .Set(c => c.DbPort, server.DbPort)
                    .Set(c => c.FtpHost, server.FtpHost)
                    .Set(c => c.FtpPort, server.FtpPort)
                    .Set(c => c.LastDiscoveryTime, server.LastDiscoveryTime)
                    .Set(c => c.DiscoveryLog, server.DiscoveryLog)
                    .Set(c => c.UpdatedTime, server.UpdatedTime)
                    .ExecuteAffrows();

                return Result.Success("探测完成!", discoveryLog);
            }
            catch (Exception ex)
            {
                return Result.Fail($"探测失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 测试连通性
        /// </summary>
        public async Task<Result> TestConnection(int serverId)
        {
            var server = Select.Where(c => c.Id == serverId).First();
            if (server == null) return Result.Fail("服务器不存在!");

            var results = new Dictionary<string, string>();

            // 测试宝塔面板
            if (!string.IsNullOrEmpty(server.BtPanelUrl))
            {
                try
                {
                    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                    var resp = await http.GetAsync(server.BtPanelUrl);
                    results["BtPanel"] = $"HTTP {(int)resp.StatusCode}";
                }
                catch (Exception ex)
                {
                    results["BtPanel"] = $"失败: {ex.Message}";
                }
            }

            // 测试 SSH 端口
            try
            {
                using var tcp = new System.Net.Sockets.TcpClient();
                await tcp.ConnectAsync(server.Host, server.SshPort);
                results["SSH"] = "连通";
            }
            catch (Exception ex)
            {
                results["SSH"] = $"失败: {ex.Message}";
            }

            return Result.Success("连通性测试完成", results);
        }

        #region 私有方法

        private static void EncryptSensitiveFields(Server server)
        {
            if (!string.IsNullOrEmpty(server.SshPassword))
                server.SshPassword = EncryptionUtil.AesEncrypt(server.SshPassword);
            if (!string.IsNullOrEmpty(server.BtApiKey))
                server.BtApiKey = EncryptionUtil.AesEncrypt(server.BtApiKey);
            if (!string.IsNullOrEmpty(server.BtPassword))
                server.BtPassword = EncryptionUtil.AesEncrypt(server.BtPassword);
            if (!string.IsNullOrEmpty(server.DbRootPassword))
                server.DbRootPassword = EncryptionUtil.AesEncrypt(server.DbRootPassword);
            if (!string.IsNullOrEmpty(server.FtpPassword))
                server.FtpPassword = EncryptionUtil.AesEncrypt(server.FtpPassword);
        }

        private static void MaskSensitiveFields(Server server)
        {
            if (!string.IsNullOrEmpty(server.SshPassword)) server.SshPassword = "******";
            if (!string.IsNullOrEmpty(server.BtApiKey)) server.BtApiKey = "******";
            if (!string.IsNullOrEmpty(server.BtPassword)) server.BtPassword = "******";
            if (!string.IsNullOrEmpty(server.DbRootPassword)) server.DbRootPassword = "******";
            if (!string.IsNullOrEmpty(server.FtpPassword)) server.FtpPassword = "******";
        }

        private static string? PreserveSensitive(string? newValue, string? existingEncrypted)
        {
            if (string.IsNullOrEmpty(newValue) || newValue == "******")
                return existingEncrypted;
            return EncryptionUtil.AesEncrypt(newValue);
        }

        private static async Task<object?> CallBtApi(HttpClient http, string panelUrl, string endpoint, string apiKey)
        {
            var requestTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            var apiKeyMd5 = Md5(apiKey);
            var requestToken = Md5(requestTime + apiKeyMd5);

            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["request_time"] = requestTime,
                ["request_token"] = requestToken,
            });

            var resp = await http.PostAsync($"{panelUrl}{endpoint}", content);
            var json = await resp.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<object>(json);
        }

        private static string Md5(string input)
        {
            var bytes = MD5.HashData(Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(bytes).ToLower();
        }

        #endregion
    }
}
