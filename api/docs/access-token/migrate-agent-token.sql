-- ============================================================================
-- PM agent token → 框架统一 AccessToken 迁移(PostgreSQL)
--
-- 目标:把 PM 自有的 sb_pm_agent_token 迁成「服务账号(SB_Admin) + API Key(SB_ApiKey)」。
-- 关键:直接搬 TokenHash(两边都是 sha256(明文) 的 hex),已签发的 bot key 明文不变、继续可用,
--       不需要重新发 token(#101/#107 hermes-agent 等平滑保活)。
--
-- 前置:framework 已升级到含 SB.Feature.AccessToken 的版本并完成建表(SB_ApiKey / SB_Admin.AccountType)。
-- 幂等:可重复执行(NOT EXISTS 去重)。执行前请备份。
-- 注意:SB_Admin / SB_ApiKey 由 FreeSql 建表 → 标识符大小写敏感,必须加双引号;
--       源表 sb_pm_agent_token 为全小写表名,列名是混合大小写(同样加引号)。
-- ============================================================================

BEGIN;

-- Step 1: 为每个 agent token 建一个服务账号(username = 'agent:'<name>,沿用原审计前缀约定)。
--   RoleId=0(无菜单角色,服务账号本就不能登录后台,角色仅影响前端可见性);
--   Password 写随机不可匹配值;AccountType=1(Service)→ AdminService 登录处直接拒绝。
--   left(...,50):Username 列上限 50,名字过长则截断。
INSERT INTO "SB_Admin" ("Username","Password","RoleId","Name","Status","AccountType","IsDeleted","CreatedTime")
SELECT DISTINCT
    left('agent:' || t."Name", 50),
    md5(random()::text) || md5(random()::text),
    0,
    t."Name",
    0,
    1,
    false,
    now()
FROM sb_pm_agent_token t
WHERE t."IsDeleted" = false
  AND NOT EXISTS (
      SELECT 1 FROM "SB_Admin" a WHERE a."Username" = left('agent:' || t."Name", 50)
  );

-- Step 2: 每个 agent token 迁成一把 API Key(搬 TokenHash),关联到上面建的服务账号。
--   FullAccess=false(agent 一律受限);Scopes 原样搬(框架 ScopeFilter 语义与原 AgentScope 一致)。
INSERT INTO "SB_ApiKey"
    ("AccountId","Name","TokenHash","TokenPrefix","Scopes","FullAccess",
     "ExpiresAt","LastUsedAt","RateLimitPerMinute","Notes","CreatedBy","CreatedTime","IsDeleted")
SELECT
    a."Id",
    t."Name",
    t."TokenHash",
    t."TokenPrefix",
    t."Scopes",
    false,
    t."ExpiresAt",
    t."LastUsedAt",
    t."RateLimitPerMinute",
    t."Notes",
    coalesce(t."CreatedBy", 'migration'),
    now(),
    false
FROM sb_pm_agent_token t
JOIN "SB_Admin" a ON a."Username" = left('agent:' || t."Name", 50)
WHERE t."IsDeleted" = false
  AND NOT EXISTS (
      SELECT 1 FROM "SB_ApiKey" k WHERE k."TokenHash" = t."TokenHash"
  );

-- 校验:迁移条数应与有效 agent token 数一致
-- SELECT
--   (SELECT count(*) FROM sb_pm_agent_token WHERE "IsDeleted"=false) AS src,
--   (SELECT count(*) FROM "SB_ApiKey")                                 AS migrated;

COMMIT;

-- 迁移并验证(用旧 sk_ 明文调 /agent/* 通过)后,可归档源表:
--   ALTER TABLE sb_pm_agent_token RENAME TO sb_pm_agent_token_migrated_bak;
