import { useState } from 'react'
import type { FormEvent } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, errorMessage } from '../api'
import type { Question, SaveQuestionRequest } from '../api'
import { ImageUpload } from '../components/ImageUpload'
import { MarkdownView } from '../components/MarkdownView'
import { ErrorNote, Loading } from '../components/Ui'
import { OPTION_LABELS, SHUFFLE_HINTS, SHUFFLE_LABELS, parseTags } from '../format'

// 插入的 Markdown 单独成段，避免粘在上一行文字后面。
function appendMarkdown(source: string, markdown: string): string {
  const base = source.replace(/\s+$/, '')
  return base ? `${base}\n\n${markdown}` : markdown
}

interface EditorOption {
  text: string
  correct: boolean
}

interface EditorFormState {
  type: string
  stem: string
  options: EditorOption[]
  shuffle: string
  tags: string
  chapterMajor: string
  chapterMinor: string
  difficulty: number
  source: string
  explanation: string
  poolId: string
  poolCount: number
}

type EditorErrorField =
  | 'stem'
  | 'options'
  | 'answer'
  | 'tags'
  | 'chapter'
  | 'pool'
  | 'general'

function blankOptions(count: number): EditorOption[] {
  return Array.from({ length: count }, () => ({ text: '', correct: false }))
}

function buildInitial(question?: Question): EditorFormState {
  if (!question) {
    return {
      type: 'single_choice',
      stem: '',
      options: blankOptions(4),
      shuffle: 'follow',
      tags: '',
      chapterMajor: '',
      chapterMinor: '',
      difficulty: 3,
      source: '',
      explanation: '',
      poolId: '',
      poolCount: 3,
    }
  }

  return {
    type: question.type,
    stem: question.stem,
    options:
      question.options.length > 0
        ? question.options.map((option) => ({ text: option.text, correct: option.correct }))
        : blankOptions(question.distractorRule ? 1 : 2),
    shuffle: question.shuffle,
    tags: question.tags.join('，'),
    chapterMajor: question.chapterPath?.[0] ?? '',
    chapterMinor: question.chapterPath?.[1] ?? '',
    difficulty: question.difficulty,
    source: question.source ?? '',
    explanation: question.explanation ?? '',
    poolId: question.distractorRule?.poolId ?? '',
    poolCount: question.distractorRule?.count ?? 3,
  }
}

function buildRequest(form: EditorFormState): SaveQuestionRequest | string {
  const stem = form.stem.trim()
  if (!stem) return '请填写题干。'
  if (stem.length > 4000) return '题干不能超过 4000 个字符。'

  const usingPool = Boolean(form.poolId)
  const minOptions = usingPool ? 1 : 2
  if (form.options.length < minOptions) {
    return usingPool ? '使用选项池时，至少写一个正确答案。' : '至少需要两个选项。'
  }
  if (form.options.some((option) => !option.text.trim())) return '请填写每个选项的内容。'

  const tags = parseTags(form.tags)
  if (tags.length > 10) return '标签最多 10 个。'

  const chapterMajor = form.chapterMajor.trim()
  const chapterMinor = form.chapterMinor.trim()
  if (chapterMajor.length > 40) return '大章不能超过 40 个字。'
  if (chapterMinor.length > 40) return '小章不能超过 40 个字。'
  if (chapterMinor && !chapterMajor) return '请先填写大章，再填写小章。'
  const chapterPath = [chapterMajor, chapterMinor].filter(Boolean)

  if (usingPool) {
    if (form.poolCount < 1 || form.poolCount > 9) return '干扰项数量需要在 1 到 9 之间。'
    if (form.options.length + form.poolCount > 10) return '题目选项加上池子抽取的干扰项不能超过 10 个。'
  }

  const totalOptions = form.options.length + (usingPool ? form.poolCount : 0)
  const answer = form.options.flatMap((option, index) =>
    option.correct ? [OPTION_LABELS[index]] : [],
  )
  if (form.type === 'single_choice' && answer.length !== 1) {
    return '单选题需要标记一个正确答案。'
  }
  if (form.type === 'multiple_choice' && answer.length < 2) {
    return '多选题需要标记至少两个正确答案。'
  }
  if (form.type === 'multiple_choice' && answer.length >= totalOptions) {
    return '多选题的正确答案不能覆盖全部选项。'
  }

  return {
    type: form.type,
    stem,
    options: form.options.map((option, index) => ({
      key: OPTION_LABELS[index],
      text: option.text.trim(),
    })),
    answer,
    shuffle: form.shuffle,
    tags,
    chapterPath: chapterPath.length > 0 ? chapterPath : undefined,
    difficulty: form.difficulty,
    source: form.source.trim() || undefined,
    explanation: form.explanation.trim(),
    distractorRule: usingPool ? { poolId: form.poolId, count: form.poolCount } : undefined,
  }
}

function errorField(message: string): EditorErrorField {
  if (message.startsWith('请填写题干') || message.startsWith('题干不能')) return 'stem'
  if (message.startsWith('标签最多')) return 'tags'
  if (message.startsWith('大章') || message.startsWith('小章') || message.startsWith('请先填写大章')) {
    return 'chapter'
  }
  if (message.startsWith('干扰项数量') || message.includes('不能超过 10 个')) return 'pool'
  if (
    message.startsWith('单选题需要') ||
    message.startsWith('多选题需要') ||
    message.startsWith('多选题的正确答案')
  ) {
    return 'answer'
  }
  if (
    message.startsWith('请填写每个选项') ||
    message.startsWith('至少需要两个选项') ||
    message.startsWith('使用选项池时')
  ) {
    return 'options'
  }
  return 'general'
}

export function QuestionEditorPage() {
  const { id, qid } = useParams<{ id: string; qid?: string }>()
  const bankId = id ?? ''

  const accessQuery = useQuery({
    queryKey: ['bank', bankId],
    queryFn: () => api.bank(bankId),
    enabled: Boolean(bankId),
  })

  const questionQuery = useQuery({
    queryKey: ['question', qid],
    queryFn: () => api.question(qid ?? ''),
    enabled: Boolean(qid),
  })

  if (!bankId) {
    return <ErrorNote message="找不到这个题库。" />
  }

  if (accessQuery.isPending || (qid && questionQuery.isPending)) {
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

  if (!accessQuery.data.canEdit) {
    return (
      <div>
        <Link to={`/banks/${bankId}`} className="back-link">
          ← 返回题库
        </Link>
        <ErrorNote message="你没有编辑这个题库的权限。" />
      </div>
    )
  }

  if (qid && questionQuery.isError) {
    return (
      <div>
        <Link to={`/banks/${bankId}`} className="back-link">
          ← 返回题库
        </Link>
        <ErrorNote message={errorMessage(questionQuery.error)} />
      </div>
    )
  }

  const bank = accessQuery.data.bank
  const editing = Boolean(qid)
  const initial = editing ? questionQuery.data : undefined

  return (
    <div>
      <Link to={`/banks/${bankId}`} className="back-link">
        ← 返回 {bank.name}
      </Link>

      <header className="page-header">
        <div className="page-heading">
          <p className="eyebrow">{bank.name}</p>
          <h1>{editing ? '编辑题目' : '新建题目'}</h1>
          <p className="page-sub">题目内容支持 Markdown、公式与图片引用。</p>
        </div>
      </header>

      {editing && !initial ? <Loading /> : null}
      {editing && initial ? (
        <QuestionForm key={initial.id} bankId={bankId} initialQuestion={initial} />
      ) : null}
      {!editing ? <QuestionForm key="new" bankId={bankId} /> : null}
    </div>
  )
}

function SectionHead({
  step,
  title,
  hint,
  meta,
}: {
  step: string
  title: string
  hint: string
  meta?: string
}) {
  return (
    <div className="editor-section-head">
      <span className="editor-step" aria-hidden="true">
        {step}
      </span>
      <div>
        <h2 className="section-title">{title}</h2>
        <p className="editor-section-hint">{hint}</p>
      </div>
      {meta ? <span className="editor-section-meta">{meta}</span> : null}
    </div>
  )
}

function QuestionForm({
  bankId,
  initialQuestion,
}: {
  bankId: string
  initialQuestion?: Question
}) {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [form, setForm] = useState<EditorFormState>(() => buildInitial(initialQuestion))
  const [formError, setFormError] = useState<string | null>(null)

  const poolsQuery = useQuery({
    queryKey: ['pools', bankId],
    queryFn: () => api.pools(bankId),
    retry: false,
  })

  const saveMutation = useMutation({
    mutationFn: (body: SaveQuestionRequest) =>
      initialQuestion
        ? api.updateQuestion(initialQuestion.id, body)
        : api.createQuestion(bankId, body),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['questions', bankId] })
      void queryClient.invalidateQueries({ queryKey: ['bank', bankId] })
      navigate(`/banks/${bankId}`)
    },
  })

  const deleteMutation = useMutation({
    mutationFn: () => {
      if (!initialQuestion) throw new Error('没有可删除的题目。')
      return api.deleteQuestion(initialQuestion.id)
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['questions', bankId] })
      void queryClient.invalidateQueries({ queryKey: ['bank', bankId] })
      navigate(`/banks/${bankId}`)
    },
  })

  function setField<K extends keyof EditorFormState>(key: K, value: EditorFormState[K]) {
    setForm((current) => ({ ...current, [key]: value }))
  }

  // 上传是异步的，用函数式更新，避免把上传期间的名字改动覆盖掉。
  function insertImage(field: 'stem' | 'explanation', markdown: string) {
    setForm((current) =>
      field === 'stem'
        ? { ...current, stem: appendMarkdown(current.stem, markdown) }
        : { ...current, explanation: appendMarkdown(current.explanation, markdown) },
    )
  }

  function setType(type: string) {
    setForm((current) => {
      if (type === current.type) return current
      const firstCorrect = current.options.findIndex((option) => option.correct)
      return {
        ...current,
        type,
        options:
          type === 'single_choice'
            ? current.options.map((option, index) => ({
                ...option,
                correct: index === firstCorrect && firstCorrect >= 0,
              }))
            : current.options,
      }
    })
  }

  function setCorrect(index: number) {
    setForm((current) => ({
      ...current,
      options: current.options.map((option, optionIndex) => ({
        ...option,
        correct:
          current.type === 'single_choice'
            ? optionIndex === index
            : optionIndex === index
              ? !option.correct
              : option.correct,
      })),
    }))
  }

  function updateOptionText(index: number, text: string) {
    setForm((current) => ({
      ...current,
      options: current.options.map((option, optionIndex) =>
        optionIndex === index ? { ...option, text } : option,
      ),
    }))
  }

  function addOption() {
    setForm((current) =>
      current.options.length >= OPTION_LABELS.length
        ? current
        : { ...current, options: [...current.options, { text: '', correct: false }] },
    )
  }

  function removeOption(index: number) {
    setForm((current) => {
      const minimum = current.poolId ? 1 : 2
      return current.options.length <= minimum
        ? current
        : { ...current, options: current.options.filter((_, optionIndex) => optionIndex !== index) }
    })
  }

  function setDistractorSource(usePool: boolean) {
    setForm((current) => {
      if (usePool) {
        return { ...current, poolId: current.poolId || poolsQuery.data?.[0]?.id || '' }
      }

      const options =
        current.options.length >= 2
          ? current.options
          : [...current.options, ...blankOptions(2 - current.options.length)]
      return { ...current, poolId: '', options }
    })
    if (formError) setFormError(null)
  }

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const body = buildRequest(form)
    if (typeof body === 'string') {
      setFormError(body)
      return
    }
    setFormError(null)
    saveMutation.mutate(body)
  }

  function handleDelete() {
    if (!initialQuestion) return
    if (window.confirm('确定删除这道题吗？此操作不可撤销。')) {
      deleteMutation.mutate()
    }
  }

  const errorTarget = formError ? errorField(formError) : null
  const singleChoice = form.type === 'single_choice'
  const usingPool = Boolean(form.poolId)
  const noPools = !poolsQuery.isPending && (poolsQuery.data?.length ?? 0) === 0

  return (
    <form className="editor" onSubmit={handleSubmit} noValidate>
      <section className="card card-pad editor-section">
        <SectionHead
          step="01"
          title="题目内容"
          hint="先选题型，再写题干。右侧预览是同学最终看到的样子。"
        />

        <div className="field">
          <span className="label">题型</span>
          <div className="segmented" role="group" aria-label="题型">
            <button
              type="button"
              className={form.type === 'single_choice' ? 'segment active' : 'segment'}
              aria-pressed={singleChoice}
              onClick={() => setType('single_choice')}
            >
              单选题
            </button>
            <button
              type="button"
              className={form.type === 'multiple_choice' ? 'segment active' : 'segment'}
              aria-pressed={!singleChoice}
              onClick={() => setType('multiple_choice')}
            >
              多选题
            </button>
          </div>
        </div>

        <div className="editor-grid">
          <div className="field">
            <div className="field-label-row">
              <label htmlFor="stem">题干（支持 Markdown 与 $公式$）</label>
              <ImageUpload onInsert={(markdown) => insertImage('stem', markdown)} />
            </div>
            <textarea
              id="stem"
              className="textarea editor-stem"
              rows={10}
              value={form.stem}
              onChange={(event) => setField('stem', event.target.value)}
              placeholder={'输入题干，例如：\n\n已知 $f(x)=x^2+1$，求 $f(2)$。'}
              aria-invalid={errorTarget === 'stem' ? true : undefined}
              aria-describedby={errorTarget === 'stem' ? 'stem-error' : undefined}
            />
            {errorTarget === 'stem' ? (
              <p id="stem-error" className="field-error" role="alert">
                {formError}
              </p>
            ) : null}
          </div>
          <div className="field">
            <span className="label">预览</span>
            <div className="preview-box">
              {form.stem.trim() ? (
                <MarkdownView source={form.stem} />
              ) : (
                <p className="muted">题干会显示在这里。</p>
              )}
            </div>
          </div>
        </div>
      </section>

      <section className="card card-pad editor-section">
        <SectionHead
          step="02"
          title="选项"
          hint={
            usingPool
              ? '勾选正确答案，干扰项每次从选项池随机抽取。'
              : singleChoice
                ? '点选项左侧的圆点标记唯一正确答案。'
                : '点选项左侧的方框标记正确答案，至少两个。'
          }
          meta={`${form.options.length} 个选项`}
        />

        <div className="field">
          <span className="label">干扰项来源</span>
          <div className="segmented" role="group" aria-label="干扰项来源">
            <button
              type="button"
              className={usingPool ? 'segment' : 'segment active'}
              aria-pressed={!usingPool}
              onClick={() => setDistractorSource(false)}
            >
              题目自带
            </button>
            <button
              type="button"
              className={usingPool ? 'segment active' : 'segment'}
              aria-pressed={usingPool}
              onClick={() => setDistractorSource(true)}
              disabled={poolsQuery.isPending || noPools}
            >
              从选项池抽取
            </button>
          </div>
          <p className="form-hint">
            {usingPool
              ? '正确答案由下面勾选的选项决定，干扰项从池子里随机抽。'
              : '选项内容和正确答案都写在下面。'}
          </p>
          {noPools ? (
            <p className="form-hint">
              题库还没有选项池，
              <Link to={`/banks/${bankId}/pools`}>先去建一个</Link>。
            </p>
          ) : null}
        </div>

        {usingPool ? (
          <div className="field">
            <label htmlFor="distractor-pool">选项池</label>
            <div className="form-row pool-source-row">
              <select
                id="distractor-pool"
                className="select"
                value={form.poolId}
                onChange={(event) => setField('poolId', event.target.value)}
              >
                {(poolsQuery.data ?? []).map((pool) => (
                  <option key={pool.id} value={pool.id}>
                    {pool.name}（可用 {pool.availableCount} 条）
                  </option>
                ))}
              </select>
              <input
                type="number"
                className="input pool-count-input"
                min={1}
                max={9}
                value={form.poolCount}
                onChange={(event) => setField('poolCount', Number(event.target.value))}
                aria-label="每次练习从选项池抽取的干扰项数量"
              />
              <span className="pool-count-unit" aria-hidden="true">
                个干扰项
              </span>
            </div>
            <p className="form-hint">
              每次练习随机抽 {form.poolCount} 个干扰项，答案由下面勾选的选项决定。
            </p>
            {errorTarget === 'pool' ? (
              <p className="field-error" role="alert">
                {formError}
              </p>
            ) : null}
          </div>
        ) : null}

        <div className="option-list">
          {form.options.map((option, index) => {
            const label = OPTION_LABELS[index] ?? String(index + 1)
            return (
              <div
                className={option.correct ? 'option-edit is-correct' : 'option-edit'}
                key={index}
              >
                <input
                  type={singleChoice ? 'radio' : 'checkbox'}
                  name="correct-option"
                  checked={option.correct}
                  onChange={() => setCorrect(index)}
                  aria-label={`选项 ${label} 为正确答案`}
                />
                <span className="option-letter small">{label}</span>
                <input
                  className="input"
                  type="text"
                  value={option.text}
                  onChange={(event) => updateOptionText(index, event.target.value)}
                  placeholder={`选项 ${label} 的内容`}
                  aria-label={`选项 ${label} 内容`}
                  aria-invalid={
                    errorTarget === 'options' && !option.text.trim() ? true : undefined
                  }
                />
                <button
                  type="button"
                  className="btn btn-small btn-danger-ghost option-edit-remove"
                  onClick={() => removeOption(index)}
                  disabled={form.options.length <= (form.poolId ? 1 : 2)}
                  aria-label={`删除选项 ${label}`}
                >
                  删除
                </button>
              </div>
            )
          })}
        </div>

        <div className="option-actions">
          <button
            type="button"
            className="btn btn-small btn-ghost"
            onClick={addOption}
            disabled={form.options.length >= OPTION_LABELS.length}
          >
            添加选项
          </button>
          <span className="muted">
            {singleChoice ? '单选' : '多选'} · 选中行会标出正确答案
          </span>
        </div>

        {errorTarget === 'options' || errorTarget === 'answer' ? (
          <p className="field-error" role="alert">
            {formError}
          </p>
        ) : null}
      </section>

      <section className="card card-pad editor-section">
        <SectionHead
          step="03"
          title="分类与解析"
          hint="章节用于按章节练习，标签和难度用于筛选与排序，解析会在同学提交答案后展示。"
        />

        <div className="chapter-fields">
          <div className="form-row">
            <div className="field">
              <label htmlFor="chapter-major">大章（选填）</label>
              <input
                id="chapter-major"
                className="input"
                type="text"
                maxLength={40}
                value={form.chapterMajor}
                onChange={(event) => setField('chapterMajor', event.target.value)}
                placeholder="例如：第一章 函数"
                aria-invalid={errorTarget === 'chapter' ? true : undefined}
                aria-describedby={errorTarget === 'chapter' ? 'chapter-error' : 'chapter-hint'}
              />
            </div>
            <div className="field">
              <label htmlFor="chapter-minor">小章（选填）</label>
              <input
                id="chapter-minor"
                className="input"
                type="text"
                maxLength={40}
                value={form.chapterMinor}
                onChange={(event) => setField('chapterMinor', event.target.value)}
                placeholder="例如：1.2 单调性"
                aria-invalid={errorTarget === 'chapter' ? true : undefined}
                aria-describedby={errorTarget === 'chapter' ? 'chapter-error' : 'chapter-hint'}
              />
            </div>
          </div>
          {errorTarget === 'chapter' ? (
            <p id="chapter-error" className="field-error" role="alert">
              {formError}
            </p>
          ) : (
            <p id="chapter-hint" className="form-hint">
              每项最多 40 个字，只填大章也可以；留空表示不归入章节。
            </p>
          )}
        </div>

        <div className="form-row">
          <div className="field">
            <label htmlFor="tags">标签</label>
            <input
              id="tags"
              className="input"
              type="text"
              value={form.tags}
              onChange={(event) => setField('tags', event.target.value)}
              placeholder="用逗号分隔，如：函数, 单调性"
              aria-invalid={errorTarget === 'tags' ? true : undefined}
              aria-describedby={errorTarget === 'tags' ? 'tags-error' : undefined}
            />
            {errorTarget === 'tags' ? (
              <p id="tags-error" className="field-error" role="alert">
                {formError}
              </p>
            ) : (
              <p className="form-hint">最多 10 个，用逗号、顿号分隔。</p>
            )}
          </div>
          <div className="field">
            <label htmlFor="difficulty">难度</label>
            <select
              id="difficulty"
              className="select"
              value={form.difficulty}
              onChange={(event) => setField('difficulty', Number(event.target.value))}
            >
              {[1, 2, 3, 4, 5].map((level) => (
                <option key={level} value={level}>
                  {level} 级
                </option>
              ))}
            </select>
            <p className="form-hint">1 级最易，5 级最难。</p>
          </div>
          <div className="field">
            <label htmlFor="shuffle">选项乱序</label>
            <select
              id="shuffle"
              className="select"
              value={form.shuffle}
              onChange={(event) => setField('shuffle', event.target.value)}
            >
              {Object.entries(SHUFFLE_LABELS).map(([value, label]) => (
                <option key={value} value={value}>
                  {label}
                </option>
              ))}
            </select>
            <p className="form-hint">{SHUFFLE_HINTS[form.shuffle] ?? ''}</p>
          </div>
          <div className="field">
            <label htmlFor="source">来源（选填）</label>
            <input
              id="source"
              className="input"
              type="text"
              value={form.source}
              onChange={(event) => setField('source', event.target.value)}
              placeholder="例如：期中复习卷 第 8 题"
            />
            <p className="form-hint">方便日后追溯出处。</p>
          </div>
        </div>

        <div className="field">
          <div className="field-label-row">
            <label htmlFor="explanation">解析（支持 Markdown）</label>
            <ImageUpload onInsert={(markdown) => insertImage('explanation', markdown)} />
          </div>
          <textarea
            id="explanation"
            className="textarea"
            rows={4}
            value={form.explanation}
            onChange={(event) => setField('explanation', event.target.value)}
            placeholder="可选，提交答案后展示。"
          />
        </div>
      </section>

      {errorTarget === 'general' ? <ErrorNote message={formError ?? ''} /> : null}
      {saveMutation.isError ? <ErrorNote message={errorMessage(saveMutation.error)} /> : null}
      {deleteMutation.isError ? <ErrorNote message={errorMessage(deleteMutation.error)} /> : null}

      <div className="editor-actions editor-actions-sticky">
        {initialQuestion ? (
          <button
            type="button"
            className="btn btn-danger-ghost editor-delete"
            onClick={handleDelete}
            disabled={deleteMutation.isPending || saveMutation.isPending}
          >
            {deleteMutation.isPending ? '删除中…' : '删除题目'}
          </button>
        ) : null}
        <Link to={`/banks/${bankId}`} className="btn btn-ghost">
          取消
        </Link>
        <button type="submit" className="btn btn-primary" disabled={saveMutation.isPending}>
          {saveMutation.isPending ? '保存中…' : '保存题目'}
        </button>
      </div>
    </form>
  )
}
