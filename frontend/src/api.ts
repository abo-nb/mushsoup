export interface UserPrefs {
  shuffleOptions: string
  submitMode: string
}

export interface User {
  id: string
  username: string
  displayName: string
  avatarUrl: string | null
  classId: string | null
  prefs: UserPrefs
}

export interface VerifyMember {
  id: string
  username: string
  globalName: string | null
  nickname: string | null
  displayName: string
  avatarUrl: string | null
}

export interface VerifyMembersResult {
  count: number
  members: VerifyMember[]
}

export type VerificationDelivery = 'dm' | 'thread'

export interface VerificationTicket {
  ticketId: string
  delivery: VerificationDelivery
  expiresAt: string
}

export interface BankSettings {
  defaultShuffleOptions: boolean
  requireReview: boolean
}

export interface Bank {
  id: string
  classId: string
  ownerId: string
  name: string
  description: string | null
  visibility: string
  settings: BankSettings
  memberIds: string[]
  tags: string[]
  year: string
  semester: string
  questionCount: number
  createdAt: string
  updatedAt: string
}

export interface RecentItem {
  kind: 'bank' | 'note'
  itemId: string
  lastOpenedAt: string
  openCount: number
}

export interface FavoriteItem {
  kind: 'bank' | 'note'
  itemId: string
  createdAt: string
}

export interface ShortLink {
  code: string
  kind: 'bank' | 'note'
  targetId: string
}

export interface GuestAnswer {
  correct: boolean
  correctKeys: string[]
  stem: string
  explanation: string
}

export interface BankAccess {
  bank: Bank
  isOwner: boolean
  isMember: boolean
  canView: boolean
  canEdit: boolean
}

export interface QuestionOption {
  key: string
  text: string
  correct: boolean
}

export interface DistractorRule {
  poolId: string
  count: number
}

export interface Question {
  id: string
  bankId: string
  type: string
  stem: string
  options: QuestionOption[]
  shuffle: string
  tags: string[]
  chapterPath: string[]
  difficulty: number
  source: string | null
  explanation: string
  status: string
  unreviewed: boolean
  version: number
  createdBy: string
  createdAt: string
  updatedAt: string
  distractorRule: DistractorRule | null
}

export interface QuestionSummary {
  id: string
  stem: string
  type: string
  tags: string[]
  chapterPath: string[]
  difficulty: number
  optionCount: number
  status: string
  updatedAt: string
  usesPool: boolean
}

export interface OptionInput {
  key: string
  text: string
}

export interface SaveQuestionRequest {
  type: string
  stem: string
  options: OptionInput[]
  answer: string[]
  shuffle?: string
  tags?: string[]
  chapterPath?: string[]
  difficulty?: number
  source?: string
  explanation?: string
  distractorRule?: DistractorRule
}

export interface OptionPool {
  id: string
  bankId: string
  name: string
  description: string | null
  entryCount: number
  availableCount: number
  createdAt: string
}

export interface PoolEntry {
  id: string
  poolId: string
  text: string
  tags: string[]
  disabled: boolean
  createdAt: string
}

export interface PoolDetail {
  pool: OptionPool
  entries: PoolEntry[]
}

export interface PoolHealth {
  poolId: string
  name: string
  entryCount: number
  availableCount: number
  referencingQuestions: number
  requiredEntries: number
  canSample: boolean
  warnings: string[]
}

export interface ImportIssue {
  code: string
  message: string
  severity: string
  index?: number | null
}

export interface ImportQuestionPreview {
  index: number
  clientId: string | null
  stem: string
  status: string
  issues: ImportIssue[]
  poolName?: string | null
  poolCount?: number | null
}

export interface ImportBankChange {
  name: string | null
  description: string | null
  visibility: string | null
  tags: string[] | null
  year: string | null
  semester: string | null
  defaultShuffleOptions: boolean | null
  requireReview: boolean | null
}

export interface ImportPoolChange {
  name: string
  mode: string
  delete: boolean
  add: number
  enable: number
  disable: number
  total: number
}

export interface ImportSummary {
  bank: ImportBankChange | null
  pools: ImportPoolChange[]
}

export interface ImportPreview {
  total: number
  valid: number
  warnings: number
  errors: number
  questions: ImportQuestionPreview[]
  batchIssues: ImportIssue[]
  sampleCards: Card[]
  planId: string
  summary?: ImportSummary | null
}

export interface ImportJob {
  jobId: string
  status: 'running' | 'done' | 'failed'
  total: number
  processed: number
  imported: number
  skipped: number
  failed: number
  pending: number
  batchId: string | null
  error: string | null
  createdAt: string
  finishedAt: string | null
}

export interface ImportResult {
  batchId: string
  total: number
  imported: number
  skipped: number
  failed: number
  batchIssues: ImportIssue[]
  items: ImportQuestionPreview[]
}

export interface ImportBatchSummary {
  id: string
  bankId: string
  createdBy: string
  createdAt: string
  total: number
  imported: number
  skipped: number
  failed: number
  pending: number
}

export interface ImportPrompt {
  bankName: string
  prompt: string
  tagCount: number
}

export interface SessionStats {
  correct: number
  wrong: number
}

export interface Session {
  id: string
  bankId: string
  bankName: string
  mode: string
  total: number
  answered: number
  mastered: number
  remaining: number
  startedAt: string
  finishedAt: string | null
  stats: SessionStats
}

export interface CardOption {
  key: string
  label: string
  text: string
}

export interface Card {
  questionId: string
  type: string
  stem: string
  options: CardOption[]
  seed: string
}

export interface NextCard {
  session: Session
  card: Card | null
  index: number
  /** 随卡下发的正确答案，用于本地即时判分。 */
  correctKeys: string[]
  explanation: string
}

export interface FeedbackOption {
  key: string
  text: string
  state: string
}

export interface AnswerFeedback {
  correct: boolean
  score: number
  stem: string
  options: FeedbackOption[]
  explanation: string
  rating: string
  index: number
  finished: boolean
  stats: SessionStats
  session: Session
  /** 服务端已经准备好的下一题，省掉一次「下一题」请求。 */
  next: NextCard | null
}

export interface SubmitAnswerBody {
  questionId: string
  selectedKeys: string[]
  elapsedMs: number
  confidence?: string | null
}

export interface CreateBankBody {
  name: string
  description?: string
  tags?: string[]
  year?: string
  semester?: string
}

export interface UpdateBankBody {
  name?: string
  description?: string
  visibility?: string
  settings?: BankSettings
  tags?: string[]
  year?: string
  semester?: string
}

export interface NoteNotice {
  tone: string
  text: string
}

/** 讲义块是开放结构：只用 type 判别，其余字段在渲染时安全取值。 */
export interface NoteBlock {
  type: string
  [key: string]: unknown
}

export interface NoteSection {
  id: string
  label?: string
  title: string
  subtitle?: string
  blocks: NoteBlock[]
}

export interface NoteDocument {
  formatVersion: number
  title: string
  subtitle?: string
  tags?: string[]
  visibility?: string
  year?: string
  semester?: string
  notice?: NoteNotice
  sections: NoteSection[]
}

export interface NoteSummary {
  id: string
  ownerId?: string
  title: string
  subtitle?: string | null
  tags?: string[]
  visibility?: string
  year?: string
  semester?: string
  chapters?: string[]
  sectionCount?: number
  createdAt?: string
  updatedAt?: string
}

export interface Note extends NoteSummary {
  content: NoteDocument
}

export interface UpdateNoteMetaBody {
  visibility?: string
  tags?: string[]
  year?: string
  semester?: string
}

export interface NoteImportPrompt {
  prompt: string
}

export interface BankChapterIndex {
  bankId: string
  chapters: string[]
}

export interface LeaderboardWeights {
  accuracy: number
  contributions: number
  volume: number
}

export interface LeaderboardEntry {
  userId: string
  displayName: string
  avatarUrl: string | null
  answered: number
  correct: number
  accuracy: number
  questionsCreated: number
  banksOwned: number
  notesOwned: number
  contributions: number
  compositeScore: number
}

export interface Leaderboard {
  entries: LeaderboardEntry[]
  weights: LeaderboardWeights
  generatedAt: string
}

export class ApiError extends Error {
  readonly status: number
  readonly detail: string
  readonly errors: Record<string, string[]>

  constructor(status: number, detail: string, errors: Record<string, string[]> = {}) {
    super(detail)
    this.name = 'ApiError'
    this.status = status
    this.detail = detail
    this.errors = errors
  }
}

interface RequestOptions {
  method?: string
  body?: unknown
}

interface ProblemBody {
  detail?: string
  title?: string
  errors?: Record<string, string[] | string>
  extensions?: {
    errors?: Record<string, string[] | string>
  }
}

function normalizeErrors(raw: Record<string, string[] | string> | undefined): Record<string, string[]> {
  if (!raw) return {}
  const errors: Record<string, string[]> = {}
  for (const [field, messages] of Object.entries(raw)) {
    errors[field] = Array.isArray(messages) ? messages.map(String) : [String(messages)]
  }
  return errors
}

function redirectToLogin() {
  const path = window.location.pathname
  if (path === '/login') return
  // 分享链接允许游客浏览，不能因为会话探测失败就把人踢去登录页。
  if (path.startsWith('/s/') || path.startsWith('/share/')) return
  window.location.assign('/login')
}

function parseResponseBody(text: string): unknown {
  if (!text) return undefined
  return JSON.parse(text)
}

function problemError(status: number, problem: ProblemBody | null, path: string): ApiError {
  const errors = normalizeErrors(problem?.errors ?? problem?.extensions?.errors)
  const detail =
    problem?.detail ||
    Object.values(errors)[0]?.[0] ||
    problem?.title ||
    `请求失败（${status}）`

  // /api/me 是会话探测，401 只代表未登录；/api/public 本来就允许匿名。
  const silent401 =
    path === '/api/me' || path.startsWith('/api/auth/') || path.startsWith('/api/public/')
  if (status === 401 && !silent401) {
    redirectToLogin()
  }

  return new ApiError(status, detail, errors)
}

async function request<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const { method = 'GET', body } = options
  const init: RequestInit = {
    method,
    credentials: 'same-origin',
  }
  if (body !== undefined) {
    init.headers = { 'Content-Type': 'application/json' }
    init.body = JSON.stringify(body)
  }

  let response: Response
  try {
    response = await fetch(path, init)
  } catch {
    throw new ApiError(0, '网络连接失败，请检查后端服务是否已启动')
  }

  if (!response.ok) {
    let problem: ProblemBody | null = null
    try {
      problem = (await response.json()) as ProblemBody
    } catch {
      problem = null
    }

    throw problemError(response.status, problem, path)
  }

  if (response.status === 204) {
    return undefined as T
  }

  return parseResponseBody(await response.text()) as T
}

export async function uploadJson<T>(
  path: string,
  body: unknown,
  onProgress?: (ratio: number) => void,
): Promise<T> {
  return new Promise<T>((resolve, reject) => {
    const xhr = new XMLHttpRequest()
    xhr.open('POST', path)
    xhr.withCredentials = true
    xhr.setRequestHeader('Content-Type', 'application/json')

    if (onProgress) {
      xhr.upload.onprogress = (event) => {
        if (event.lengthComputable && event.total > 0) {
          onProgress(event.loaded / event.total)
        }
      }
    }

    xhr.onerror = () => reject(new ApiError(0, '网络连接失败，请检查后端服务是否已启动'))

    xhr.onload = () => {
      if (xhr.status >= 200 && xhr.status < 300) {
        try {
          resolve(parseResponseBody(xhr.responseText) as T)
        } catch (error) {
          reject(error)
        }
        return
      }

      let problem: ProblemBody | null = null
      try {
        problem = JSON.parse(xhr.responseText) as ProblemBody
      } catch {
        problem = null
      }

      reject(problemError(xhr.status, problem, path))
    }

    xhr.send(JSON.stringify(body))
  })
}

export function errorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    return Object.values(error.errors)[0]?.[0] ?? error.detail
  }
  if (error instanceof Error && error.message) {
    return error.message
  }
  return '出了点问题，请稍后再试'
}

export const api = {
  me: () => request<User>('/api/me'),

  verifyMembers: (refresh = false) =>
    request<VerifyMembersResult>(`/api/auth/verify/members${refresh ? '?refresh=true' : ''}`),

  requestVerification: (userId: string) =>
    request<VerificationTicket>('/api/auth/verify/request', {
      method: 'POST',
      body: { userId },
    }),

  confirmVerification: (ticketId: string, code: string) =>
    request<User>('/api/auth/verify/confirm', {
      method: 'POST',
      body: { ticketId, code },
    }),

  logout: () => request<void>('/api/auth/logout', { method: 'POST' }),

  updatePrefs: (prefs: { shuffleOptions?: string; submitMode?: string }) =>
    request<User>('/api/me/prefs', { method: 'PATCH', body: prefs }),

  banks: () => request<Bank[]>('/api/banks'),

  recentItems: (limit = 6) => request<RecentItem[]>(`/api/recents?limit=${limit}`),

  favorites: () => request<FavoriteItem[]>('/api/favorites'),

  addFavorite: (kind: 'bank' | 'note', itemId: string) =>
    request<void>('/api/favorites', { method: 'POST', body: { kind, itemId } }),

  removeFavorite: (kind: 'bank' | 'note', itemId: string) =>
    request<void>(`/api/favorites/${kind}/${encodeURIComponent(itemId)}`, { method: 'DELETE' }),

  recordRecent: (kind: 'bank' | 'note', itemId: string) =>
    request<void>('/api/recents', { method: 'POST', body: { kind, itemId } }),

  createLink: (kind: 'bank' | 'note', targetId: string) =>
    request<ShortLink>('/api/links', { method: 'POST', body: { kind, targetId } }),

  resolveLink: (code: string) =>
    request<ShortLink>(`/api/public/links/${encodeURIComponent(code)}`),

  publicNote: (noteId: string) =>
    request<Note>(`/api/public/notes/${encodeURIComponent(noteId)}`),

  publicBank: (bankId: string) =>
    request<Bank>(`/api/public/banks/${encodeURIComponent(bankId)}`),

  publicBankQuestions: (bankId: string) =>
    request<QuestionSummary[]>(`/api/public/banks/${encodeURIComponent(bankId)}/questions`),

  publicCard: (bankId: string, questionId: string, attempt = 0) =>
    request<Card>(
      `/api/public/banks/${encodeURIComponent(bankId)}/questions/${encodeURIComponent(questionId)}/card?attempt=${attempt}`,
    ),

  publicAnswer: (bankId: string, questionId: string, selectedKeys: string[]) =>
    request<GuestAnswer>(
      `/api/public/banks/${encodeURIComponent(bankId)}/questions/${encodeURIComponent(questionId)}/answer`,
      { method: 'POST', body: { selectedKeys } },
    ),

  activeSession: (bankId: string) =>
    request<Session | null>(`/api/banks/${encodeURIComponent(bankId)}/sessions/active`),

  bank: (bankId: string) => request<BankAccess>(`/api/banks/${encodeURIComponent(bankId)}`),

  createBank: (body: CreateBankBody) =>
    request<Bank>('/api/banks', { method: 'POST', body }),

  updateBank: (bankId: string, body: UpdateBankBody) =>
    request<Bank>(`/api/banks/${encodeURIComponent(bankId)}`, { method: 'PATCH', body }),

  deleteBank: (bankId: string) =>
    request<void>(`/api/banks/${encodeURIComponent(bankId)}`, { method: 'DELETE' }),

  addBankMember: (bankId: string, userId: string) =>
    request<Bank>(`/api/banks/${encodeURIComponent(bankId)}/members`, {
      method: 'POST',
      body: { userId },
    }),

  removeBankMember: (bankId: string, userId: string) =>
    request<Bank>(
      `/api/banks/${encodeURIComponent(bankId)}/members/${encodeURIComponent(userId)}`,
      { method: 'DELETE' },
    ),

  questions: (bankId: string, params: { q?: string; tag?: string } = {}) => {
    const search = new URLSearchParams()
    if (params.q) search.set('q', params.q)
    if (params.tag) search.set('tag', params.tag)
    const suffix = search.toString()
    return request<QuestionSummary[]>(
      `/api/banks/${encodeURIComponent(bankId)}/questions${suffix ? `?${suffix}` : ''}`,
    )
  },

  question: (questionId: string) =>
    request<Question>(`/api/questions/${encodeURIComponent(questionId)}`),

  bankChapterIndex: (bankIds: string[]) => {
    const search = new URLSearchParams()
    for (const id of bankIds) search.append('bankIds', id)
    return request<BankChapterIndex[]>(`/api/questions/chapter-index?${search.toString()}`)
  },

  createQuestion: (bankId: string, body: SaveQuestionRequest) =>
    request<Question>(`/api/banks/${encodeURIComponent(bankId)}/questions`, {
      method: 'POST',
      body,
    }),

  updateQuestion: (questionId: string, body: SaveQuestionRequest) =>
    request<Question>(`/api/questions/${encodeURIComponent(questionId)}`, {
      method: 'PUT',
      body,
    }),

  deleteQuestion: (questionId: string) =>
    request<void>(`/api/questions/${encodeURIComponent(questionId)}`, { method: 'DELETE' }),

  startSession: (bankId: string, mode?: string, limit?: number, chapters?: string[][]) =>
    request<Session>(`/api/banks/${encodeURIComponent(bankId)}/sessions`, {
      method: 'POST',
      body: { limit: limit ?? null, mode: mode ?? null, chapters: chapters ?? null },
    }),

  nextCard: (sessionId: string) =>
    request<NextCard>(`/api/sessions/${encodeURIComponent(sessionId)}/next`),

  submitAnswer: (sessionId: string, body: SubmitAnswerBody) =>
    request<AnswerFeedback>(`/api/sessions/${encodeURIComponent(sessionId)}/answers`, {
      method: 'POST',
      body,
    }),

  finishSession: (sessionId: string) =>
    request<Session>(`/api/sessions/${encodeURIComponent(sessionId)}/finish`, { method: 'POST' }),

  pools: (bankId: string) => request<OptionPool[]>(`/api/banks/${encodeURIComponent(bankId)}/pools`),

  createPool: (bankId: string, body: { name: string; description?: string }) =>
    request<OptionPool>(`/api/banks/${encodeURIComponent(bankId)}/pools`, {
      method: 'POST',
      body,
    }),

  pool: (poolId: string) => request<PoolDetail>(`/api/pools/${encodeURIComponent(poolId)}`),

  updatePool: (poolId: string, body: { name?: string; description?: string }) =>
    request<OptionPool>(`/api/pools/${encodeURIComponent(poolId)}`, { method: 'PATCH', body }),

  deletePool: (poolId: string) =>
    request<void>(`/api/pools/${encodeURIComponent(poolId)}`, { method: 'DELETE' }),

  addPoolEntry: (poolId: string, body: { text: string; tags?: string[] }) =>
    request<PoolEntry>(`/api/pools/${encodeURIComponent(poolId)}/entries`, {
      method: 'POST',
      body,
    }),

  updatePoolEntry: (
    poolId: string,
    entryId: string,
    body: { text?: string; tags?: string[]; disabled?: boolean },
  ) =>
    request<PoolEntry>(
      `/api/pools/${encodeURIComponent(poolId)}/entries/${encodeURIComponent(entryId)}`,
      { method: 'PATCH', body },
    ),

  deletePoolEntry: (poolId: string, entryId: string) =>
    request<void>(
      `/api/pools/${encodeURIComponent(poolId)}/entries/${encodeURIComponent(entryId)}`,
      { method: 'DELETE' },
    ),

  poolHealth: (poolId: string) =>
    request<PoolHealth>(`/api/pools/${encodeURIComponent(poolId)}/health`),

  importPrompt: (bankId: string) =>
    request<ImportPrompt>(`/api/banks/${encodeURIComponent(bankId)}/import-prompt`),

  previewImport: (bankId: string, payload: string, onProgress?: (ratio: number) => void) =>
    uploadJson<ImportPreview>(
      `/api/banks/${encodeURIComponent(bankId)}/imports/preview`,
      { payload, force: false },
      onProgress,
    ),

  startImportJob: (
    bankId: string,
    body: { planId?: string; payload?: string; force: boolean },
  ) =>
    request<{ jobId: string }>(`/api/banks/${encodeURIComponent(bankId)}/imports/jobs`, {
      method: 'POST',
      body,
    }),

  importJob: (jobId: string) =>
    request<ImportJob>(`/api/imports/jobs/${encodeURIComponent(jobId)}`),

  importQuestions: (bankId: string, payload: string, force: boolean) =>
    request<ImportResult>(`/api/banks/${encodeURIComponent(bankId)}/imports`, {
      method: 'POST',
      body: { payload, force },
    }),

  importBatches: (bankId: string) =>
    request<ImportBatchSummary[]>(`/api/banks/${encodeURIComponent(bankId)}/imports`),

  approveImport: (batchId: string) =>
    request<{ published: number; remaining: number }>(
      `/api/imports/${encodeURIComponent(batchId)}/approve`,
      { method: 'POST' },
    ),

  notes: () => request<NoteSummary[]>('/api/notes'),

  noteImportPrompt: () => request<NoteImportPrompt>('/api/notes/import-prompt'),

  note: (noteId: string) => request<Note>(`/api/notes/${encodeURIComponent(noteId)}`),

  createNote: (doc: NoteDocument) => request<Note>('/api/notes', { method: 'POST', body: doc }),

  updateNoteMeta: (noteId: string, body: UpdateNoteMetaBody) =>
    request<Note>(`/api/notes/${encodeURIComponent(noteId)}`, { method: 'PATCH', body }),

  deleteNote: (noteId: string) =>
    request<void>(`/api/notes/${encodeURIComponent(noteId)}`, { method: 'DELETE' }),

  leaderboard: () => request<Leaderboard>('/api/leaderboard'),
}
