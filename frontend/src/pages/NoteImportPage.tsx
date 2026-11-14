import { useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, errorMessage } from '../api'
import type { NoteDocument } from '../api'
import { Icon } from '../components/Icon'
import { TagInput } from '../components/TagInput'
import { ErrorNote } from '../components/Ui'
import { asBlocks, countBlocks, parseNoteDocument } from '../note-format'
import type { NoteParseResult } from '../note-format'

const VISIBILITY_OPTIONS = [
  { value: 'private', label: '私有', hint: '只有你能在讲义列表里看到' },
  { value: 'public', label: '公开', hint: '所有人能在讲义列表里看到并阅读' },
]

export function NoteImportPage() {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [payload, setPayload] = useState('')
  const [parsed, setParsed] = useState<NoteParseResult | null>(null)
  const [visibility, setVisibility] = useState('public')
  const [tags, setTags] = useState<string[]>([])
  const [year, setYear] = useState('')
  const [semester, setSemester] = useState('')
  const [copied, setCopied] = useState(false)
  const [showPrompt, setShowPrompt] = useState(false)

  const promptQuery = useQuery({
    queryKey: ['note-import-prompt'],
    queryFn: api.noteImportPrompt,
    retry: false,
  })

  const publishMutation = useMutation({
    mutationFn: (doc: NoteDocument) => api.createNote(doc),
    onSuccess: (note) => {
      void queryClient.invalidateQueries({ queryKey: ['notes'] })
      navigate(`/notes/${note.id}`)
    },
  })

  const document = parsed?.document ?? null
  const sections = document && Array.isArray(document.sections) ? document.sections : []
  const publishBlocked = visibility === 'public' && tags.length === 0
  const totalBlocks = sections.reduce(
    (sum, section) => sum + countBlocks(asBlocks(section.blocks)),
    0,
  )

  function handlePayloadChange(value: string) {
    setPayload(value)
    if (parsed) setParsed(null)
    if (publishMutation.isError) publishMutation.reset()
  }

  function handleParse() {
    publishMutation.reset()
    const result = parseNoteDocument(payload)
    setParsed(result)
    if (result.document) {
      setVisibility(result.document.visibility === 'private' ? 'private' : 'public')
      setTags(result.document.tags ?? [])
      setYear(result.document.year ?? '')
      const nextSemester = result.document.semester ?? ''
      setSemester(nextSemester === '上' || nextSemester === '下' ? nextSemester : '')
    }
  }

  function handleVisibilityChange(next: string) {
    setVisibility(next)
    if (publishMutation.isError) publishMutation.reset()
  }

  function handleTagsChange(next: string[]) {
    setTags(next)
    if (publishMutation.isError) publishMutation.reset()
  }

  function handleYearChange(next: string) {
    setYear(next)
    if (publishMutation.isError) publishMutation.reset()
  }

  function handleSemesterChange(next: string) {
    setSemester(next)
    if (publishMutation.isError) publishMutation.reset()
  }

  function handleClear() {
    setPayload('')
    setParsed(null)
    setYear('')
    setSemester('')
    publishMutation.reset()
  }

  async function copyPrompt() {
    const text = promptQuery.data?.prompt
    if (!text) return
    try {
      await navigator.clipboard.writeText(text)
      setCopied(true)
      window.setTimeout(() => setCopied(false), 1800)
    } catch {
      setCopied(false)
    }
  }

  function handlePublish() {
    if (!document || publishBlocked) return
    publishMutation.mutate({ ...document, visibility, tags, year: year.trim(), semester })
  }

  return (
    <div className="note-import-page">
      <Link to="/notes" className="back-link">
        ← 返回讲义列表
      </Link>

      <header className="page-header">
        <div className="page-heading">
          <h1>导入讲义</h1>
          <p className="page-sub">
            把整份讲义 JSON 粘贴进来，先在本地校验，确认结构无误后再发布。
          </p>
        </div>
      </header>

      <section className="card card-pad note-import-card">
        <div className="section-head">
          <h2 className="section-title">复制提示词</h2>
          <span className="muted">只描述格式</span>
        </div>
        <p className="form-hint">
          提示词只说明 JSON 结构和允许的块类型，不规定讲什么、分几节。复制后把主题、章节和大致篇幅告诉自己的模型。
        </p>
        <div className="form-actions">
          <button
            type="button"
            className="btn btn-ghost"
            onClick={() => setShowPrompt((value) => !value)}
          >
            {showPrompt ? '收起' : '查看提示词'}
          </button>
          <button
            type="button"
            className="btn btn-primary"
            onClick={() => void copyPrompt()}
            disabled={!promptQuery.data}
          >
            {copied ? '已复制' : '复制到剪贴板'}
          </button>
        </div>
        {showPrompt ? (
          <textarea
            className="textarea code-area note-import-code"
            readOnly
            rows={16}
            value={promptQuery.data?.prompt ?? ''}
            aria-label="讲义导入提示词"
            spellCheck={false}
          />
        ) : null}
        {promptQuery.isError ? <ErrorNote message={errorMessage(promptQuery.error)} /> : null}
      </section>

      <section className="card card-pad note-import-card">
        <div className="section-head">
          <h2 className="section-title">粘贴讲义</h2>
          <span className="muted">{payload.length.toLocaleString()} 字符</span>
        </div>

        <div className="field">
          <label htmlFor="note-json">讲义 JSON</label>
          <textarea
            id="note-json"
            className="textarea code-area note-import-code"
            rows={14}
            value={payload}
            onChange={(event) => handlePayloadChange(event.target.value)}
            placeholder={'{\n  "formatVersion": 1,\n  "title": "……",\n  "sections": [ … ]\n}'}
            spellCheck={false}
          />
          <p className="form-hint">只接受 formatVersion 为 1 的完整文档，不支持文件上传。</p>
        </div>

        <div className="form-actions">
          <button
            type="button"
            className="btn btn-ghost"
            onClick={handleClear}
            disabled={!payload && !parsed}
          >
            <Icon name="x" size={16} />
            清空
          </button>
          <button
            type="button"
            className="btn btn-primary"
            onClick={handleParse}
            disabled={!payload.trim()}
          >
            解析并校验
          </button>
        </div>
      </section>

      {parsed && parsed.errors.length > 0 ? (
        <section className="card card-pad note-import-card" aria-live="polite">
          <div className="section-head">
            <h2 className="section-title">校验没有通过</h2>
            <span className="muted">{parsed.errors.length} 条问题</span>
          </div>
          <ul className="note-import-errors">
            {parsed.errors.map((message, index) => (
              <li key={index}>{message}</li>
            ))}
          </ul>
          <p className="form-hint">修正后重新粘贴或编辑，再点一次「解析并校验」。</p>
        </section>
      ) : null}

      {document ? (
        <section className="card card-pad note-import-card note-import-result">
          <div className="section-head">
            <h2 className="section-title">解析结果</h2>
            <span className="muted">formatVersion {document.formatVersion}</span>
          </div>

          <h3 className="note-import-title">{document.title}</h3>
          {document.subtitle ? (
            <p className="note-import-sub">{document.subtitle}</p>
          ) : null}

          <div className="import-stats">
            <span className="stat-pill">{sections.length} 节</span>
            <span className="stat-pill">{totalBlocks} 个块</span>
            <span className="stat-pill">{tags.length} 个标签</span>
          </div>

          <div className="note-import-meta">
            <div className="note-import-field">
              <p className="note-import-field-title" id="note-import-visibility-title">
                可见性
              </p>
              <div
                className="radio-cards"
                role="radiogroup"
                aria-labelledby="note-import-visibility-title"
              >
                {VISIBILITY_OPTIONS.map((option) => (
                  <label
                    key={option.value}
                    className={
                      visibility === option.value ? 'radio-card selected' : 'radio-card'
                    }
                  >
                    <input
                      type="radio"
                      name="note-import-visibility"
                      value={option.value}
                      checked={visibility === option.value}
                      onChange={() => handleVisibilityChange(option.value)}
                    />
                    <span className="radio-card-text">
                      <strong>{option.label}</strong>
                      <small>{option.hint}</small>
                    </span>
                    <span className="radio-card-check" aria-hidden="true">
                      {visibility === option.value ? <Icon name="check" size={16} /> : null}
                    </span>
                  </label>
                ))}
              </div>
            </div>

            <div className="form-row">
              <div className="field">
                <label htmlFor="note-import-year">学年</label>
                <input
                  id="note-import-year"
                  className="input"
                  type="text"
                  maxLength={20}
                  value={year}
                  onChange={(event) => handleYearChange(event.target.value)}
                  placeholder="如 高一 / 2025-2026"
                />
              </div>

              <div className="field">
                <label htmlFor="note-import-semester">学期</label>
                <select
                  id="note-import-semester"
                  className="select"
                  value={semester}
                  onChange={(event) => handleSemesterChange(event.target.value)}
                >
                  <option value="">未设置</option>
                  <option value="上">上学期</option>
                  <option value="下">下学期</option>
                </select>
              </div>
            </div>

            <TagInput
              label="学科标签"
              value={tags}
              onChange={handleTagsChange}
              hint="公开讲义至少需要一个学科标签；私有讲义可以不填。"
            />

            {publishBlocked ? (
              <p className="form-hint note-import-warning" role="status">
                公开讲义至少需要一个学科标签，暂时不能发布。
              </p>
            ) : null}
          </div>

          <ol className="note-import-sections">
            {sections.map((section, index) => (
              <li key={section.id || index}>
                <span className="note-import-index">
                  {section.label || `第 ${index + 1} 节`}
                </span>
                <span className="note-import-name">{section.title}</span>
                <span className="note-import-count">
                  {countBlocks(asBlocks(section.blocks))} 块
                </span>
              </li>
            ))}
          </ol>

          {publishMutation.isError ? (
            <ErrorNote message={errorMessage(publishMutation.error)} />
          ) : null}

          <div className="form-actions">
            <Link to="/notes" className="btn btn-ghost">
              取消
            </Link>
            <button
              type="button"
              className="btn btn-primary"
              onClick={handlePublish}
              disabled={publishMutation.isPending || publishBlocked}
            >
              <Icon name="check" size={16} />
              {publishMutation.isPending ? '发布中…' : '发布讲义'}
            </button>
          </div>
        </section>
      ) : null}
    </div>
  )
}
