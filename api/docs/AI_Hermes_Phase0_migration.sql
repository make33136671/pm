-- Hermes×PM 工作流 v2 · Phase 0 数据迁移
-- 为 sb_pm_task_item 增加 AutoAssign / Source / SourceRef 三列(模型已加,AutoSyncStructure=false 不自动建列)。
-- 注意:部署对应版本 PM 前执行。RequirementSource 扩 Hermes 值无需 DDL(Requirement.Source 列已存在,仅新增可选值)。

-- ===== MySQL（生产 docker: sbpm_docker @ 192.168.10.61）=====
ALTER TABLE `sb_pm_task_item`
  ADD COLUMN `AutoAssign` TINYINT(1)   NOT NULL DEFAULT 0,
  ADD COLUMN `Source`     VARCHAR(20)  NULL,
  ADD COLUMN `SourceRef`  VARCHAR(500) NULL;

-- ===== PostgreSQL（开发: sbpm @ 192.168.10.62）=====
-- ALTER TABLE sb_pm_task_item
--   ADD COLUMN "AutoAssign" boolean      NOT NULL DEFAULT false,
--   ADD COLUMN "Source"     varchar(20)  NULL,
--   ADD COLUMN "SourceRef"  varchar(500) NULL;
