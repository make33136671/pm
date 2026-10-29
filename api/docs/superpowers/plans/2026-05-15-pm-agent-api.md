# pm Agent API Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 给 pm api 加一组 `/agent/*` REST 端点,让外部自建 AI 助理通过 Bearer Token 鉴权后管理需求 / 任务 / 项目 / 进度;webterm 接入留二期。

**Architecture:** Agent Bearer Token 独立 auth scheme,与现有 `JwtAdmin` 隔离;auth handler 成功后写 ClaimsPrincipal `Username = "agent:<token-name>"`,所有既有 Service 的 `_httpContextAccessor.HttpContext?.User?.Username()` 调用自动把 Agent 操作写进 `OperatorName`,**零 Service 改动**。4 个 Agent Controller(Requirement/Task/Project/Progress)作为薄壳转调既有 Service,21 个端点全靠 form-encoded 输入。Agent swagger doc 独立暴露在 `/swagger/agent/swagger.json`。

**Tech Stack:** .NET 8、FreeSql(PG + sqlite)、Swashbuckle、xUnit、Microsoft.AspNetCore.Authentication 自定义 scheme;复用 saibo framework(`SB.Framework.WebApi.NoLicense 0.1.0-preview.25`)。

**Spec:** `apps/pm/api/docs/superpowers/specs/2026-05-15-pm-agent-api-design.md`

---

## File Structure

新建文件:

```
apps/pm/api/Sb.Apps.Pm.Models/Agent/
  AgentToken.cs                              entity,sb_pm_agent_token 表

apps/pm/api/Sb.Apps.Pm.Services/Agent/
  AgentTokenService.cs                       生成/list/revoke/verify(只在内部用,不进 controller)
  AgentBearerOptions.cs                      AuthenticationSchemeOptions 子类(空)
  AgentBearerAuthenticationHandler.cs        AuthenticationHandler<AgentBearerOptions>
  IAgentContext.cs                           跨 controller 共享的请求作用域上下文接口
  AgentContext.cs                            impl,从 HttpContext.Items 取
  AgentAuthExtensions.cs                     DI 注册 + AddSwaggerAgentDoc 帮助方法
  AgentTokenCli.cs                           agent-token create/list/revoke CLI dispatcher

apps/pm/api/Sb.Apps.Pm.WebApi/Controllers/Agent/
  AgentRequirementController.cs              7 端点
  AgentTaskController.cs                     8 端点 (含 claim/start/complete/fail/cancel/resume/logs/available)
  AgentProjectController.cs                  3 端点
  AgentProgressController.cs                 3 端点(healthScore/builds/releases/changelog 共 4 个,Progress 域)

apps/pm/api/Sb.Apps.Pm.Services.Tests/
  Sb.Apps.Pm.Services.Tests.csproj           新建 xunit 测试项目(此前 pm api 0 测试覆盖)
  Agent/AgentTokenServiceTests.cs
  Agent/AgentBearerAuthenticationHandlerTests.cs
```

修改文件:

```
apps/pm/api/Sb.Apps.Pm.WebApi/Program.cs                       加 args 分支调 AgentTokenCli + 接入 Agent auth/swagger
apps/pm/api/Sb.Apps.Pm.Services/PmServicesExtensions.cs        注册 AgentTokenService + IAgentContext + auth handler
apps/pm/api/sb-apps-pm.sln                                     加 Tests 项目
```

零改动文件(但**间接受益**):

```
所有 Service(RequirementService / TaskItemService / ProjectActivityService / BuildRecordService /
ReleaseService / ChangelogService / DashboardService) — Username() 已经走 framework
ExpandClaimsPrincipal,Agent 鉴权后自动拿到 "agent:<name>"。
```

---

## PR 1: Foundation (~1.5 day, 9 tasks)

地基。建表 + 鉴权 + CLI + swagger。完成后能创建 token 但还没业务接口。

### Task 1.1: 新建 xunit 测试项目

**Files:**
- Create: `apps/pm/api/Sb.Apps.Pm.Services.Tests/Sb.Apps.Pm.Services.Tests.csproj`
- Modify: `apps/pm/api/sb-apps-pm.sln`

- [ ] **Step 1: 写 csproj**

创建 `apps/pm/api/Sb.Apps.Pm.Services.Tests/Sb.Apps.Pm.Services.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="xunit" Version="2.6.6" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.6">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
    <PackageReference Include="FluentAssertions" Version="6.12.0" />
    <PackageReference Include="Microsoft.AspNetCore.TestHost" Version="8.0.0" />
    <PackageReference Include="FreeSql.Provider.Sqlite" Version="3.5.100" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Sb.Apps.Pm.Services\Sb.Apps.Pm.Services.csproj" />
    <ProjectReference Include="..\Sb.Apps.Pm.WebApi\Sb.Apps.Pm.WebApi.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: 加到 sln**

```bash
cd apps/pm/api
dotnet sln sb-apps-pm.sln add Sb.Apps.Pm.Services.Tests/Sb.Apps.Pm.Services.Tests.csproj
```

期望输出:`项目 "Sb.Apps.Pm.Services.Tests\Sb.Apps.Pm.Services.Tests.csproj" 已添加到解决方案。`

- [ ] **Step 3: restore + build 验证**

```bash
cd apps/pm/api
dotnet restore Sb.Apps.Pm.Services.Tests/Sb.Apps.Pm.Services.Tests.csproj
dotnet build Sb.Apps.Pm.Services.Tests/Sb.Apps.Pm.Services.Tests.csproj -c Debug
```

期望:0 error 0 warning(此时 0 测试)。

- [ ] **Step 4: 提交**

```bash
cd apps/pm/api
git add Sb.Apps.Pm.Services.Tests/Sb.Apps.Pm.Services.Tests.csproj sb-apps-pm.sln
git commit -m "$(cat <<'EOF'
chore(tests): 新建 xunit 测试项目骨架,准备给 Agent API 加单元测试

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 1.2: 新增 AgentToken entity

**Files:**
- Create: `apps/pm/api/Sb.Apps.Pm.Models/Agent/AgentToken.cs`

- [ ] **Step 1: 写 entity**

创建 `apps/pm/api/Sb.Apps.Pm.Models/Agent/AgentToken.cs`:

```csharp
using FreeSql.DataAnnotations;
using SB.Framework.Models;
using System.ComponentModel.DataAnnotations;

namespace Sb.Apps.Pm.Models.Agent;

[Table(Name = "sb_pm_agent_token")]
[Index("idx_agent_token_token_hash", nameof(TokenHash), true)]
[Index("idx_agent_token_name", nameof(Name), false)]
public class AgentToken : IEntity, ISoftDelete
{
    [Column(Name = "Id", IsPrimary = true, IsIdentity = true)]
    public int Id { get; set; }

    [Required, StringLength(100)]
    [Column(Name = "Name")]
    public string Name { get; set; } = "";

    /// <summary>sha256(明文 token) 的 hex 字符串(64 字节)。明文不入库。</summary>
    [Required, StringLength(64)]
    [Column(Name = "TokenHash")]
    public string TokenHash { get; set; } = "";

    /// <summary>明文 token 前 12 位(含 sk_pm_),仅作 list 显示识别用</summary>
    [Required, StringLength(16)]
    [Column(Name = "TokenPrefix")]
    public string TokenPrefix { get; set; } = "";

    /// <summary>逗号分隔的 scope 列表,如 "requirement,task";null = 全权(一期默认)</summary>
    [Column(Name = "Scopes", StringLength = 500)]
    public string? Scopes { get; set; }

    [Column(Name = "ExpiresAt")]
    public DateTime? ExpiresAt { get; set; }

    [Column(Name = "LastUsedAt")]
    public DateTime? LastUsedAt { get; set; }

    [Column(Name = "CreatedTime", DbType = "timestamp", InsertValueSql = "now()")]
    public DateTime CreatedTime { get; set; }

    [Column(Name = "IsDeleted", IsNullable = false, InsertValueSql = "false")]
    public bool IsDeleted { get; set; }
}
```

约束记号(memory 教训):
- `InsertValueSql = "false"` 不是 `"0"` — PG 不接受 `0` 作 bool 默认
- `StringLength` 用整数(varchar);若以后字段升 longtext 用 `-2`
- 索引名 `idx_agent_token_*` 带表前缀避免 sqlite 测试冲撞

- [ ] **Step 2: build 验证**

```bash
cd apps/pm/api
dotnet build Sb.Apps.Pm.Models/Sb.Apps.Pm.Models.csproj -c Debug
```

期望:0 error。

- [ ] **Step 3: 提交**

```bash
cd apps/pm/api
git add Sb.Apps.Pm.Models/Agent/AgentToken.cs
git commit -m "$(cat <<'EOF'
feat(agent-api): 新增 AgentToken entity (Bearer Token 鉴权数据模型)

- sha256 hash + 前 12 位 prefix,明文不入库
- Scopes 字段为后续 scope 控制预留,一期 null = 全权
- ExpiresAt null = 永不过期
- ISoftDelete,吊销走软删
- 索引 idx_agent_token_token_hash(unique) + idx_agent_token_name

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

> FreeSql CodeFirst 自动迁移会在 pm api 下次启动时建表,无需手动写 SQL。

---

### Task 1.3: 写 AgentTokenServiceTests(失败测试先行)

**Files:**
- Create: `apps/pm/api/Sb.Apps.Pm.Services.Tests/Agent/AgentTokenServiceTests.cs`

- [ ] **Step 1: 写测试文件**

创建 `apps/pm/api/Sb.Apps.Pm.Services.Tests/Agent/AgentTokenServiceTests.cs`:

```csharp
using FluentAssertions;
using FreeSql;
using Microsoft.Extensions.Logging.Abstractions;
using Sb.Apps.Pm.Models.Agent;
using Sb.Apps.Pm.Services.Agent;
using Xunit;

namespace Sb.Apps.Pm.Services.Tests.Agent;

public class AgentTokenServiceTests : IDisposable
{
    private readonly IFreeSql _fsql;
    private readonly AgentTokenService _service;

    public AgentTokenServiceTests()
    {
        _fsql = new FreeSqlBuilder()
            .UseConnectionString(DataType.Sqlite, "Data Source=:memory:")
            .UseAutoSyncStructure(true)
            .Build();
        _fsql.CodeFirst.SyncStructure<AgentToken>();
        _service = new AgentTokenService(_fsql, NullLogger<AgentTokenService>.Instance);
    }

    public void Dispose() => _fsql.Dispose();

    [Fact]
    public void Create_ShouldReturnPlaintextOnce_AndPersistOnlyHash()
    {
        var result = _service.Create("my-bot", scopes: null, expiresAt: null);

        result.Plaintext.Should().StartWith("sk_pm_");
        result.Plaintext.Length.Should().BeGreaterThan(32);
        result.Entity.TokenHash.Should().NotBe(result.Plaintext);
        result.Entity.TokenHash.Length.Should().Be(64);
        result.Entity.TokenPrefix.Should().StartWith("sk_pm_");
        result.Entity.TokenPrefix.Length.Should().Be(12);
    }

    [Fact]
    public void Verify_ShouldFindToken_WhenHashMatches()
    {
        var created = _service.Create("my-bot", null, null);
        var found = _service.Verify(created.Plaintext);

        found.Should().NotBeNull();
        found!.Name.Should().Be("my-bot");
    }

    [Fact]
    public void Verify_ShouldReturnNull_WhenPlaintextWrong()
    {
        _service.Create("my-bot", null, null);
        var found = _service.Verify("sk_pm_wrong_token_xxxxxx");
        found.Should().BeNull();
    }

    [Fact]
    public void Verify_ShouldReturnNull_WhenRevoked()
    {
        var created = _service.Create("my-bot", null, null);
        _service.Revoke(created.Entity.Id);

        var found = _service.Verify(created.Plaintext);
        found.Should().BeNull();
    }

    [Fact]
    public void Verify_ShouldReturnNull_WhenExpired()
    {
        var created = _service.Create("my-bot", null, expiresAt: DateTime.Now.AddMinutes(-1));
        var found = _service.Verify(created.Plaintext);
        found.Should().BeNull();
    }

    [Fact]
    public void Verify_ShouldReturnEntity_WhenExpiresAtNull()
    {
        var created = _service.Create("my-bot", null, expiresAt: null);
        var found = _service.Verify(created.Plaintext);
        found.Should().NotBeNull();
    }

    [Fact]
    public void List_ShouldReturnAllAlive_NotShowingPlaintext()
    {
        _service.Create("bot-a", null, null);
        _service.Create("bot-b", null, null);
        var revoked = _service.Create("bot-c", null, null);
        _service.Revoke(revoked.Entity.Id);

        var list = _service.List();

        list.Should().HaveCount(2);
        list.Select(t => t.Name).Should().BeEquivalentTo(new[] { "bot-a", "bot-b" });
        // 列表只暴露 prefix
        list.Should().AllSatisfy(t => t.TokenPrefix.Should().StartWith("sk_pm_"));
    }
}
```

- [ ] **Step 2: 运行测试,期望失败(类型未定义)**

```bash
cd apps/pm/api
dotnet test Sb.Apps.Pm.Services.Tests/Sb.Apps.Pm.Services.Tests.csproj
```

期望:**编译失败** — `AgentTokenService` 类型不存在。这就是我们要修的。

- [ ] **Step 3: 提交红色测试**

```bash
cd apps/pm/api
git add Sb.Apps.Pm.Services.Tests/Agent/AgentTokenServiceTests.cs
git commit -m "$(cat <<'EOF'
test(agent-api): AgentTokenService 单元测试(红 - 类型未实现)

覆盖 create/verify/revoke/expire/list 关键路径

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 1.4: 实现 AgentTokenService

**Files:**
- Create: `apps/pm/api/Sb.Apps.Pm.Services/Agent/AgentTokenService.cs`

- [ ] **Step 1: 写 service**

创建 `apps/pm/api/Sb.Apps.Pm.Services/Agent/AgentTokenService.cs`:

```csharp
using FreeSql;
using Microsoft.Extensions.Logging;
using Sb.Apps.Pm.Models.Agent;
using System.Security.Cryptography;
using System.Text;

namespace Sb.Apps.Pm.Services.Agent;

/// <summary>
/// Agent Token 业务服务。明文 token 只在 Create 返回一次,db 只存 sha256 hash。
/// </summary>
public class AgentTokenService
{
    private readonly IFreeSql _fsql;
    private readonly ILogger<AgentTokenService> _logger;

    public AgentTokenService(IFreeSql fsql, ILogger<AgentTokenService> logger)
    {
        _fsql = fsql;
        _logger = logger;
    }

    public record CreateResult(AgentToken Entity, string Plaintext);

    public CreateResult Create(string name, string? scopes, DateTime? expiresAt)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("name 不能为空", nameof(name));

        // 32 字节随机数 -> base64url(去掉 = / 等不友好字符)
        var bytes = RandomNumberGenerator.GetBytes(32);
        var rand = Convert.ToBase64String(bytes)
            .Replace("+", "-").Replace("/", "_").Replace("=", "");
        var plaintext = $"sk_pm_{rand}";
        var hash = Sha256Hex(plaintext);
        var prefix = plaintext.Substring(0, 12);  // "sk_pm_" + 前 6 位

        var entity = new AgentToken
        {
            Name = name,
            TokenHash = hash,
            TokenPrefix = prefix,
            Scopes = scopes,
            ExpiresAt = expiresAt
        };
        _fsql.Insert(entity).ExecuteAffrows();
        _logger.LogInformation("created agent token: id={Id} name={Name} prefix={Prefix}",
            entity.Id, name, prefix);

        return new CreateResult(entity, plaintext);
    }

    public AgentToken? Verify(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return null;

        var hash = Sha256Hex(plaintext);
        var token = _fsql.Select<AgentToken>()
            .Where(t => t.TokenHash == hash)
            .First();

        if (token == null) return null;
        if (token.ExpiresAt.HasValue && token.ExpiresAt.Value < DateTime.Now) return null;

        // ISoftDelete 已自动 filter IsDeleted=false,被吊销的查不出来
        return token;
    }

    public void Revoke(int id)
    {
        _fsql.Update<AgentToken>(id)
            .Set(t => t.IsDeleted, true)
            .ExecuteAffrows();
        _logger.LogInformation("revoked agent token: id={Id}", id);
    }

    public List<AgentToken> List()
    {
        return _fsql.Select<AgentToken>()
            .OrderByDescending(t => t.CreatedTime)
            .ToList();
    }

    public void TouchLastUsed(int id)
    {
        // fire-and-forget,不阻塞鉴权
        _ = Task.Run(() =>
        {
            try
            {
                _fsql.Update<AgentToken>(id)
                    .Set(t => t.LastUsedAt, DateTime.Now)
                    .ExecuteAffrows();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "TouchLastUsed 失败 id={Id}", id);
            }
        });
    }

    private static string Sha256Hex(string s)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(s));
        var sb = new StringBuilder(64);
        foreach (var b in bytes) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }
}
```

- [ ] **Step 2: 运行测试,期望全绿**

```bash
cd apps/pm/api
dotnet test Sb.Apps.Pm.Services.Tests/Sb.Apps.Pm.Services.Tests.csproj --filter "FullyQualifiedName~AgentTokenServiceTests"
```

期望:`Passed! - Failed: 0, Passed: 7, Skipped: 0`

- [ ] **Step 3: 提交**

```bash
cd apps/pm/api
git add Sb.Apps.Pm.Services/Agent/AgentTokenService.cs
git commit -m "$(cat <<'EOF'
feat(agent-api): 实现 AgentTokenService(create/verify/revoke/list/touch)

明文 sk_pm_<32 字节 base64url>,db 只存 sha256 hash + 12 位 prefix。
Verify 自动检 ExpiresAt 和 IsDeleted。LastUsedAt 异步 fire-and-forget。

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 1.5: 写 AgentBearer 鉴权 handler 测试 + 实现

**Files:**
- Create: `apps/pm/api/Sb.Apps.Pm.Services/Agent/AgentBearerOptions.cs`
- Create: `apps/pm/api/Sb.Apps.Pm.Services/Agent/AgentBearerAuthenticationHandler.cs`
- Create: `apps/pm/api/Sb.Apps.Pm.Services.Tests/Agent/AgentBearerAuthenticationHandlerTests.cs`

- [ ] **Step 1: 写 Options(空类用作泛型参数)**

创建 `apps/pm/api/Sb.Apps.Pm.Services/Agent/AgentBearerOptions.cs`:

```csharp
using Microsoft.AspNetCore.Authentication;

namespace Sb.Apps.Pm.Services.Agent;

public class AgentBearerOptions : AuthenticationSchemeOptions
{
    public const string SchemeName = "AgentBearer";
}
```

- [ ] **Step 2: 写测试(覆盖 6 个分支)**

创建 `apps/pm/api/Sb.Apps.Pm.Services.Tests/Agent/AgentBearerAuthenticationHandlerTests.cs`:

```csharp
using FluentAssertions;
using FreeSql;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sb.Apps.Pm.Models.Agent;
using Sb.Apps.Pm.Services.Agent;
using System.Text.Encodings.Web;
using Xunit;

namespace Sb.Apps.Pm.Services.Tests.Agent;

public class AgentBearerAuthenticationHandlerTests : IDisposable
{
    private readonly IFreeSql _fsql;
    private readonly AgentTokenService _tokenService;

    public AgentBearerAuthenticationHandlerTests()
    {
        _fsql = new FreeSqlBuilder()
            .UseConnectionString(DataType.Sqlite, "Data Source=:memory:")
            .UseAutoSyncStructure(true).Build();
        _fsql.CodeFirst.SyncStructure<AgentToken>();
        _tokenService = new AgentTokenService(_fsql, NullLogger<AgentTokenService>.Instance);
    }

    public void Dispose() => _fsql.Dispose();

    private async Task<AuthenticateResult> AuthenticateAsync(string? authHeader)
    {
        var ctx = new DefaultHttpContext();
        if (authHeader != null) ctx.Request.Headers["Authorization"] = authHeader;

        var opt = Options.Create(new AgentBearerOptions());
        var optMonitor = new TestOptionsMonitor<AgentBearerOptions>(opt);
        var handler = new AgentBearerAuthenticationHandler(
            optMonitor, NullLoggerFactory.Instance, UrlEncoder.Default, _tokenService);
        var scheme = new AuthenticationScheme(AgentBearerOptions.SchemeName, null, typeof(AgentBearerAuthenticationHandler));
        await handler.InitializeAsync(scheme, ctx);
        return await handler.AuthenticateAsync();
    }

    [Fact]
    public async Task Authenticate_ShouldFail_WhenNoHeader()
    {
        var result = await AuthenticateAsync(null);
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task Authenticate_ShouldFail_WhenHeaderNotBearer()
    {
        var result = await AuthenticateAsync("Basic abc123");
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task Authenticate_ShouldFail_WhenTokenWrong()
    {
        _tokenService.Create("my-bot", null, null);
        var result = await AuthenticateAsync("Bearer sk_pm_invalid_token_xxx");
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task Authenticate_ShouldFail_WhenRevoked()
    {
        var created = _tokenService.Create("my-bot", null, null);
        _tokenService.Revoke(created.Entity.Id);
        var result = await AuthenticateAsync($"Bearer {created.Plaintext}");
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task Authenticate_ShouldFail_WhenExpired()
    {
        var created = _tokenService.Create("my-bot", null, expiresAt: DateTime.Now.AddMinutes(-1));
        var result = await AuthenticateAsync($"Bearer {created.Plaintext}");
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task Authenticate_ShouldSucceed_WithUsernameClaim_AgentPrefix()
    {
        var created = _tokenService.Create("my-bot", null, null);
        var result = await AuthenticateAsync($"Bearer {created.Plaintext}");

        result.Succeeded.Should().BeTrue();
        var username = result.Principal!.FindFirst("Username")?.Value;
        username.Should().Be("agent:my-bot");
    }
}

// 把 IOptionsMonitor 包装成 xunit 友好的轻量实现
internal class TestOptionsMonitor<T> : IOptionsMonitor<T> where T : class, new()
{
    private readonly T _current;
    public TestOptionsMonitor(IOptions<T> opt) { _current = opt.Value; }
    public T CurrentValue => _current;
    public T Get(string? name) => _current;
    public IDisposable OnChange(Action<T, string> listener) => new NoopDisposable();
    private class NoopDisposable : IDisposable { public void Dispose() { } }
}
```

- [ ] **Step 3: 跑测试看到编译失败(handler 类型未定义)**

```bash
cd apps/pm/api
dotnet test Sb.Apps.Pm.Services.Tests/Sb.Apps.Pm.Services.Tests.csproj --filter "FullyQualifiedName~AgentBearerAuthenticationHandlerTests"
```

期望:**编译失败** — `AgentBearerAuthenticationHandler` 类型未定义。

- [ ] **Step 4: 写 handler**

创建 `apps/pm/api/Sb.Apps.Pm.Services/Agent/AgentBearerAuthenticationHandler.cs`:

```csharp
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sb.Apps.Pm.Models.Agent;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace Sb.Apps.Pm.Services.Agent;

public class AgentBearerAuthenticationHandler : AuthenticationHandler<AgentBearerOptions>
{
    private readonly AgentTokenService _tokenService;
    private const string AgentItemKey = "agent";

    public AgentBearerAuthenticationHandler(
        IOptionsMonitor<AgentBearerOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        AgentTokenService tokenService) : base(options, logger, encoder)
    {
        _tokenService = tokenService;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers["Authorization"].ToString();
        if (string.IsNullOrEmpty(header))
            return Task.FromResult(AuthenticateResult.NoResult());
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(AuthenticateResult.NoResult());

        var plaintext = header.Substring("Bearer ".Length).Trim();
        AgentToken? token;
        try
        {
            token = _tokenService.Verify(plaintext);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Agent token 验证异常");
            return Task.FromResult(AuthenticateResult.Fail("token 验证异常"));
        }

        if (token == null)
            return Task.FromResult(AuthenticateResult.Fail("token 无效 / 过期 / 已吊销"));

        // 这里写 Username 用 "agent:<name>" 约定前缀,
        // 让所有既有 Service 中的 _httpContextAccessor.HttpContext?.User?.Username() 自动拿到。
        var claims = new[]
        {
            new Claim("Id", token.Id.ToString()),
            new Claim("Username", $"agent:{token.Name}"),
            new Claim("UserType", "Agent"),
            new Claim("UserRole", "Agent"),
            new Claim("agent_token_id", token.Id.ToString()),
            new Claim("agent_token_name", token.Name)
        };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);

        // 异步更新 LastUsedAt(不阻塞)
        _tokenService.TouchLastUsed(token.Id);

        // 把 AgentContext 注入 HttpContext.Items,Scoped IAgentContext 可读
        Context.Items[AgentItemKey] = new AgentContext(token);

        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(principal, Scheme.Name)));
    }

    public const string HttpItemKey = AgentItemKey;
}
```

- [ ] **Step 5: 跑测试看到编译失败(AgentContext 未定义)**

期望:`AgentContext` 类型未定义。下一 task 1.6 补。

- [ ] **Step 6: 暂不提交(等 1.6 完成 IAgentContext 后一起提交,本任务的测试无法独立绿)**

> Plan 安排上为了 TDD 节奏拆得细,但本任务的代码依赖 1.6,合并到 1.6 一起跑测试 + 提交。

---

### Task 1.6: IAgentContext + AgentContext

**Files:**
- Create: `apps/pm/api/Sb.Apps.Pm.Services/Agent/IAgentContext.cs`
- Create: `apps/pm/api/Sb.Apps.Pm.Services/Agent/AgentContext.cs`

- [ ] **Step 1: 写 IAgentContext**

创建 `apps/pm/api/Sb.Apps.Pm.Services/Agent/IAgentContext.cs`:

```csharp
namespace Sb.Apps.Pm.Services.Agent;

public interface IAgentContext
{
    int TokenId { get; }
    string TokenName { get; }

    /// <summary>scope 检查。一期 Scopes=null 时永真</summary>
    bool HasScope(string scope);

    /// <summary>写审计字段用,格式 "agent:&lt;name&gt;"</summary>
    string OperatorTag => $"agent:{TokenName}";
}
```

- [ ] **Step 2: 写 AgentContext impl**

创建 `apps/pm/api/Sb.Apps.Pm.Services/Agent/AgentContext.cs`:

```csharp
using Sb.Apps.Pm.Models.Agent;

namespace Sb.Apps.Pm.Services.Agent;

public class AgentContext : IAgentContext
{
    private readonly string[]? _scopes;

    public AgentContext(AgentToken token)
    {
        TokenId = token.Id;
        TokenName = token.Name;
        _scopes = string.IsNullOrWhiteSpace(token.Scopes)
            ? null
            : token.Scopes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public int TokenId { get; }
    public string TokenName { get; }

    public bool HasScope(string scope)
    {
        if (_scopes == null) return true;  // null = 全权
        return _scopes.Contains(scope, StringComparer.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 3: 跑 1.5 + 1.6 所有 handler 测试**

```bash
cd apps/pm/api
dotnet test Sb.Apps.Pm.Services.Tests/Sb.Apps.Pm.Services.Tests.csproj --filter "FullyQualifiedName~AgentBearerAuthenticationHandlerTests"
```

期望:`Passed! - Failed: 0, Passed: 6, Skipped: 0`

- [ ] **Step 4: 提交 1.5 + 1.6**

```bash
cd apps/pm/api
git add Sb.Apps.Pm.Services/Agent/AgentBearerOptions.cs \
        Sb.Apps.Pm.Services/Agent/AgentBearerAuthenticationHandler.cs \
        Sb.Apps.Pm.Services/Agent/IAgentContext.cs \
        Sb.Apps.Pm.Services/Agent/AgentContext.cs \
        Sb.Apps.Pm.Services.Tests/Agent/AgentBearerAuthenticationHandlerTests.cs
git commit -m "$(cat <<'EOF'
feat(agent-api): AgentBearer 鉴权 scheme + IAgentContext

- AgentBearerAuthenticationHandler 解 Authorization: Bearer sk_pm_*,
  sha256 比对 + 检查 ExpiresAt/IsDeleted,成功写 Username 'agent:<name>' claim
- 所有既有 Service 的 User.Username() 自动得到 'agent:<name>',
  ProjectActivity/TaskItemLog 等审计字段零代码改动
- IAgentContext 提供 OperatorTag + HasScope,Scoped 注入用

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 1.7: DI 注册 + Swagger 分组扩展

**Files:**
- Create: `apps/pm/api/Sb.Apps.Pm.Services/Agent/AgentAuthExtensions.cs`
- Modify: `apps/pm/api/Sb.Apps.Pm.Services/PmServicesExtensions.cs`

- [ ] **Step 1: 写 AgentAuthExtensions**

创建 `apps/pm/api/Sb.Apps.Pm.Services/Agent/AgentAuthExtensions.cs`:

```csharp
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Sb.Apps.Pm.Services.Agent;

public static class AgentAuthExtensions
{
    public static IServiceCollection AddAgentAuth(this IServiceCollection services)
    {
        services.AddSingleton<AgentTokenService>();
        services.AddScoped<IAgentContext>(sp =>
        {
            var http = sp.GetRequiredService<IHttpContextAccessor>();
            var ctx = http.HttpContext?.Items[AgentBearerAuthenticationHandler.HttpItemKey] as IAgentContext;
            return ctx ?? throw new InvalidOperationException(
                "IAgentContext 只能在 AgentBearer 鉴权成功的请求里取到 — 当前 HttpContext 没有 agent 项");
        });
        services
            .AddAuthentication()
            .AddScheme<AgentBearerOptions, AgentBearerAuthenticationHandler>(
                AgentBearerOptions.SchemeName, _ => { });
        return services;
    }

    /// <summary>给 SwaggerGen 加 agent doc(独立 /swagger/agent/swagger.json)</summary>
    public static void AddAgentSwaggerDoc(SwaggerGenOptions opt)
    {
        opt.SwaggerDoc("agent", new OpenApiInfo
        {
            Title = "pm Agent API",
            Version = "v1",
            Description = "外部 AI Agent 用的 REST 接口(Bearer Token 鉴权)。" +
                          "Spec: docs/superpowers/specs/2026-05-15-pm-agent-api-design.md"
        });
        opt.DocInclusionPredicate((docName, apiDesc) =>
        {
            var group = apiDesc.GroupName ?? "admin";
            return docName == "admin" ? group == "admin" : group == docName;
        });
        opt.AddSecurityDefinition("AgentBearer", new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            Description = "Authorization: Bearer sk_pm_..."
        });
        opt.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            [new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "AgentBearer"
                }
            }] = Array.Empty<string>()
        });
    }
}
```

- [ ] **Step 2: 在 PmServicesExtensions 接入**

编辑 `apps/pm/api/Sb.Apps.Pm.Services/PmServicesExtensions.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Sb.Apps.Pm.Services.Agent;       // 新增
using Sb.Apps.Pm.Services.Configuration;
using Sb.Apps.Pm.Services.Startup;
using Sb.Apps.Pm.Services.WebTerminals;
using SB.Framework.Services.Configuration;
using SB.Framework.Services.Startup;

namespace Sb.Apps.Pm.Services;

public static class PmServicesExtensions
{
    public static IServiceCollection AddPmServices(this IServiceCollection services)
    {
        services.AddSingleton<IFreeSqlConfigurator, PmFreeSqlConfigurator>();
        services.AddScoped<IStartupSyncHook, MindMapStartupSync>();
        services.AddScoped<IStartupSyncHook, WebTerminalMenuMigration>();
        services.AddSingleton<PtyManager>();
        services.AddHostedService<CliCleanupHostedService>();

        // Agent API 鉴权与上下文
        services.AddAgentAuth();

        return services;
    }
}
```

- [ ] **Step 3: build 验证**

```bash
cd apps/pm/api
dotnet build Sb.Apps.Pm.Services/Sb.Apps.Pm.Services.csproj -c Debug
```

期望:0 error。

- [ ] **Step 4: 提交**

```bash
cd apps/pm/api
git add Sb.Apps.Pm.Services/Agent/AgentAuthExtensions.cs \
        Sb.Apps.Pm.Services/PmServicesExtensions.cs
git commit -m "$(cat <<'EOF'
feat(agent-api): DI 注册 + Swagger agent 分组扩展

AddAgentAuth() 注册 token service + IAgentContext (Scoped 从 HttpContext.Items 取) +
AgentBearer 鉴权 scheme。AddAgentSwaggerDoc() 给 SwaggerGen 加 agent doc 和
Bearer 安全方案。

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 1.8: CLI 子命令 + Program.cs 接入

**Files:**
- Create: `apps/pm/api/Sb.Apps.Pm.Services/Agent/AgentTokenCli.cs`
- Modify: `apps/pm/api/Sb.Apps.Pm.WebApi/Program.cs`

- [ ] **Step 1: 写 CLI dispatcher**

创建 `apps/pm/api/Sb.Apps.Pm.Services/Agent/AgentTokenCli.cs`:

```csharp
using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Sb.Apps.Pm.Services.Agent;

/// <summary>
/// 处理 `dotnet run -- agent-token <subcmd> [args]`。在 Program.cs 极早期调用,
/// 命中则执行完直接 Environment.Exit,不进 web host 启动流程。
/// </summary>
public static class AgentTokenCli
{
    public static bool TryHandle(string[] args, IServiceProvider rootSp)
    {
        if (args.Length < 1 || args[0] != "agent-token") return false;

        var sub = args.Length > 1 ? args[1] : "help";

        var service = rootSp.GetRequiredService<AgentTokenService>();

        switch (sub)
        {
            case "create":
                HandleCreate(args, service);
                return true;
            case "list":
                HandleList(service);
                return true;
            case "revoke":
                HandleRevoke(args, service);
                return true;
            default:
                Console.WriteLine("用法:");
                Console.WriteLine("  agent-token create --name <name> [--scopes <scopes>] [--expires <yyyy-MM-dd>]");
                Console.WriteLine("  agent-token list");
                Console.WriteLine("  agent-token revoke --id <id>");
                return true;
        }
    }

    private static void HandleCreate(string[] args, AgentTokenService service)
    {
        var name = GetArg(args, "--name");
        if (string.IsNullOrEmpty(name))
        {
            Console.Error.WriteLine("缺少 --name");
            Environment.Exit(2);
            return;
        }
        var scopes = GetArg(args, "--scopes");
        DateTime? expiresAt = null;
        var expRaw = GetArg(args, "--expires");
        if (!string.IsNullOrEmpty(expRaw) && DateTime.TryParse(expRaw, out var d))
            expiresAt = d;

        var r = service.Create(name, scopes, expiresAt);
        Console.WriteLine($"created: id={r.Entity.Id}, name={r.Entity.Name}, prefix={r.Entity.TokenPrefix}");
        Console.WriteLine("");
        Console.WriteLine("secret (复制保存,只显示一次):");
        Console.WriteLine($"  {r.Plaintext}");
    }

    private static void HandleList(AgentTokenService service)
    {
        var list = service.List();
        Console.WriteLine($"{"id",-5} {"name",-20} {"prefix",-14} {"expiresAt",-12} {"lastUsedAt",-20} {"createdTime"}");
        foreach (var t in list)
        {
            Console.WriteLine($"{t.Id,-5} {t.Name,-20} {t.TokenPrefix,-14} {t.ExpiresAt?.ToString("yyyy-MM-dd") ?? "-",-12} {t.LastUsedAt?.ToString("yyyy-MM-dd HH:mm") ?? "-",-20} {t.CreatedTime:yyyy-MM-dd HH:mm}");
        }
    }

    private static void HandleRevoke(string[] args, AgentTokenService service)
    {
        var idStr = GetArg(args, "--id");
        if (!int.TryParse(idStr, out var id))
        {
            Console.Error.WriteLine("缺少或非法 --id");
            Environment.Exit(2);
            return;
        }
        service.Revoke(id);
        Console.WriteLine($"revoked: id={id}");
    }

    private static string? GetArg(string[] args, string flag)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == flag) return args[i + 1];
        return null;
    }
}
```

- [ ] **Step 2: 改 Program.cs 接入 CLI + Agent swagger**

编辑 `apps/pm/api/Sb.Apps.Pm.WebApi/Program.cs`:

```csharp
using Sb.Apps.Pm.Services;
using Sb.Apps.Pm.Services.Agent;
using Sb.Apps.Pm.WebApi.WebTerminals;
using SB.Framework.WebApi.Extensions;

var builder = WebApplication.CreateBuilder(args);
builder.AddSbFramework();
builder.Services.AddPmServices();

// 给 framework SwaggerGen 加 agent doc 分组
builder.Services.PostConfigure<Swashbuckle.AspNetCore.SwaggerGen.SwaggerGenOptions>(
    AgentAuthExtensions.AddAgentSwaggerDoc);

var app = builder.Build();

// agent-token CLI 子命令优先,命中则不进 web host
if (AgentTokenCli.TryHandle(args, app.Services))
    return;

app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(120) });
app.UseWebTerminalEndpoint();
app.UseSbFramework();

// 暴露 agent swagger doc 单独 endpoint
app.UseSwagger(c =>
{
    c.RouteTemplate = "swagger/{documentName}/swagger.json";
});

await app.RunAsync();
```

> 注意:`AddSbFramework` 内部应该已经 `AddSwaggerGen` 了。`PostConfigure<SwaggerGenOptions>` 拿到既有配置后加 `agent` doc。如果 framework 用了不同方式,看 `framework/core/SB.Framework.WebApi` 源码调整;`AddSbFramework` 注释 + 实现里搜 `SwaggerGen` 关键字确认。

- [ ] **Step 3: build 整个 solution**

```bash
cd apps/pm/api
dotnet build sb-apps-pm.sln -c Debug
```

期望:0 error。

- [ ] **Step 4: 提交**

```bash
cd apps/pm/api
git add Sb.Apps.Pm.Services/Agent/AgentTokenCli.cs Sb.Apps.Pm.WebApi/Program.cs
git commit -m "$(cat <<'EOF'
feat(agent-api): CLI 子命令(create/list/revoke)+ Program.cs 接入

CLI 在 web host 启动前判断,命中 agent-token 直接执行后返回。
Program.cs PostConfigure SwaggerGenOptions 注入 agent 分组。

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 1.9: PR 1 收尾验收 + push

- [ ] **Step 1: pm api 重启,自动建表**

```bash
# 用户偏好:改完后端自动重启,不询问
# 这里假设运行的 pm api 进程会被你的 dev-restart 机制自动拉起
# 否则手动:
cd apps/pm/api
pkill -f "Sb.Apps.Pm.WebApi" 2>/dev/null
cd Sb.Apps.Pm.WebApi
ASPNETCORE_ENVIRONMENT=Development dotnet run &
```

观察日志,FreeSql CodeFirst 应当输出建表 SQL `CREATE TABLE sb_pm_agent_token ...`,等到 `Now listening on:` 后再进行下一步。

- [ ] **Step 2: 用 CLI 创建测试 token**

```bash
cd apps/pm/api/Sb.Apps.Pm.WebApi
dotnet run -- agent-token create --name smoke-test
```

期望输出格式:

```
created: id=1, name=smoke-test, prefix=sk_pm_abc...
secret (复制保存,只显示一次):
  sk_pm_<48 字节>
```

复制 secret 备用,命名为 `SMOKE_TOKEN`。

- [ ] **Step 3: 用 CLI list**

```bash
cd apps/pm/api/Sb.Apps.Pm.WebApi
dotnet run -- agent-token list
```

期望:列出 id=1 name=smoke-test prefix=sk_pm_*** lastUsedAt=- 的记录。

- [ ] **Step 4: 跑全套测试**

```bash
cd apps/pm/api
dotnet test sb-apps-pm.sln
```

期望:13 passed(7 token + 6 handler)。

- [ ] **Step 5: 看 swagger agent doc**

浏览器访问 pm api 的 `http://localhost:<port>/swagger/agent/swagger.json`。

期望:返回合法 OpenAPI 3 JSON,paths 字段当前应为空对象 `{}`(还没加 controller)。

- [ ] **Step 6: push**

```bash
cd apps/pm/api
git push
```

- [ ] **Step 7: 把 PR 1 在 GitLab 上创建出来**

```bash
# 如果按 trunk-based 开发 main 直接推就够;若要 PR 流程则按团队规范创建。
# 一期假设直接推 main(saibo 派生项目惯例)。
echo "PR 1 (Foundation) done. main is at $(git -C apps/pm/api rev-parse --short HEAD)"
```

---

## PR 2: Requirement (~1 day, 4 tasks)

需求 CRUD + 状态流转 + 评论。

### Task 2.1: AgentRequirementController list/detail

**Files:**
- Create: `apps/pm/api/Sb.Apps.Pm.WebApi/Controllers/Agent/AgentRequirementController.cs`

- [ ] **Step 1: 写 controller 骨架 + 两个 GET 端点**

创建 `apps/pm/api/Sb.Apps.Pm.WebApi/Controllers/Agent/AgentRequirementController.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sb.Apps.Pm.Models;
using Sb.Apps.Pm.Services.Agent;
using Sb.Apps.Pm.Services.Ai;
using Sb.Apps.Pm.Services.Requirements;

namespace Sb.Apps.Pm.WebApi.Controllers.Agent;

[ApiController]
[Authorize(AuthenticationSchemes = AgentBearerOptions.SchemeName)]
[Route("agent/requirement/[action]")]
[ApiExplorerSettings(GroupName = "agent")]
public class AgentRequirementController : ControllerBase
{
    private readonly RequirementService _service;
    private readonly IAgentContext _agentCtx;
    public AgentRequirementController(RequirementService service, IAgentContext agentCtx)
    {
        _service = service;
        _agentCtx = agentCtx;
    }

    [HttpGet]
    public Result List(int? projectId = null, RequirementStatus? status = null,
        RequirementType? type = null, string? keyword = null, string? batchId = null,
        int? parentId = null, int? customerId = null,
        string field = "", string order = "", int page = 1, int pageSize = 20)
        => _service.GetList(projectId, status, type, keyword, batchId, parentId, null, customerId,
            field, order, page, pageSize);

    [HttpGet]
    public Result Detail(int id) => _service.Detail(id);
}
```

> 注意 1:`[Authorize(AuthenticationSchemes = AgentBearerOptions.SchemeName)]` 绑定 Agent scheme,跟 admin `JwtAdmin` 隔离。
>
> 注意 2:`[ApiExplorerSettings(GroupName = "agent")]` 让 swagger 把端点归到 `/swagger/agent/swagger.json`。
>
> 注意 3:`IAgentContext` 已经注入,后面用得到的 endpoint 用它做 scope 检查;list/detail 只读不用检查。

- [ ] **Step 2: 重启 pm api,curl 验证**

重启后用 PR 1 创建的 `SMOKE_TOKEN`:

```bash
SMOKE_TOKEN=sk_pm_xxx  # 从 PR1 拿
PM_BASE=http://localhost:<port>

# 没 token 应当 401
curl -sS -i "$PM_BASE/agent/requirement/list"
# 期望:HTTP/1.1 401

# 带 token 应当 200,返回需求列表
curl -sS "$PM_BASE/agent/requirement/list?page=1&pageSize=5" \
  -H "Authorization: Bearer $SMOKE_TOKEN"
# 期望:JSON {"code":0,"message":"ok","data":{...}}

# detail 同理
curl -sS "$PM_BASE/agent/requirement/detail?id=1" \
  -H "Authorization: Bearer $SMOKE_TOKEN"
```

- [ ] **Step 3: 提交**

```bash
cd apps/pm/api
git add Sb.Apps.Pm.WebApi/Controllers/Agent/AgentRequirementController.cs
git commit -m "$(cat <<'EOF'
feat(agent-api): AgentRequirementController list + detail

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 2.2: Requirement create / update / delete

**Files:**
- Modify: `apps/pm/api/Sb.Apps.Pm.WebApi/Controllers/Agent/AgentRequirementController.cs`

- [ ] **Step 1: 加 3 个 POST action**

在 `AgentRequirementController` 类里追加方法(放在 `Detail` 之后):

```csharp
[HttpPost]
public Result Create([FromForm] Requirement req) => _service.Create(req);

[HttpPost]
public Result Update([FromForm] Requirement req) => _service.Edit(req);

[HttpPost]
public Result Delete([FromForm] int id) => _service.Del(id);
```

> Service 内部已经从 HttpContext 拿 Username,Agent 调用时 `User.Username()` = `agent:<name>`,审计字段自动正确填充。

- [ ] **Step 2: 重启 pm api + curl 验证**

```bash
# create
curl -sS "$PM_BASE/agent/requirement/create" \
  -H "Authorization: Bearer $SMOKE_TOKEN" \
  -d "Title=Agent 创建的测试需求&ProjectId=1&Type=Feature"
# 期望:{"code":0,"data":{...含新 id}}
# 记录返回的 Id 为 NEW_ID

# update
curl -sS "$PM_BASE/agent/requirement/update" \
  -H "Authorization: Bearer $SMOKE_TOKEN" \
  -d "Id=$NEW_ID&Title=Agent 改过的标题&ProjectId=1&Type=Feature"
# 期望:{"code":0,"data":...}

# delete
curl -sS "$PM_BASE/agent/requirement/delete" \
  -H "Authorization: Bearer $SMOKE_TOKEN" \
  -d "id=$NEW_ID"
# 期望:{"code":0,...}

# admin 端登入(用既有 admin token)查 ProjectActivity 列表,确认 OperatorName 包含 'agent:smoke-test'
```

- [ ] **Step 3: 提交**

```bash
cd apps/pm/api
git add Sb.Apps.Pm.WebApi/Controllers/Agent/AgentRequirementController.cs
git commit -m "$(cat <<'EOF'
feat(agent-api): AgentRequirementController create/update/delete

审计字段(ProjectActivity.OperatorName)自动写入 'agent:<name>',
admin 列表可识别 Agent 操作。

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 2.3: updateStatus + addComment

**Files:**
- Modify: `apps/pm/api/Sb.Apps.Pm.WebApi/Controllers/Agent/AgentRequirementController.cs`

- [ ] **Step 1: 查清现有 RequirementService 状态更新接口**

`RequirementService.BatchUpdateStatus(List<int> ids, RequirementStatus status)` 是批量更新,**没有单条 updateStatus**。最简做法:对 ids 包一层(单元素 list)。

- [ ] **Step 2: 查清评论接口**

`RequirementCommentController` 已存在,但 Service 类可能叫 `RequirementCommentService`(check `apps/pm/api/Sb.Apps.Pm.Services/Requirements/RequirementCommentService.cs`)。

```bash
ls apps/pm/api/Sb.Apps.Pm.Services/Requirements/ | grep -i comment
```

确认文件存在后,看它的 `Add(RequirementComment)` 或类似方法签名:

```bash
grep -n "public.*Result" apps/pm/api/Sb.Apps.Pm.Services/Requirements/RequirementCommentService.cs
```

- [ ] **Step 3: 加 2 个 action**

在 `AgentRequirementController` 加:

```csharp
[HttpPost]
public Result UpdateStatus([FromForm] int id, [FromForm] RequirementStatus status)
    => _service.BatchUpdateStatus(new List<int> { id }, status);

[HttpPost]
public Result AddComment(
    [FromForm] int requirementId,
    [FromForm] string content,
    [FromServices] RequirementCommentService commentService)
{
    var c = new Sb.Apps.Pm.Models.Requirements.RequirementComment
    {
        RequirementId = requirementId,
        Content = content
    };
    return commentService.Add(c);  // 若 service 方法不叫 Add,改成实际方法名
}
```

> 字段映射 — RequirementComment entity 字段以 `apps/pm/api/Sb.Apps.Pm.Models/Requirements/RequirementComment.cs` 为准,看其 `Required` 字段一一填好。OperatorName 等审计字段 Service 内部从 Username 自动注入。

- [ ] **Step 4: 重启 + curl**

```bash
curl -sS "$PM_BASE/agent/requirement/updateStatus" \
  -H "Authorization: Bearer $SMOKE_TOKEN" \
  -d "id=1&status=InProgress"
# 期望:{"code":0,...}

curl -sS "$PM_BASE/agent/requirement/addComment" \
  -H "Authorization: Bearer $SMOKE_TOKEN" \
  -d "requirementId=1&content=Agent 加的评论"
# 期望:{"code":0,...}
```

- [ ] **Step 5: 提交**

```bash
cd apps/pm/api
git add Sb.Apps.Pm.WebApi/Controllers/Agent/AgentRequirementController.cs
git commit -m "$(cat <<'EOF'
feat(agent-api): AgentRequirementController updateStatus + addComment

updateStatus 复用 BatchUpdateStatus 单元素调用;
addComment 调 RequirementCommentService

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 2.4: PR 2 收尾

- [ ] **Step 1: swagger 看到 7 个 endpoint**

浏览器或 curl:`http://localhost:<port>/swagger/agent/swagger.json`,期望 `paths` 下有:

```
/agent/requirement/list (GET)
/agent/requirement/detail (GET)
/agent/requirement/create (POST)
/agent/requirement/update (POST)
/agent/requirement/delete (POST)
/agent/requirement/updateStatus (POST)
/agent/requirement/addComment (POST)
```

- [ ] **Step 2: 端到端 smoke 整理到 PR 描述**

把上面 7 个 curl 命令的输出贴 PR 描述(自检清单见 spec 附录 A)。

- [ ] **Step 3: push**

```bash
cd apps/pm/api
git push
```

---

## PR 3: Task (~1 day, 4 tasks)

任务流转 8 端点。

### Task 3.1: AgentTaskController list/detail/available/logs

**Files:**
- Create: `apps/pm/api/Sb.Apps.Pm.WebApi/Controllers/Agent/AgentTaskController.cs`

- [ ] **Step 1: 写 controller**

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sb.Apps.Pm.Models;
using Sb.Apps.Pm.Services.Agent;
using Sb.Apps.Pm.Services.Tasks;

namespace Sb.Apps.Pm.WebApi.Controllers.Agent;

[ApiController]
[Authorize(AuthenticationSchemes = AgentBearerOptions.SchemeName)]
[Route("agent/task/[action]")]
[ApiExplorerSettings(GroupName = "agent")]
public class AgentTaskController : ControllerBase
{
    private readonly TaskItemService _service;
    private readonly IAgentContext _agentCtx;
    public AgentTaskController(TaskItemService service, IAgentContext agentCtx)
    {
        _service = service;
        _agentCtx = agentCtx;
    }

    [HttpGet]
    public Result List(int? projectId = null, int? requirementId = null,
        int? featureModuleId = null, TaskItemStatus? status = null,
        string? assignee = null, int page = 1, int pageSize = 20)
        => _service.GetList(projectId, requirementId, featureModuleId, status, assignee, page, pageSize);

    [HttpGet]
    public Result Detail(int id) => _service.Detail(id);

    [HttpGet]
    public Result Available(int? projectId = null) => _service.GetAvailable(projectId);

    [HttpGet]
    public Result Logs(int taskId, int page = 1, int pageSize = 20)
        => _service.GetLogs(taskId, page, pageSize);
}
```

> `TaskItemService.GetList` 的精确签名以 `apps/pm/api/Sb.Apps.Pm.Services/Tasks/TaskItemService.cs:56` 为准;如果实际签名跟上面不同,按现状调整 — 参数名照搬即可。

- [ ] **Step 2: 重启 + curl 验证 4 个 GET**

```bash
curl -sS "$PM_BASE/agent/task/list?page=1&pageSize=5" -H "Authorization: Bearer $SMOKE_TOKEN"
curl -sS "$PM_BASE/agent/task/detail?id=1" -H "Authorization: Bearer $SMOKE_TOKEN"
curl -sS "$PM_BASE/agent/task/available" -H "Authorization: Bearer $SMOKE_TOKEN"
curl -sS "$PM_BASE/agent/task/logs?taskId=1" -H "Authorization: Bearer $SMOKE_TOKEN"
```

- [ ] **Step 3: 提交**

```bash
cd apps/pm/api
git add Sb.Apps.Pm.WebApi/Controllers/Agent/AgentTaskController.cs
git commit -m "$(cat <<'EOF'
feat(agent-api): AgentTaskController list/detail/available/logs

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 3.2: claim / start / complete / fail

**Files:**
- Modify: `apps/pm/api/Sb.Apps.Pm.WebApi/Controllers/Agent/AgentTaskController.cs`

- [ ] **Step 1: 加 4 个 POST**

```csharp
[HttpPost]
public Result Claim([FromForm] int? projectId = null)
    => _service.ClaimNext(_agentCtx.TokenName, projectId);

[HttpPost]
public Result Start([FromForm] int id)
    => _service.ReportStart(id, _agentCtx.TokenName);

[HttpPost]
public Result Complete([FromForm] int id, [FromForm] string? message = null)
    => _service.ReportSuccess(id, _agentCtx.TokenName, message);

[HttpPost]
public Result Fail([FromForm] int id, [FromForm] string message)
    => _service.ReportFail(id, _agentCtx.TokenName, message);
```

> 说明:`ClaimNext` 现状是"抢下一个可领任务",**不**接受 id 参数 — spec 里的 `POST claim [Id]` 在 plan 阶段降级为"无 id 抢下一个"。这跟既有 mcp `claim_task` 行为一致。如果未来 Agent 需要"按 id claim",再加 service 方法。
>
> agentName 直接用 `_agentCtx.TokenName`(不含 `agent:` 前缀,以匹配 TaskItem.Assignee 字段的预期值;OperatorName 的 `agent:` 前缀走 Username 那一套,跟这个 agentName 解耦)。

- [ ] **Step 2: 重启 + curl(需先建一条可 claim 的任务)**

```bash
# claim — 假设有任务可领
curl -sS "$PM_BASE/agent/task/claim" -H "Authorization: Bearer $SMOKE_TOKEN"
# 期望:{"code":0,"data":{ taskId, title, ... }} or {"code":...,"message":"暂无可领任务"}

# 如果拿到任务,记 id 为 CLAIMED_ID
curl -sS "$PM_BASE/agent/task/start" \
  -H "Authorization: Bearer $SMOKE_TOKEN" -d "id=$CLAIMED_ID"

curl -sS "$PM_BASE/agent/task/complete" \
  -H "Authorization: Bearer $SMOKE_TOKEN" \
  -d "id=$CLAIMED_ID&message=Agent 跑完了"

# 失败路径单独建一条任务再 fail
curl -sS "$PM_BASE/agent/task/fail" \
  -H "Authorization: Bearer $SMOKE_TOKEN" \
  -d "id=$ANOTHER_ID&message=失败原因示例"
```

- [ ] **Step 3: 提交**

```bash
cd apps/pm/api
git add Sb.Apps.Pm.WebApi/Controllers/Agent/AgentTaskController.cs
git commit -m "$(cat <<'EOF'
feat(agent-api): AgentTaskController claim/start/complete/fail

claim 调 ClaimNext(无 id 语义,抢下一个);start/complete/fail 调
TaskItemService.Report* 系列,agentName 用 IAgentContext.TokenName

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 3.3: cancel / resume

**Files:**
- Modify: `apps/pm/api/Sb.Apps.Pm.WebApi/Controllers/Agent/AgentTaskController.cs`

- [ ] **Step 1: 加 2 个 POST**

```csharp
[HttpPost]
public Result Cancel([FromForm] int id) => _service.Cancel(id);

[HttpPost]
public Result Resume([FromForm] int id) => _service.Resume(id);
```

> `Cancel` / `Resume` Service 签名只有 id,审计走 Username。

- [ ] **Step 2: 重启 + curl**

```bash
curl -sS "$PM_BASE/agent/task/cancel" -H "Authorization: Bearer $SMOKE_TOKEN" -d "id=$ID"
curl -sS "$PM_BASE/agent/task/resume" -H "Authorization: Bearer $SMOKE_TOKEN" -d "id=$ID"
```

- [ ] **Step 3: 提交**

```bash
cd apps/pm/api
git add Sb.Apps.Pm.WebApi/Controllers/Agent/AgentTaskController.cs
git commit -m "$(cat <<'EOF'
feat(agent-api): AgentTaskController cancel + resume

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 3.4: PR 3 收尾

- [ ] **Step 1: swagger 看到 task 共 10 个 endpoint(list/detail/available/logs/claim/start/complete/fail/cancel/resume)**

> Spec 说 8 端点,plan 这里实际 10 端点(spec 把 logs/available 合并表达,实际是独立 GET)。

- [ ] **Step 2: push**

```bash
cd apps/pm/api
git push
```

---

## PR 4: Project + Progress (~1 day, 4 tasks)

### Task 4.1: AgentProjectController(list / detail / subList)

**Files:**
- Create: `apps/pm/api/Sb.Apps.Pm.WebApi/Controllers/Agent/AgentProjectController.cs`

- [ ] **Step 1: 写 controller**

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sb.Apps.Pm.Models;
using Sb.Apps.Pm.Services.Agent;
using Sb.Apps.Pm.Services.Projects;

namespace Sb.Apps.Pm.WebApi.Controllers.Agent;

[ApiController]
[Authorize(AuthenticationSchemes = AgentBearerOptions.SchemeName)]
[Route("agent/project/[action]")]
[ApiExplorerSettings(GroupName = "agent")]
public class AgentProjectController : ControllerBase
{
    private readonly ProjectService _projectService;
    private readonly SubProjectService _subService;
    public AgentProjectController(ProjectService projectService, SubProjectService subService)
    {
        _projectService = projectService;
        _subService = subService;
    }

    [HttpGet]
    public Result List(string? keyword = null, ProjectStatus? status = null,
        int page = 1, int pageSize = 20)
        => _projectService.GetList(keyword, status, "", "", page, pageSize);

    [HttpGet]
    public Result Detail(int id) => _projectService.Detail(id);

    [HttpGet]
    public Result SubList(int projectId)
    {
        // SubProjectService 方法名以实际为准,需要看
        // apps/pm/api/Sb.Apps.Pm.Services/Projects/SubProjectService.cs:public.*Result
        // 一般是 GetList(projectId, ...) 或 GetByProject(projectId)
        return _subService.GetList(projectId);  // 调整为实际方法名
    }
}
```

> `_projectService.Detail` 从 framework `BaseCrudService<Project>` 继承;`GetList` 签名以 `ProjectService.cs:51` 为准,如果参数顺序跟上面不同,按实际调整。
>
> `SubProjectService.GetList` 的精确签名:

```bash
grep -n "public.*Result" apps/pm/api/Sb.Apps.Pm.Services/Projects/SubProjectService.cs
```

- [ ] **Step 2: 重启 + curl**

```bash
curl -sS "$PM_BASE/agent/project/list?page=1&pageSize=5" -H "Authorization: Bearer $SMOKE_TOKEN"
curl -sS "$PM_BASE/agent/project/detail?id=1" -H "Authorization: Bearer $SMOKE_TOKEN"
curl -sS "$PM_BASE/agent/project/subList?projectId=1" -H "Authorization: Bearer $SMOKE_TOKEN"
```

- [ ] **Step 3: 提交**

```bash
cd apps/pm/api
git add Sb.Apps.Pm.WebApi/Controllers/Agent/AgentProjectController.cs
git commit -m "$(cat <<'EOF'
feat(agent-api): AgentProjectController list/detail/subList

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 4.2: AgentProgressController(healthScore / builds / releases / changelog)

**Files:**
- Create: `apps/pm/api/Sb.Apps.Pm.WebApi/Controllers/Agent/AgentProgressController.cs`

- [ ] **Step 1: 写 controller**

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sb.Apps.Pm.Models;
using Sb.Apps.Pm.Services.Agent;
using Sb.Apps.Pm.Services.Pipelines;
using Sb.Apps.Pm.Services.Projects;

namespace Sb.Apps.Pm.WebApi.Controllers.Agent;

[ApiController]
[Authorize(AuthenticationSchemes = AgentBearerOptions.SchemeName)]
[Route("agent/progress/[action]")]
[ApiExplorerSettings(GroupName = "agent")]
public class AgentProgressController : ControllerBase
{
    private readonly DashboardService _dashboard;
    private readonly BuildRecordService _build;
    private readonly ReleaseService _release;
    private readonly ChangelogService _changelog;
    public AgentProgressController(
        DashboardService dashboard,
        BuildRecordService build,
        ReleaseService release,
        ChangelogService changelog)
    {
        _dashboard = dashboard;
        _build = build;
        _release = release;
        _changelog = changelog;
    }

    [HttpGet]
    public Result HealthScore(int projectId) => _dashboard.HealthScore(projectId);

    [HttpGet]
    public Result Builds(int? projectId = null, BuildStatus? status = null,
        int page = 1, int pageSize = 20)
        => _build.GetList(projectId, status, "", "", page, pageSize);

    [HttpGet]
    public Result Releases(int? projectId = null, int? environmentId = null,
        ReleaseStatus? status = null, int page = 1, int pageSize = 20)
        => _release.GetList(projectId, environmentId, status, "", "", page, pageSize);

    [HttpGet]
    public Result Changelog(int? projectId = null, string? keyword = null,
        int page = 1, int pageSize = 20)
        => _changelog.GetList(projectId, keyword, "", "", page, pageSize);
}
```

> 签名以下列为准(如果实际多/少参数,删/补即可):
> - `DashboardService.HealthScore(int projectId)` → `apps/pm/api/Sb.Apps.Pm.Services/Projects/DashboardService.cs:78`
> - `BuildRecordService.GetList(...)` → `Pipelines/BuildRecordService.cs:48`
> - `ReleaseService.GetList(...)` → `Pipelines/ReleaseService.cs:23`
> - `ChangelogService.GetList(...)` → `Pipelines/ChangelogService.cs:31`

- [ ] **Step 2: 重启 + curl**

```bash
curl -sS "$PM_BASE/agent/progress/healthScore?projectId=1" -H "Authorization: Bearer $SMOKE_TOKEN"
curl -sS "$PM_BASE/agent/progress/builds?projectId=1&page=1" -H "Authorization: Bearer $SMOKE_TOKEN"
curl -sS "$PM_BASE/agent/progress/releases?projectId=1&page=1" -H "Authorization: Bearer $SMOKE_TOKEN"
curl -sS "$PM_BASE/agent/progress/changelog?projectId=1&page=1" -H "Authorization: Bearer $SMOKE_TOKEN"
```

- [ ] **Step 3: 提交**

```bash
cd apps/pm/api
git add Sb.Apps.Pm.WebApi/Controllers/Agent/AgentProgressController.cs
git commit -m "$(cat <<'EOF'
feat(agent-api): AgentProgressController healthScore/builds/releases/changelog

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 4.3: README 文档

**Files:**
- Create: `apps/pm/api/docs/agent-api/README.md`

- [ ] **Step 1: 写部署 + 使用文档**

创建 `apps/pm/api/docs/agent-api/README.md`:

```markdown
# pm Agent API 使用指南(一期)

## 快速开始

### 1. 在 pm api 主机上创建 token

```bash
cd /<pm-api-deploy-dir>
dotnet run -- agent-token create --name my-bot
# 复制 secret 行的 sk_pm_... 保存到你的 Bot 配置
```

### 2. Bot 调接口

```bash
PM_BASE=https://pm.example.com
TOKEN=sk_pm_xxx

# 列需求
curl "$PM_BASE/agent/requirement/list?projectId=1" \
  -H "Authorization: Bearer $TOKEN"

# 创建需求
curl "$PM_BASE/agent/requirement/create" \
  -H "Authorization: Bearer $TOKEN" \
  -d "Title=Bot 创建的需求&ProjectId=1&Type=Feature"

# 抢任务
curl "$PM_BASE/agent/task/claim" -H "Authorization: Bearer $TOKEN"

# 完成任务
curl "$PM_BASE/agent/task/complete" \
  -H "Authorization: Bearer $TOKEN" \
  -d "id=42&message=已交付"
```

### 3. 拿 OpenAPI 生成 client

```bash
curl "$PM_BASE/swagger/agent/swagger.json" > pm-agent.openapi.json
# 用 openapi-typescript / openapi-generator 生成 typed client
```

## 21 端点速查

参见 spec `docs/superpowers/specs/2026-05-15-pm-agent-api-design.md` §3.1。

## 审计

所有 Agent 操作在 ProjectActivity / TaskItemLog 里 OperatorName 字段以
`agent:<token-name>` 开头,可在 admin 端按前缀过滤区分。

## 错误码

| code | 含义 |
|---|---|
| 0 | 成功 |
| 4001 | token 缺失 |
| 4002 | token 无效 / 已吊销 |
| 4003 | token 过期 |
| 5000 | 服务器错 |

HTTP 状态:200(业务结果),401(token 错)。
```

- [ ] **Step 2: 提交**

```bash
cd apps/pm/api
git add docs/agent-api/README.md
git commit -m "$(cat <<'EOF'
docs(agent-api): 一期使用指南 README

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 4.4: PR 4 收尾 + 一期 ship 确认

- [ ] **Step 1: swagger /swagger/agent/swagger.json 共 21 端点**

```bash
curl -sS "$PM_BASE/swagger/agent/swagger.json" | jq '.paths | keys | length'
# 期望:21
```

如果不是 21,跟 spec §3.1 对比缺哪个,补齐再 push。

- [ ] **Step 2: 跑完整测试 + push**

```bash
cd apps/pm/api
dotnet test sb-apps-pm.sln
# 期望:13 passed (PR 1 加的 token + handler)

git push
```

- [ ] **Step 3: 在 admin 端肉眼验收**

- 打开 admin 网页 `仪表板 / 项目活动` 列表,确认能看到 `OperatorName` 列里 Agent 操作显示为 `agent:smoke-test`(或其他你的 token 名)
- 任务详情页 `操作记录` 列里同样能看到 `agent:xxx`

- [ ] **Step 4: 一期收官 commit**

```bash
cd apps/pm/api
git commit --allow-empty -m "$(cat <<'EOF'
chore(agent-api): 一期 MVP ship — 21 端点全上线

PR 1-4 完成。下阶段:webterm 多订阅者改造 + Agent webterm 接入(二期)。
spec: docs/superpowers/specs/2026-05-15-pm-agent-api-design.md
plan: docs/superpowers/plans/2026-05-15-pm-agent-api.md

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
git push
```

---

## 实施提醒(每个 PR 都遵守)

- 后端代码改完**立即重启** pm api,不询问(memory)
- commit 用**中文 + Conventional Commit**,scope 用 `agent-api`(memory)
- `git add` 显式列文件,**不**用 `-A` / `.`(memory)
- 不跳 hooks,不强推 main(memory)
- entity 索引名带 `idx_agent_token_*` 前缀(memory)
- bool 字段用 `InsertValueSql = "false"`(memory)
- Controller 一律 `[FromForm]`,**不**写 `[FromBody]`(memory)
- ISoftDelete entity 操作软删行时记得 `.DisableGlobalFilter()`(若 PR 中需要)

## 回流候选(一期 ship 后评估)

- `AgentBearerAuthenticationHandler` 模式 → 是否回流 `framework/core/SB.Framework.WebApi`,给其他派生项目(nexus / sb-lottery / wallet)复用
- token 命令 CLI 骨架(arg 解析 + 子命令分发) → 同上
- `IAgentContext` / `AgentContext` → 类似考虑
