import { memo, useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, errorMessage } from '../api'
import type { QuestionSummary } from '../api'
import { Icon } from '../components/Icon'
import { DifficultyDots, EmptyState, ErrorNote, Loading } from '../components/Ui'
import { formatDate, previewText, typeLabel } from '../format'

const CHAPTER_SEP = '\u001f'

function chapterKey(path: string[]): string {
  return path.join(CHAPTER_SEP)
}

interface ChapterMinor {
  name: string
  key: string
  count: number
}

interface ChapterBranch {
  name: string
  key: string
  count: number
  directCount: number
  minors: ChapterMinor[]
}

function buildChapterTree(questions: QuestionSummary[]) {
  const majors = new Map<string, { count: number; direct: number; minors: Map<string, number> }>()
  let uncategorized = 0

  for (const question of questions) {
    const path = question.chapterPath ?? []
    const major = path[0]
    if (!major) {
      uncategorized += 1
      continue
    }

    const entry = majors.get(major) ?? { count: 0, direct: 0, minors: new Map<string, number>() }
    entry.count += 1
    const minor = path[1]
    if (minor) {
      entry.minors.set(minor, (entry.minors.get(minor) ?? 0) + 1)
    } else {
      entry.direct += 1
    }
    majors.set(major, entry)
  }

  const branches: ChapterBranch[] = [...majors.entries()]
    .sort(([left], [right]) => left.localeCompare(right, 'zh-CN'))
    .map(([major, entry]) => ({
      name: major,
      key: chapterKey([major]),
      count: entry.count,
      directCount: entry.direct,
      minors: [...entry.minors.entries()]
        .sort(([left], [right]) => left.localeCompare(right, 'zh-CN'))
        .map(([minor, count]) => ({ name: minor, key: chapterKey([major, minor]), count })),
    }))

  return { branches, uncategorized }
}

function isPathPrefix(prefix: string[], path: string[]): boolean {
  return prefix.length <= path.length && prefix.every((part, index) => path[index] === part)
}

function ChapterCheck({
  checked,
  mixed,
  name,
  count,
  isMajor,
  onChange,
}: {
  checked: boolean
  mixed?: boolean
  name: string
  count: number
  isMajor?: boolean
  onChange: (next: boolean) => void
}) {
  const ref = useRef<HTMLInputElement>(null)

  useEffect(() => {
    if (ref.current) ref.current.indeterminate = Boolean(mixed)
  }, [mixed])

  return (
    <label className={isMajor ? 'chapter-check is-major' : 'chapter-check'}>
      <input
        ref={ref}
        type="checkbox"
        checked={checked}
        aria-checked={mixed ? 'mixed' : undefined}
        onChange={(event) => onChange(event.target.checked)}
      />
      <span className="chapter-name">{name}</span>
      <span className="chapter-count">{count}</span>
    </label>
  )
}

/**
 * 题目行单独 memo：章节勾选只更新章节面板，不必重建上千行题目。
 * 这里用到的 props 在章节切换时都保持引用稳定。
 */
const QuestionList = memo(function QuestionList({
  questions,
  bankId,
  canEdit,
  deleting,
  onPickTag,
  onDelete,
}: {
  questions: QuestionSummary[]
  bankId: string
  canEdit: boolean
  deleting: boolean
  onPickTag: (tag: string) => void
  onDelete: (question: QuestionSummary) => void
}) {
  return (
    <ul className="question-list">
      {questions.map((question) => (
        <li key={question.id} className="question-row">
          <div className="question-row-main">
            {canEdit ? (
              <Link
                to={`/banks/${bankId}/questions/${question.id}`}
                className="question-stem-link"
              >
                {previewText(question.stem)}
              </Link>
            ) : (
              <span className="question-stem-link static">{previewText(question.stem)}</span>
            )}
            <div className="question-meta">
              <span className="badge badge-quiet">{typeLabel(question.type)}</span>
              <span className="meta-item">
                难度 <DifficultyDots value={question.difficulty} />
              </span>
              {question.tags.slice(0, 3).map((item) => (
                <button
                  key={item}
                  type="button"
                  className="chip chip-small"
                  onClick={() => onPickTag(item)}
                >
                  {item}
                </button>
              ))}
              <span className="meta-time">更新于 {formatDate(question.updatedAt)}</span>
            </div>
          </div>
          {canEdit ? (
            <div className="question-actions">
              <Link
                to={`/banks/${bankId}/questions/${question.id}`}
                className="btn btn-small btn-ghost"
              >
                编辑
              </Link>
              <button
                type="button"
                className="btn btn-small btn-danger-ghost"
                onClick={() => onDelete(question)}
                disabled={deleting}
              >
                删除
              </button>
            </div>
          ) : null}
        </li>
      ))}
    </ul>
  )
})

export function BankDetailPage() {
  const { id } = useParams<{ id: string }>()
  const bankId = id ?? ''
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [search, setSearch] = useState('')
  const [query, setQuery] = useState('')
  const [tag, setTag] = useState('')
  const [selectedChapters, setSelectedChapters] = useState<Set<string>>(() => new Set())
  const moreMenuRef = useRef<HTMLDetailsElement>(null)

  const accessQuery = useQuery({
    queryKey: ['bank', bankId],
    queryFn: () => api.bank(bankId),
    enabled: Boolean(bankId),
  })

  const allQuestionsQuery = useQuery({
    queryKey: ['questions', bankId, '', ''],
    queryFn: () => api.questions(bankId),
    enabled: Boolean(bankId) && Boolean(accessQuery.data?.canView),
  })

  const questionsQuery = useQuery({
    queryKey: ['questions', bankId, query, tag],
    queryFn: () => api.questions(bankId, { q: query, tag }),
    enabled: Boolean(bankId) && Boolean(accessQuery.data?.canView),
  })

  useEffect(() => {
    const timer = window.setTimeout(() => setQuery(search.trim()), 260)
    return () => window.clearTimeout(timer)
  }, [search])

  useEffect(() => {
    function handlePointerDown(event: PointerEvent) {
      const menu = moreMenuRef.current
      if (!menu?.open) return
      if (event.target instanceof Node && !menu.contains(event.target)) {
        menu.open = false
      }
    }

    function handleKeyDown(event: KeyboardEvent) {
      const menu = moreMenuRef.current
      if (event.key !== 'Escape' || !menu?.open) return
      menu.open = false
      menu.querySelector('summary')?.focus()
    }

    document.addEventListener('pointerdown', handlePointerDown)
    document.addEventListener('keydown', handleKeyDown)
    return () => {
      document.removeEventListener('pointerdown', handlePointerDown)
      document.removeEventListener('keydown', handleKeyDown)
    }
  }, [])

  const tags = useMemo(() => {
    const set = new Set<string>()
    for (const question of allQuestionsQuery.data ?? []) {
      for (const item of question.tags) set.add(item)
    }
    return [...set].sort((a, b) => a.localeCompare(b, 'zh-CN'))
  }, [allQuestionsQuery.data])

  const chapterTree = useMemo(
    () => buildChapterTree(allQuestionsQuery.data ?? []),
    [allQuestionsQuery.data],
  )
  const chapters = chapterTree.branches

  // 选中项只存路径 key，列表刷新或重新筛选都不会丢。
  const selectedPaths = useMemo(() => {
    const paths = [...selectedChapters].map((key) => key.split(CHAPTER_SEP))
    return paths.filter(
      (path) => !paths.some((other) => other.length < path.length && isPathPrefix(other, path)),
    )
  }, [selectedChapters])

  const selectedQuestionCount = useMemo(() => {
    if (selectedPaths.length === 0) return 0
    return (allQuestionsQuery.data ?? []).filter((question) =>
      selectedPaths.some((prefix) => isPathPrefix(prefix, question.chapterPath ?? [])),
    ).length
  }, [allQuestionsQuery.data, selectedPaths])

  const allChaptersSelected = useMemo(
    () =>
      chapters.length > 0 &&
      chapters.every((branch) =>
        branch.minors.length === 0
          ? selectedChapters.has(branch.key)
          : branch.minors.every((minor) => selectedChapters.has(minor.key)) &&
            (branch.directCount === 0 || selectedChapters.has(branch.key)),
      ),
    [chapters, selectedChapters],
  )

  const startMutation = useMutation({
    mutationFn: ({ mode, chapters: paths }: { mode: string; chapters?: string[][] }) =>
      api.startSession(bankId, mode, undefined, paths),
    onSuccess: (session) => navigate(`/practice/${session.id}`),
  })

  const deleteMutation = useMutation({
    mutationFn: (questionId: string) => api.deleteQuestion(questionId),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['questions', bankId] })
      void queryClient.invalidateQueries({ queryKey: ['bank', bankId] })
    },
  })

  const handleDelete = useCallback(
    (question: QuestionSummary) => {
      const ok = window.confirm('确定删除这道题吗？此操作不可撤销。')
      if (ok) deleteMutation.mutate(question.id)
    },
    [deleteMutation.mutate],
  )

  function clearFilters() {
    setSearch('')
    setQuery('')
    setTag('')
  }

  function toggleMinor(branch: ChapterBranch, minorKey: string, next: boolean) {
    setSelectedChapters((current) => {
      const updated = new Set(current)
      if (next) {
        updated.add(minorKey)
        // 小章全选时才把「直属大章」的题目一并纳入，避免半选时前缀覆盖多选。
        if (branch.directCount > 0 && branch.minors.every((minor) => updated.has(minor.key))) {
          updated.add(branch.key)
        }
      } else {
        updated.delete(minorKey)
        updated.delete(branch.key)
      }
      return updated
    })
  }

  function toggleMajor(branch: ChapterBranch, next: boolean) {
    setSelectedChapters((current) => {
      const updated = new Set(current)
      if (next) {
        if (branch.minors.length === 0) {
          updated.add(branch.key)
        } else {
          for (const minor of branch.minors) updated.add(minor.key)
          if (branch.directCount > 0) updated.add(branch.key)
        }
      } else {
        for (const minor of branch.minors) updated.delete(minor.key)
        updated.delete(branch.key)
      }
      return updated
    })
  }

  function selectAllChapters() {
    setSelectedChapters(() => {
      const updated = new Set<string>()
      for (const branch of chapters) {
        if (branch.minors.length === 0) {
          updated.add(branch.key)
        } else {
          for (const minor of branch.minors) updated.add(minor.key)
        }
        if (branch.directCount > 0) updated.add(branch.key)
      }
      return updated
    })
  }

  function clearChapterSelection() {
    setSelectedChapters(new Set())
  }

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
  const filtering = Boolean(query || tag)
  const startDisabled = bank.questionCount === 0 || startMutation.isPending

  return (
    <div>
      <Link to="/" className="back-link">
        ← 返回题库
      </Link>

      <header className="page-header">
        <div className="page-heading">
          <p className="eyebrow">{bank.visibility === 'public' ? '公开题库' : '私有题库'}</p>
          <h1>{bank.name}</h1>
          {bank.description ? <p className="page-sub">{bank.description}</p> : null}
          <p className="bank-line">
            {bank.questionCount} 道题
            <span className="dot-sep">·</span>
            {access.canEdit ? '你可以编辑此题库' : '只读'}
          </p>
        </div>
        <div className="header-actions bank-actions">
          <details className="more-menu" ref={moreMenuRef}>
            <summary className="btn btn-ghost more-summary">更多</summary>
            <div className="more-panel">
              {access.isOwner ? (
                <Link to={`/banks/${bank.id}/settings`} className="more-item">
                  <Icon name="settings" size={16} />
                  <span>题库设置</span>
                </Link>
              ) : null}
              <Link to={`/banks/${bank.id}/pools`} className="more-item">
                <Icon name="layers" size={16} />
                <span>选项池</span>
              </Link>
              {access.canEdit ? (
                <Link to={`/banks/${bank.id}/import`} className="more-item">
                  <Icon name="upload" size={16} />
                  <span>批量导入</span>
                </Link>
              ) : null}
              {access.canEdit ? (
                <Link to={`/banks/${bank.id}/questions/new`} className="more-item">
                  <Icon name="plus" size={16} />
                  <span>新建题目</span>
                </Link>
              ) : null}
            </div>
          </details>
          <button
            type="button"
            className="btn btn-ghost"
            onClick={() => startMutation.mutate({ mode: 'endless' })}
            disabled={startDisabled}
            title={bank.questionCount === 0 ? '题库还没有题目' : undefined}
          >
            无限刷题
          </button>
          <button
            type="button"
            className="btn btn-primary"
            onClick={() => startMutation.mutate({ mode: 'daily' })}
            disabled={startDisabled}
            title={bank.questionCount === 0 ? '题库还没有题目' : undefined}
          >
            {startMutation.isPending ? '正在开始…' : '开始练习'}
          </button>
        </div>
      </header>

      {startMutation.isError ? <ErrorNote message={errorMessage(startMutation.error)} /> : null}
      {deleteMutation.isError ? <ErrorNote message={errorMessage(deleteMutation.error)} /> : null}

      {chapters.length > 0 ? (
        <section className="card chapter-panel" aria-labelledby="chapter-panel-title">
          <div className="chapter-panel-head">
            <div className="chapter-panel-intro">
              <h2 id="chapter-panel-title" className="section-title">
                按章节练习
              </h2>
              <p className="chapter-hint">勾选小章可以组合练习；选中大章会包含它下面的全部小节。</p>
            </div>
            <div className="chapter-toolbar">
              <button
                type="button"
                className="btn btn-small btn-ghost"
                onClick={selectAllChapters}
                disabled={allChaptersSelected}
              >
                全选
              </button>
              <button
                type="button"
                className="btn btn-small btn-ghost"
                onClick={clearChapterSelection}
                disabled={selectedChapters.size === 0}
              >
                清空
              </button>
              <span className="chapter-selected" role="status">
                已选 {selectedQuestionCount} 题
              </span>
            </div>
          </div>

          <ul className="chapter-tree">
            {chapters.map((branch) => {
              const selectedMinors = branch.minors.filter((minor) =>
                selectedChapters.has(minor.key),
              ).length
              const majorChecked =
                branch.minors.length === 0
                  ? selectedChapters.has(branch.key)
                  : selectedMinors === branch.minors.length &&
                    (branch.directCount === 0 || selectedChapters.has(branch.key))
              const majorMixed =
                !majorChecked &&
                (selectedMinors > 0 ||
                  (branch.directCount > 0 && selectedChapters.has(branch.key)))

              return (
                <li key={branch.key} className="chapter-branch">
                  <ChapterCheck
                    isMajor
                    checked={majorChecked}
                    mixed={majorMixed}
                    name={branch.name}
                    count={branch.count}
                    onChange={(next) => toggleMajor(branch, next)}
                  />
                  {branch.minors.length > 0 ? (
                    <ul className="chapter-minors">
                      {branch.minors.map((minor) => (
                        <li key={minor.key}>
                          <ChapterCheck
                            checked={selectedChapters.has(minor.key)}
                            name={minor.name}
                            count={minor.count}
                            onChange={(next) => toggleMinor(branch, minor.key, next)}
                          />
                        </li>
                      ))}
                    </ul>
                  ) : null}
                </li>
              )
            })}
          </ul>

          {chapterTree.uncategorized > 0 ? (
            <p className="chapter-note">
              另有 {chapterTree.uncategorized} 道题目还没有章节，不受章节筛选影响。
            </p>
          ) : null}

          <div className="chapter-actions">
            <button
              type="button"
              className="btn btn-primary"
              onClick={() =>
                startMutation.mutate({ mode: 'daily', chapters: selectedPaths })
              }
              disabled={selectedQuestionCount === 0 || startMutation.isPending}
            >
              {startMutation.isPending ? '正在开始…' : '练习所选章节'}
            </button>
            <button
              type="button"
              className="btn btn-ghost"
              onClick={() =>
                startMutation.mutate({ mode: 'endless', chapters: selectedPaths })
              }
              disabled={selectedQuestionCount === 0 || startMutation.isPending}
            >
              无限刷题
            </button>
            <p className="chapter-hint chapter-actions-hint">只练勾选的章节，其余题目不受影响。</p>
          </div>
        </section>
      ) : allQuestionsQuery.data && allQuestionsQuery.data.length > 0 ? (
        <p className="chapter-note chapter-note-block">
          题目还没有章节。给题目填上「大章 / 小章」后，就能按章节练习。
        </p>
      ) : null}

      <section className="card list-card">
        <div className="list-toolbar">
          <div className="search-field">
            <Icon name="search" size={16} className="search-field-icon" />
            <input
              type="search"
              className="input search-input"
              placeholder="搜索题干…"
              aria-label="搜索题目"
              value={search}
              onChange={(event) => setSearch(event.target.value)}
            />
            {search ? (
              <button
                type="button"
                className="search-clear"
                aria-label="清除搜索"
                onClick={() => {
                  setSearch('')
                  setQuery('')
                }}
              >
                <Icon name="x" size={14} />
              </button>
            ) : null}
          </div>
          <span className="muted" role="status">
            {questionsQuery.data ? `${questionsQuery.data.length} 道题目` : '题目列表'}
          </span>
        </div>

        {tags.length > 0 ? (
          <div className="tag-filter" role="group" aria-label="按标签筛选">
            <button
              type="button"
              className={tag ? 'chip' : 'chip active'}
              onClick={() => setTag('')}
            >
              全部
            </button>
            {tags.map((item) => (
              <button
                key={item}
                type="button"
                className={tag === item ? 'chip active' : 'chip'}
                onClick={() => setTag(tag === item ? '' : item)}
              >
                {item}
              </button>
            ))}
          </div>
        ) : null}

        {questionsQuery.isPending ? <Loading /> : null}
        {questionsQuery.isError ? (
          <div className="list-pad">
            <ErrorNote message={errorMessage(questionsQuery.error)} />
          </div>
        ) : null}

        {questionsQuery.data && questionsQuery.data.length > 0 ? (
          <QuestionList
            questions={questionsQuery.data}
            bankId={bank.id}
            canEdit={access.canEdit}
            deleting={deleteMutation.isPending}
            onPickTag={setTag}
            onDelete={handleDelete}
          />
        ) : null}

        {questionsQuery.data && questionsQuery.data.length === 0 ? (
          <EmptyState
            title={filtering ? '没有找到匹配的题目' : '题库还是空的'}
            hint={filtering ? '换个关键词，或清除筛选条件。' : '把第一道题放进来吧。'}
          >
            {filtering ? (
              <button type="button" className="btn btn-ghost" onClick={clearFilters}>
                <Icon name="x" size={16} />
                清除筛选
              </button>
            ) : access.canEdit ? (
              <Link to={`/banks/${bank.id}/questions/new`} className="btn btn-primary">
                <Icon name="plus" size={16} />
                新建题目
              </Link>
            ) : null}
          </EmptyState>
        ) : null}
      </section>
    </div>
  )
}
