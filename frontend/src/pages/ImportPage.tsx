import { useEffect, useMemo, useState } from 'react'
import type { ReactNode } from 'react'
import { Link, useParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, errorMessage } from '../api'
import type { ImportPreview, ImportQuestionPreview } from '../api'
import { MarkdownView } from '../components/MarkdownView'
import { Icon } from '../components/Icon'
import { EmptyState, ErrorNote, Loading } from '../components/Ui'
import { formatDate } from '../format'

const IMPORT_STEPS = [
  { step: 1, label: '复制提示词' },
  { step: 2, label: '粘贴与校验' },
  { step: 3, label: '预览结果' },
]

/** 一次导入可能上万题，预览列表只渲染一部分，避免浏览器卡死。 */
const PREVIEW_ROW_LIMIT = 300

function StepHead({
  step,
  title,
  hint,
  children,
}: {
  step: number
  title: string
  hint: string
  children?: ReactNode
}) {
  return (
    <div className="step-head">
      <span className="step-number" aria-hidden="true">
        {step}
      </span>
      <div className="step-body">
        <h2 className="section-title">{title}</h2>
        <p className="step-hint">{hint}</p>
      </div>
      {children ? <div className="step-actions">{children}</div> : null}
    </div>
  )
}

export function ImportPage() {
  const { id } = useParams<{ id: string }>()
  const bankId = id ?? ''
  const queryClient = useQueryClient()

  const [payload, setPayload] = useState('')
  const [force, setForce] = useState(false)
  const [showPrompt, setShowPrompt] = useState(false)
  const [copied, setCopied] = useState(false)
  const [preview, setPreview] = useState<ImportPreview | null>(null)
  const [planId, setPlanId] = useState('')
  const [jobId, setJobId] = useState<string | null>(null)
  const [uploadRatio, setUploadRatio] = useState<number | null>(null)

  const accessQuery = useQuery({
    queryKey: ['bank', bankId],
    queryFn: () => api.bank(bankId),
    enabled: Boolean(bankId),
  })

  const promptQuery = useQuery({
    queryKey: ['import-prompt', bankId],
    queryFn: () => api.importPrompt(bankId),
    enabled: Boolean(bankId),
    retry: false,
  })

  const batchesQuery = useQuery({
    queryKey: ['import-batches', bankId],
    queryFn: () => api.importBatches(bankId),
    enabled: Boolean(bankId) && Boolean(accessQuery.data?.canView),
  })

  const previewMutation = useMutation({
    mutationFn: () => {
      handleUploadProgress(0)
      return api.previewImport(bankId, payload, handleUploadProgress)
    },
    onSuccess: (data) => {
      setPreview(data)
      setPlanId(data.planId)
      setJobId(null)
    },
    onSettled: () => setUploadRatio(null),
  })

  const startMutation = useMutation({
    mutationFn: () =>
      planId
        ? api.startImportJob(bankId, { planId, force })
        : api.startImportJob(bankId, { payload, force }),
    onSuccess: (data) => {
      setJobId(data.jobId)
    },
  })

  const jobQuery = useQuery({
    queryKey: ['import-job', jobId],
    queryFn: () => api.importJob(jobId ?? ''),
    enabled: Boolean(jobId),
    retry: false,
    refetchInterval: (query) => {
      const status = query.state.data?.status
      return status === undefined || status === 'running' ? 800 : false
    },
  })

  const job = jobQuery.data
  const jobRunning = job?.status === 'running'

  useEffect(() => {
    if (!jobId || job?.status !== 'done') return
    setPreview(null)
    setPlanId('')
    void queryClient.invalidateQueries({ queryKey: ['questions', bankId] })
    void queryClient.invalidateQueries({ queryKey: ['bank', bankId] })
    void queryClient.invalidateQueries({ queryKey: ['import-batches', bankId] })
  }, [jobId, job?.status, bankId, queryClient])

  const approveMutation = useMutation({
    mutationFn: (batchId: string) => api.approveImport(batchId),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['import-batches', bankId] })
      void queryClient.invalidateQueries({ queryKey: ['questions', bankId] })
    },
  })

  function handleUploadProgress(ratio: number) {
    setUploadRatio((previous) => {
      const next = Math.min(1, Math.max(0, ratio))
      if (previous !== null && Math.round(previous * 100) === Math.round(next * 100)) {
        return previous
      }
      return next
    })
  }

  const issueCount = useMemo(() => {
    if (!preview) return 0
    return preview.batchIssues.length + preview.questions.reduce((sum, q) => sum + q.issues.length, 0)
  }, [preview])

  const previewRows = useMemo(() => {
    if (!preview) return []
    if (preview.questions.length <= PREVIEW_ROW_LIMIT) return preview.questions
    const flagged = preview.questions.filter((question) => question.issues.length > 0)
    const clean = preview.questions.filter((question) => question.issues.length === 0)
    return [...flagged, ...clean].slice(0, PREVIEW_ROW_LIMIT)
  }, [preview])

  if (!bankId) {
    return <ErrorNote message="找不到这个题库。" />
  }

  if (accessQuery.isPending) {
    return <Loading full />
  }

  if (accessQuery.isError) {
    return (
      <div>
        <Link to="/" className="back-link">
          ← 返回题库
        </Link>
        <ErrorNote message={errorMessage(accessQuery.error)} />
      </div>
    )
  }

  const access = accessQuery.data
  const bank = access.bank
  const busy = previewMutation.isPending || startMutation.isPending || jobRunning
  const activeStep = jobId || preview ? 3 : payload.trim() ? 2 : 1
  const uploading = uploadRatio !== null && uploadRatio < 1
  const uploadPercent = uploadRatio !== null && uploadRatio < 1 ? Math.round(uploadRatio * 100) : 0
  const jobPercent =
    job && job.total > 0 ? Math.min(100, Math.round((job.processed / job.total) * 100)) : 0

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

  return (
    <div>
      <Link to={`/banks/${bankId}`} className="back-link">
        ← 返回 {bank.name}
      </Link>

      <header className="page-header">
        <div className="page-heading">
          <p className="eyebrow">{bank.name}</p>
          <h1>批量导入</h1>
          <p className="page-sub">
            把提示词粘到自己的模型里生成题目，再把返回的 JSON 贴回来。导入前会先校验并预览。
          </p>
        </div>
      </header>

      {!access.canEdit ? (
        <ErrorNote message="你没有编辑这个题库的权限，只能查看导入记录。" />
      ) : null}

      <ol className="import-steps" aria-label="导入流程">
        {IMPORT_STEPS.map((item) => {
          const content = (
            <>
              <span className="import-step-index" aria-hidden="true">
                {item.step}
              </span>
              <span className="import-step-label">{item.label}</span>
            </>
          )
          return (
            <li
              key={item.step}
              className={
                item.step === activeStep
                  ? 'import-step is-current'
                  : item.step < activeStep
                    ? 'import-step is-done'
                    : 'import-step'
              }
            >
              {item.step <= activeStep ? (
                <a
                  href={`#import-step-${item.step}`}
                  aria-current={item.step === activeStep ? 'step' : undefined}
                >
                  {content}
                </a>
              ) : (
                <span className="import-step-link">{content}</span>
              )}
            </li>
          )
        })}
      </ol>

      <section id="import-step-1" className="card card-pad editor-section import-section">
        <StepHead
          step={1}
          title="复制提示词"
          hint={`提示词里已经带上本题库的标签表${
            promptQuery.data ? `（${promptQuery.data.tagCount} 个标签）` : ''
          }，复制后粘到自己的模型里生成题目。`}
        >
          <button
            type="button"
            className="btn btn-ghost"
            onClick={() => setShowPrompt((v) => !v)}
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
        </StepHead>
        {showPrompt ? (
          <textarea
            className="textarea code-area import-code"
            readOnly
            rows={16}
            value={promptQuery.data?.prompt ?? ''}
            aria-label="导入提示词"
            spellCheck={false}
          />
        ) : null}
        {promptQuery.isError ? <ErrorNote message={errorMessage(promptQuery.error)} /> : null}
      </section>

      <section id="import-step-2" className="card card-pad editor-section import-section">
        <StepHead
          step={2}
          title="粘贴生成结果"
          hint="支持带代码围栏、尾逗号或前后说明文字的 JSON。"
        />
        <textarea
          className="textarea code-area import-code"
          rows={12}
          value={payload}
          onChange={(event) => {
            setPayload(event.target.value)
            setPreview(null)
            setPlanId('')
            setJobId(null)
          }}
          placeholder={'{\n  "formatVersion": 1,\n  "questions": [ ... ]\n}'}
          aria-label="待导入的 JSON"
          spellCheck={false}
        />

        <div className="import-options">
          <label className="checkbox-row">
            <input
              type="checkbox"
              checked={force}
              onChange={(event) => setForce(event.target.checked)}
            />
            重复题也导入
          </label>
          <span className="muted">建议先校验，确认没有错误再导入。</span>
        </div>

        <div className="form-actions">
          <button
            type="button"
            className="btn btn-ghost"
            onClick={() => previewMutation.mutate()}
            disabled={!payload.trim() || busy || !access.canEdit}
          >
            {previewMutation.isPending ? '校验中…' : '预览与校验'}
          </button>
          <button
            type="button"
            className="btn btn-primary"
            onClick={() => startMutation.mutate()}
            disabled={!payload.trim() || busy || !access.canEdit}
          >
            {startMutation.isPending || jobRunning ? '导入中…' : '确认导入'}
          </button>
        </div>

        {previewMutation.isPending ? (
          <div className="import-progress" aria-live="polite" aria-busy="true">
            <div className="import-progress-head">
              <span>{uploading ? `上传中 ${uploadPercent}%` : '服务器校验中…'}</span>
            </div>
            <div
              className="import-progress-track"
              role="progressbar"
              aria-label="上传进度"
              aria-valuemin={0}
              aria-valuemax={100}
              aria-valuenow={uploading ? uploadPercent : undefined}
            >
              <div
                className={
                  uploading ? 'import-progress-bar' : 'import-progress-bar is-indeterminate'
                }
                style={uploading ? { width: `${uploadPercent}%` } : undefined}
              />
            </div>
          </div>
        ) : null}

        {jobRunning && job ? (
          <div className="import-progress" aria-live="polite" aria-busy="true">
            <div className="import-progress-head">
              <span>
                正在导入 {job.processed} / {job.total} 题
              </span>
              <span className="muted">{jobPercent}%</span>
            </div>
            <div
              className="import-progress-track"
              role="progressbar"
              aria-label="导入进度"
              aria-valuemin={0}
              aria-valuemax={100}
              aria-valuenow={jobPercent}
            >
              <div className="import-progress-bar" style={{ width: `${jobPercent}%` }} />
            </div>
            <p className="muted">
              已导入 {job.imported} · 跳过 {job.skipped} · 失败 {job.failed}
            </p>
          </div>
        ) : null}

        {previewMutation.isError ? <ErrorNote message={errorMessage(previewMutation.error)} /> : null}
        {startMutation.isError ? <ErrorNote message={errorMessage(startMutation.error)} /> : null}
        {job?.status === 'failed' ? (
          <ErrorNote message={job.error ?? '导入任务失败，请重新预览后再试。'} />
        ) : null}
        {jobQuery.isError ? <ErrorNote message={errorMessage(jobQuery.error)} /> : null}
      </section>

      {preview ? (
        <section id="import-step-3" className="card card-pad editor-section import-section">
          <StepHead
            step={3}
            title="预览结果"
            hint="逐题核对题干与提示；错误会标在具体题号上，修正 JSON 后可重新校验。"
          >
            <span className="muted">{issueCount} 条提示</span>
          </StepHead>

          <div className="import-stats">
            <span className="stat-pill">共 {preview.total} 题</span>
            <span className="stat-pill ok">可导入 {preview.valid}</span>
            <span className="stat-pill warn">警告 {preview.warnings}</span>
            <span className="stat-pill bad">错误 {preview.errors}</span>
          </div>

          {preview.batchIssues.length > 0 ? (
            <div className="notice import-batch-issues">
              <p className="batch-issue-title">整批提示</p>
              <ul>
                {preview.batchIssues.map((issue) => (
                  <li key={`${issue.code}-${issue.message}`}>
                    <code className="issue-code">{issue.code}</code>
                    <span>{issue.message}</span>
                  </li>
                ))}
              </ul>
            </div>
          ) : null}

          <div className="import-list">
            {previewRows.map((question) => (
              <div
                key={`${question.index}-${question.clientId ?? ''}`}
                className={`import-row ${question.status}`}
              >
                <div className="import-row-head">
                  <span className="import-index">第 {question.index + 1} 题</span>
                  <span className={`import-status ${question.status}`}>
                    {statusLabel(question.status)}
                  </span>
                  {question.clientId ? (
                    <code className="import-client">{question.clientId}</code>
                  ) : null}
                </div>
                <p className="import-stem">{question.stem || '（题干为空）'}</p>
                {question.poolName && question.poolCount ? (
                  <p className="import-chapter">
                    选项池：{question.poolName} · 抽 {question.poolCount} 个干扰项
                  </p>
                ) : null}
                <ImportChapter question={question} />
                {question.issues.length > 0 ? (
                  <ul className="issue-list">
                    {question.issues.map((issue, index) => (
                      <li key={`${issue.code}-${index}`} className={issue.severity}>
                        <code className="issue-code">{issue.code}</code>
                        <span>{issue.message}</span>
                      </li>
                    ))}
                  </ul>
                ) : null}
              </div>
            ))}
          </div>

          {preview.questions.length > previewRows.length ? (
            <p className="muted">
              题目较多，这里只列出前 {PREVIEW_ROW_LIMIT} 条（有提示的优先），其余题目同样会一起校验与导入。
            </p>
          ) : null}

          {preview.sampleCards.length > 0 ? (
            <>
              <div className="sample-head">
                <h3 className="section-title">导入后的样子（示例）</h3>
                <span className="sample-badge">示例</span>
              </div>
              <div className="sample-cards">
                {preview.sampleCards.map((card) => (
                  <article key={card.questionId} className="sample-card">
                    <MarkdownView source={card.stem} />
                    <div className="sample-options">
                      {card.options.map((option) => (
                        <div key={option.key} className="sample-option">
                          <span className="option-letter small">{option.label}</span>
                          <MarkdownView source={option.text} />
                        </div>
                      ))}
                    </div>
                  </article>
                ))}
              </div>
            </>
          ) : null}
        </section>
      ) : null}

      {job?.status === 'done' ? (
        <section className="card card-pad editor-section import-section import-result">
          <div className="import-result-head">
            <span className="import-result-mark" aria-hidden="true">
              <Icon name="check" size={18} />
            </span>
            <h2 className="section-title">导入完成</h2>
          </div>
          <div className="import-stats">
            <span className="stat-pill">共 {job.total} 题</span>
            <span className="stat-pill ok">已导入 {job.imported}</span>
            <span className="stat-pill warn">跳过 {job.skipped}</span>
            <span className="stat-pill bad">失败 {job.failed}</span>
          </div>
          {job.failed > 0 ? (
            <div className="notice">
              <p>有 {job.failed} 题未能导入，可以在下方导入记录里查看。</p>
            </div>
          ) : null}
          <div className="form-actions">
            <Link to={`/banks/${bankId}`} className="btn btn-primary">
              回到题库
            </Link>
          </div>
        </section>
      ) : null}

      <section className="card card-pad editor-section import-section">
        <div className="section-head">
          <h2 className="section-title">导入记录</h2>
          <span className="muted">最近 30 次</span>
        </div>
        {batchesQuery.isPending ? <Loading /> : null}
        {batchesQuery.isError ? <ErrorNote message={errorMessage(batchesQuery.error)} /> : null}
        {batchesQuery.data && batchesQuery.data.length === 0 ? (
          <EmptyState title="还没有导入记录" hint="生成一批题目后，这里会出现每次导入的统计。" />
        ) : null}
        {batchesQuery.data && batchesQuery.data.length > 0 ? (
          <div className="entry-list">
            {batchesQuery.data.map((batch) => (
              <div key={batch.id} className="batch-row">
                <div>
                  <strong>{formatDate(batch.createdAt)}</strong>
                  <p className="muted">
                    共 {batch.total} · 导入 {batch.imported} · 跳过 {batch.skipped} · 失败 {batch.failed}
                  </p>
                </div>
                {batch.pending > 0 ? (
                  <button
                    type="button"
                    className="btn btn-small btn-primary"
                    onClick={() => approveMutation.mutate(batch.id)}
                    disabled={approveMutation.isPending}
                  >
                    {approveMutation.isPending ? '处理中…' : `通过待审 ${batch.pending} 题`}
                  </button>
                ) : (
                  <span className="muted">已全部发布</span>
                )}
              </div>
            ))}
          </div>
        ) : null}
        {approveMutation.isError ? <ErrorNote message={errorMessage(approveMutation.error)} /> : null}
      </section>
    </div>
  )
}

function ImportChapter({ question }: { question: ImportQuestionPreview }) {
  const path = (question as ImportQuestionPreview & { chapterPath?: string[] | null }).chapterPath
  if (!path || path.length === 0) return null
  return <p className="import-chapter">{path.join(' / ')}</p>
}

function statusLabel(status: string): string {
  switch (status) {
    case 'valid':
      return '可导入'
    case 'warning':
      return '有提示'
    case 'error':
      return '有错误'
    case 'imported':
      return '已导入'
    case 'skipped':
      return '已跳过'
    case 'failed':
      return '失败'
    default:
      return status
  }
}
