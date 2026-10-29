<script lang="ts" setup>
import type { VxeGridProps } from '#/adapter/vxe-table';
import type { VbenFormProps } from '#/adapter/form';

import { Page } from '@vben/common-ui';

import { Tag, Space, Button, Popconfirm, message } from 'ant-design-vue';

import { useVbenVxeGrid } from '#/adapter/vxe-table';

import { getBackupList, postBackupDel } from '../api';
import { getList as getProjectList } from '../../project/api';

interface RowType {
  id: number;
  version: string;
  projectId: number;
  environmentId: number;
  backupPath: string;
  backupSize: number;
  backupType: string;
  checksum: string;
  createdTime: string;
  expireTime: string;
}

const backupTypeColorMap: Record<string, string> = {
  Auto: 'green',
  Manual: 'blue',
  PreRelease: 'orange',
};

const formOptions: VbenFormProps = {
  schema: [
    {
      label: '项目',
      fieldName: 'projectId',
      component: 'ApiSelect',
      componentProps: {
        placeholder: '请选择项目',
        api: getProjectList,
        afterFetch: (data: any) => {
          return data.items.map((item: any) => ({
            label: item.name,
            value: item.id,
          }));
        },
        allowClear: true,
        filterOption: true,
        showSearch: true,
      },
    },
  ],
  wrapperClass: 'grid-cols-1',
  showCollapseButton: false,
  submitOnChange: false,
  submitOnEnter: true,
};

const gridOptions: VxeGridProps<RowType> = {
  columns: [
    { fixed: 'left', field: 'id', title: 'Id', width: 50 },
    { field: 'version', title: '版本号', width: 130 },
    { field: 'projectId', title: '项目ID', width: 80 },
    { field: 'environmentId', title: '环境ID', width: 80 },
    { field: 'backupPath', title: '备份路径', minWidth: 200 },
    {
      field: 'backupSize',
      title: '大小',
      width: 110,
      slots: { default: 'backupSize' },
    },
    {
      field: 'backupType',
      title: '备份类型',
      width: 110,
      slots: { default: 'backupType' },
    },
    {
      field: 'checksum',
      title: 'Checksum',
      width: 140,
      slots: { default: 'checksum' },
    },
    {
      field: 'createdTime',
      title: '创建时间',
      width: 170,
      slots: { default: 'createdTime' },
    },
    {
      field: 'expireTime',
      title: '过期时间',
      width: 170,
      slots: { default: 'expireTime' },
    },
    {
      field: 'action',
      fixed: 'right',
      slots: { default: 'action' },
      title: '操作',
      width: 130,
      align: 'center',
    },
  ],
  height: 'auto',
  keepSource: true,
  pagerConfig: {},
  proxyConfig: {
    ajax: {
      query: async ({ page }, formValues) => {
        return await getBackupList({
          page: page.currentPage,
          pageSize: page.pageSize,
          ...formValues,
        });
      },
    },
  },
  showOverflow: false,
  rowConfig: {
    isHover: true,
  },
  toolbarConfig: {
    refresh: true,
    custom: true,
  },
};

const [Grid, gridApi] = useVbenVxeGrid({
  formOptions,
  gridOptions,
});

function handleDel(id: number) {
  postBackupDel(id).then(() => {
    message.success('删除成功');
    gridApi.query();
  });
}

function formatSize(bytes: number) {
  if (!bytes && bytes !== 0) return '-';
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  if (bytes < 1024 * 1024 * 1024)
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  return `${(bytes / (1024 * 1024 * 1024)).toFixed(2)} GB`;
}

function formatDateTime(dateStr: string) {
  if (!dateStr) return '-';
  return new Date(dateStr).toLocaleString('zh-CN');
}
</script>

<template>
  <Page auto-content-height>
    <Grid>
      <template #backupSize="{ row }">
        {{ formatSize(row.backupSize) }}
      </template>

      <template #backupType="{ row }">
        <Tag :color="backupTypeColorMap[row.backupType] || 'default'">
          {{ row.backupType }}
        </Tag>
      </template>

      <template #checksum="{ row }">
        <span style="font-family: monospace; font-size: 12px">
          {{ row.checksum ? row.checksum.substring(0, 16) : '-' }}
        </span>
      </template>

      <template #createdTime="{ row }">
        {{ formatDateTime(row.createdTime) }}
      </template>

      <template #expireTime="{ row }">
        {{ formatDateTime(row.expireTime) }}
      </template>

      <template #action="{ row }">
        <Space wrap>
          <Popconfirm
            title="确定要删除该备份吗？"
            @confirm="handleDel(row.id)"
          >
            <Button danger>删除</Button>
          </Popconfirm>
        </Space>
      </template>
    </Grid>
  </Page>
</template>
