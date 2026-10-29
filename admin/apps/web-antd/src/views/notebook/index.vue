<script lang="ts" setup>
import { onMounted, ref, computed } from 'vue';

import { Page } from '@vben/common-ui';

import {
  Card,
  Row,
  Col,
  Button,
  Input,
  Modal,
  Popconfirm,
  Tag,
  Typography,
  Space,
  message,
} from 'ant-design-vue';

import { useVbenForm } from '#/adapter/form';

import { getList, postEdit, postDel, postPin } from './api';

const TypographyParagraph = Typography.Paragraph;

interface NoteItem {
  id: number;
  title: string;
  content: string;
  color: string;
  tags: string;
  isPinned: boolean;
  createdTime: string;
  updatedTime: string;
}

const notes = ref<NoteItem[]>([]);
const searchText = ref('');
const modalVisible = ref(false);
const editingId = ref<number>(0);

const filteredNotes = computed(() => {
  if (!searchText.value) return notes.value;
  const keyword = searchText.value.toLowerCase();
  return notes.value.filter(
    (n) =>
      n.title?.toLowerCase().includes(keyword) ||
      n.content?.toLowerCase().includes(keyword),
  );
});

const colorOptions = [
  { label: '白色', value: '#ffffff' },
  { label: '橙色', value: '#fff3e0' },
  { label: '绿色', value: '#e8f5e9' },
  { label: '蓝色', value: '#e3f2fd' },
  { label: '粉色', value: '#fce4ec' },
];

const [NoteForm, noteFormApi] = useVbenForm({
  commonConfig: {
    colon: true,
    componentProps: {
      class: 'w-full',
    },
  },
  handleSubmit: onFormSubmit,
  layout: 'horizontal',
  schema: [
    {
      fieldName: 'title',
      component: 'Input',
      label: '标题',
      rules: 'required',
      componentProps: { placeholder: '请输入标题' },
    },
    {
      fieldName: 'content',
      component: 'Textarea',
      label: '内容',
      componentProps: {
        placeholder: '请输入内容',
        rows: 8,
      },
    },
    {
      fieldName: 'color',
      component: 'Select',
      label: '颜色',
      defaultValue: '#ffffff',
      componentProps: {
        placeholder: '请选择颜色',
        options: colorOptions,
      },
    },
    {
      fieldName: 'tags',
      component: 'Input',
      label: '标签',
      componentProps: { placeholder: '逗号分隔' },
    },
  ],
  wrapperClass: 'grid-cols-1',
});

async function loadNotes() {
  const result: any = await getList({ page: 1, pageSize: 200 });
  notes.value = result.items || [];
}

onMounted(() => {
  loadNotes();
});

function handleAdd() {
  editingId.value = 0;
  noteFormApi.resetForm();
  modalVisible.value = true;
}

function handleEditNote(note: NoteItem) {
  editingId.value = note.id;
  noteFormApi.setValues({
    title: note.title,
    content: note.content,
    color: note.color || '#ffffff',
    tags: note.tags,
  });
  modalVisible.value = true;
}

function onFormSubmit(values: Record<string, any>) {
  if (editingId.value > 0) {
    values.id = editingId.value;
  }
  postEdit(values).then(() => {
    message.success('保存成功');
    modalVisible.value = false;
    loadNotes();
  });
}

function handleModalOk() {
  noteFormApi.submitForm();
}

function handleDelete(id: number) {
  postDel(id).then(() => {
    message.success('删除成功');
    loadNotes();
  });
}

function handlePin(id: number) {
  postPin(id).then(() => {
    loadNotes();
  });
}

function parseTags(tags: string): string[] {
  if (!tags) return [];
  return tags
    .split(',')
    .map((t) => t.trim())
    .filter(Boolean);
}

function formatDate(dateStr: string): string {
  if (!dateStr) return '';
  return new Date(dateStr).toLocaleString('zh-CN');
}
</script>

<template>
  <Page auto-content-height>
    <div style="padding: 16px;">
      <div
        style="
          display: flex;
          align-items: center;
          justify-content: space-between;
          margin-bottom: 16px;
        "
      >
        <Space>
          <Button type="primary" @click="handleAdd">新建笔记</Button>
        </Space>
        <Input
          v-model:value="searchText"
          placeholder="搜索笔记..."
          style="width: 300px;"
          allow-clear
        />
      </div>

      <Row :gutter="[16, 16]">
        <Col
          v-for="note in filteredNotes"
          :key="note.id"
          :span="6"
        >
          <Card
            :hoverable="true"
            :style="{
              borderTop: `3px solid ${note.color || '#ffffff'}`,
              backgroundColor: note.color || '#ffffff',
            }"
          >
            <template #title>
              <div
                style="
                  display: flex;
                  align-items: center;
                  justify-content: space-between;
                "
              >
                <span style=" font-size: 15px;font-weight: bold;">
                  {{ note.title }}
                </span>
                <span
                  v-if="note.isPinned"
                  style=" font-size: 14px;color: #1890ff;"
                >&#x1F4CC;</span>
              </div>
            </template>
            <template #extra>
              <Space>
                <a @click="handleEditNote(note)">编辑</a>
                <a @click="handlePin(note.id)">
                  {{ note.isPinned ? '取消置顶' : '置顶' }}
                </a>
                <Popconfirm
                  title="确定删除吗？"
                  @confirm="handleDelete(note.id)"
                >
                  <a style="color: #ff4d4f;">删除</a>
                </Popconfirm>
              </Space>
            </template>

            <TypographyParagraph
              :ellipsis="{ rows: 2 }"
              :content="note.content"
              style="margin-bottom: 8px; color: #666;"
            />

            <div v-if="note.tags" style="margin-bottom: 8px;">
              <Tag
                v-for="tag in parseTags(note.tags)"
                :key="tag"
                style="margin-bottom: 4px;"
              >
                {{ tag }}
              </Tag>
            </div>

            <div style="font-size: 12px; color: #999;">
              {{ formatDate(note.updatedTime || note.createdTime) }}
            </div>
          </Card>
        </Col>
      </Row>

      <div
        v-if="filteredNotes.length === 0"
        style="
          padding: 60px 0;
          font-size: 14px;
          color: #999;
          text-align: center;
        "
      >
        暂无笔记
      </div>
    </div>

    <Modal
      v-model:open="modalVisible"
      :title="editingId > 0 ? '编辑笔记' : '新建笔记'"
      :width="600"
      @ok="handleModalOk"
      @cancel="modalVisible = false"
    >
      <NoteForm />
    </Modal>
  </Page>
</template>
