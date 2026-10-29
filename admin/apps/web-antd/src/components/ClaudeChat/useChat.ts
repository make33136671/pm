import { reactive, ref } from 'vue';
import type { ChatContext, ChatMessage, ChatState } from './types';

const state = reactive<ChatState>({
  messages: [],
  sessionId: null,
  loading: false,
  visible: false,
  context: {},
});

const abortController = ref<AbortController | null>(null);

function genId(): string {
  return Date.now().toString(36) + Math.random().toString(36).slice(2, 7);
}

export function useChat() {
  function open(context?: ChatContext) {
    if (context) {
      state.context = context;
    }
    state.visible = true;
  }

  function close() {
    state.visible = false;
  }

  function toggle(context?: ChatContext) {
    if (state.visible) {
      close();
    } else {
      open(context);
    }
  }

  async function send(message: string) {
    if (!message.trim() || state.loading) return;

    // add user message
    state.messages.push({
      id: genId(),
      role: 'user',
      content: message,
      timestamp: new Date(),
    });

    // add placeholder for assistant
    state.messages.push({
      id: genId(),
      role: 'assistant',
      content: '',
      timestamp: new Date(),
    });

    // get reactive reference via array index
    const msgIndex = state.messages.length - 1;

    state.loading = true;
    const controller = new AbortController();
    abortController.value = controller;

    try {
      const resp = await fetch('/claude-api/chat', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          message,
          sessionId: state.sessionId,
          projectId: state.context.projectId,
          requirementId: state.context.requirementId,
          documentId: state.context.documentId,
          cwd: state.context.cwd,
        }),
        signal: controller.signal,
      });

      if (!resp.ok || !resp.body) {
        state.messages[msgIndex]!.content =
          '连接失败，请检查 pm-claude 服务是否启动。';
        state.loading = false;
        return;
      }

      const reader = resp.body.getReader();
      const decoder = new TextDecoder();
      let buffer = '';
      let eventType = '';

      while (true) {
        const { done, value } = await reader.read();
        if (done) break;

        buffer += decoder.decode(value, { stream: true });
        const lines = buffer.split('\n');
        buffer = lines.pop() || '';

        for (const line of lines) {
          if (line.startsWith('event: ')) {
            eventType = line.slice(7).trim();
          } else if (line.startsWith('data: ')) {
            const dataStr = line.slice(6);
            try {
              const data = JSON.parse(dataStr);
              handleEvent(eventType, data, msgIndex);
            } catch {
              // skip
            }
          }
        }
      }
    } catch (err: unknown) {
      if (err instanceof DOMException && err.name === 'AbortError') {
        // user stopped
      } else {
        state.messages[msgIndex]!.content += '\n\n[连接中断]';
      }
    } finally {
      state.loading = false;
      abortController.value = null;
    }
  }

  function handleEvent(
    eventType: string,
    data: Record<string, unknown>,
    msgIndex: number,
  ) {
    const msg = state.messages[msgIndex]!;
    switch (eventType) {
      case 'session':
        state.sessionId = (data.sessionId as string) || state.sessionId;
        break;
      case 'content':
        msg.content += (data.text as string) || '';
        break;
      case 'result':
        // result contains full text, use it if content is empty
        if (!msg.content && data.text) {
          msg.content = data.text as string;
        }
        break;
      case 'error':
        msg.content += `\n\n[错误: ${data.message}]`;
        break;
      case 'done':
        if (data.sessionId) {
          state.sessionId = data.sessionId as string;
        }
        break;
    }
  }

  function stop() {
    if (abortController.value) {
      abortController.value.abort();
    }
    // also notify server
    if (state.sessionId) {
      fetch('/claude-api/chat/stop', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ sessionId: state.sessionId }),
      }).catch(() => {});
    }
  }

  function clearMessages() {
    state.messages = [];
    state.sessionId = null;
  }

  return {
    state,
    open,
    close,
    toggle,
    send,
    stop,
    clearMessages,
  };
}
