export interface ChatMessage {
  id: string;
  role: 'user' | 'assistant';
  content: string;
  timestamp: Date;
}

export interface ChatContext {
  projectId?: number;
  projectName?: string;
  requirementId?: number;
  requirementTitle?: string;
  documentId?: number;
  documentTitle?: string;
  cwd?: string;
}

export interface ChatState {
  messages: ChatMessage[];
  sessionId: string | null;
  loading: boolean;
  visible: boolean;
  context: ChatContext;
}
