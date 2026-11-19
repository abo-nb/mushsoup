import { useEffect, useMemo, useRef, useState, type FormEvent } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { useMutation, useQuery } from '@tanstack/react-query'
import { api, errorMessage } from '../api'
import type { QuestionSummary } from '../api'
import { Icon } from '../components/Icon'
import { ErrorNote, Loading } from '../components/Ui'
import { ShareButton } from '../components/ShareButton'
import { FavoriteButton } from '../components/FavoriteButton'
import { naturalCompare } from '../natural-sort'
import {
  buildChapterLeaves,
  leafNumberLabel,
  resolveChapterRange,
} from '../chapter-range'
import { useRecordRecent } from '../recent'

const CHAPTER_SEP = '\u001f'

const MODE_LABELS: Record<string, string> = {
  sequence: '顺序练习',
  daily: '每日练习',
  endless: '无限刷题',
}

// 与后端默认一致：每日/顺序练习一次 20 题，上限 100。
const PRACTICE_LIMITS = [10, 20, 50, 100]
const PRACTICE_LIMIT_KEY = 'ms_practice_limit'
const DEFAULT_PRACTICE_LIMIT = 20

function readPracticeLimit(): number {
  try {
    const stored = Number(window.localStorage.getItem(PRACTICE_LIMIT_KEY))
    return PRACTICE_LIMITS.includes(stored) ? stored : DEFAULT_PRACTICE_LIMIT
  } catch {
    return DEFAULT_PRACTICE_LIMIT
  }
}

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
    .sort(([left], [right]) => naturalCompare(left, right))
    .map(([major, entry]) => ({
      name: major,
      key: chapterKey([major]),
      count: entry.count,
      directCount: entry.direct,
      minors: [...entry.minors.entries()]
        .sort(([left], [right]) => naturalCompare(left, right))
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

export function BankDetailPage() {
  const { id } = useParams<{ id: string }>()
  useRecordRecent('bank', id)
  const bankId = id ?? ''
  const navigate = useNavigate()
  const [limit, setLimit] = useState(readPracticeLimit)
  const [selectedChapters, setSelectedChapters] = useState<Set<string>>(() => new Set())
  const [rangeText, setRangeText] = useState('')
  const [rangeError, setRangeError] = useState('')
  const moreMenuRef = useRef<HTMLDetailsElement>(null)

  const accessQuery = useQuery({
    queryKey: ['bank', bankId],
    queryFn: () => api.bank(bankId),
    enabled: Boolean(bankId),
    staleTime: 60_000,
  })

  const allQuestionsQuery = useQuery({
    queryKey: ['questions', bankId, '', ''],
    queryFn: () => api.questions(bankId),
    enabled: Boolean(bankId) && Boolean(accessQuery.data?.canView),
    staleTime: 60_000,
  })

  // 上次没做完的练习，用来在页面上给一个「继续」入口。
  const activeSessionQuery = useQuery({
    queryKey: ['active-session', bankId],
    queryFn: () => api.activeSession(bankId),
    enabled: Boolean(bankId) && Boolean(accessQuery.data?.canView),
    retry: false,
    // 练习进度必须即时反映，不沿用全局 60 秒缓存。
    staleTime: 0,
  })

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

  const chapterTree = useMemo(
    () => buildChapterTree(allQuestionsQuery.data ?? []),
    [allQuestionsQuery.data],
  )
  const chapters = chapterTree.branches

  const chapterLeaves = useMemo(() => buildChapterLeaves(chapters), [chapters])
  const rangeExample = useMemo(() => {
    const numbered = chapterLeaves.filter((leaf) => leaf.tokens.length > 0)
    if (numbered.length < 2) return null
    return {
      start: leafNumberLabel(numbered[0]),
      end: leafNumberLabel(numbered[numbered.length - 1]),
    }
  }, [chapterLeaves])

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
    mutationFn: ({
      mode,
      chapters: paths,
      limit: count,
    }: {
      mode: string
      chapters?: string[][]
      limit?: number
    }) => api.startSession(bankId, mode, count, paths),
    onSuccess: (session) => navigate(`/practice/${session.id}`),
  })

  function changePracticeLimit(next: number) {
    setLimit(next)
    try {
      window.localStorage.setItem(PRACTICE_LIMIT_KEY, String(next))
    } catch {
      // 隐私模式下写入会失败，本次选择仍然生效。
    }
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

  function branchSelection(branch: ChapterBranch) {
    const selectedMinors = branch.minors.filter((minor) => selectedChapters.has(minor.key)).length
    const checked =
      branch.minors.length === 0
        ? selectedChapters.has(branch.key)
        : selectedMinors === branch.minors.length &&
          (branch.directCount === 0 || selectedChapters.has(branch.key))
    const mixed =
      !checked &&
      (selectedMinors > 0 || (branch.directCount > 0 && selectedChapters.has(branch.key)))
    return { checked, mixed }
  }

  function applyChapterRange(event: FormEvent) {
    event.preventDefault()
    const result = resolveChapterRange(rangeText, chapters)
    if ('error' in result) {
      setRangeError(result.error)
      return
    }
    setRangeError('')
    setSelectedChapters(result.keys)
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
        <Link to="/banks" className="back-link">
          ← 返回题库
        </Link>
        <ErrorNote message={errorMessage(accessQuery.error)} />
      </div>
    )
  }

  const access = accessQuery.data
  const bank = access.bank
  const startDisabled = bank.questionCount === 0 || startMutation.isPending

  return (
    <div>
      <Link to="/banks" className="back-link">
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
          {bank.visibility === 'public' ? (
            <>
              <FavoriteButton kind="bank" itemId={bank.id} />
              <ShareButton kind="bank" targetId={bank.id} />
            </>
          ) : null}
          <details className="more-menu" ref={moreMenuRef}>
            <summary className="btn btn-ghost more-summary">更多</summary>
            <div className="more-panel">
              {access.isOwner ? (
                <Link to={`/banks/${bank.id}/settings`} className="more-item">
                  <Icon name="settings" size={16} />
                  <span>题库设置</span>
                </Link>
              ) : null}
              <Link to={`/banks/${bank.id}/questions`} className="more-item">
                <Icon name="document" size={16} />
                <span>题目设置</span>
              </Link>
              <Link to={`/banks/${bank.id}/pools`} className="more-item">
                <Icon name="layers" size={16} />
                <span>选项池</span>
              </Link>
            </div>
          </details>
          <label className="practice-count">
            <span className="practice-count-label">题数</span>
            <select
              className="select practice-count-select"
              value={limit}
              onChange={(event) => changePracticeLimit(Number(event.target.value))}
            >
              {PRACTICE_LIMITS.map((item) => (
                <option key={item} value={item}>
                  {item} 题
                </option>
              ))}
            </select>
          </label>
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
            onClick={() => startMutation.mutate({ mode: 'daily', limit })}
            disabled={startDisabled}
            title={bank.questionCount === 0 ? '题库还没有题目' : undefined}
          >
            {startMutation.isPending ? '正在开始…' : '开始练习'}
          </button>
        </div>
      </header>

      {startMutation.isError ? <ErrorNote message={errorMessage(startMutation.error)} /> : null}

      {activeSessionQuery.data ? (
        <section className="card resume-card" aria-labelledby="resume-title">
          <div className="resume-body">
            <p className="eyebrow">继续练习</p>
            <h2 id="resume-title">
              {MODE_LABELS[activeSessionQuery.data.mode] ?? '练习'} · 已答{' '}
              {activeSessionQuery.data.answered} / {activeSessionQuery.data.total}
            </h2>
          </div>
          <Link to={`/practice/${activeSessionQuery.data.id}`} className="btn btn-primary">
            <Icon name="play" size={16} />
            继续
          </Link>
        </section>
      ) : null}

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

          <div className="chapter-quick">
            {chapters.length > 1 ? (
              <div className="chapter-quick-row">
                <span className="chapter-quick-label">大章</span>
                <div className="chapter-chips" role="group" aria-label="快速选择大章">
                  {chapters.map((branch) => {
                    const state = branchSelection(branch)
                    return (
                      <button
                        key={branch.key}
                        type="button"
                        className={
                          state.checked
                            ? 'chapter-chip is-active'
                            : state.mixed
                              ? 'chapter-chip is-mixed'
                              : 'chapter-chip'
                        }
                        aria-pressed={state.checked}
                        title={`${branch.name} · ${branch.count} 题`}
                        onClick={() => toggleMajor(branch, !state.checked)}
                      >
                        {branch.name}
                      </button>
                    )
                  })}
                </div>
              </div>
            ) : null}

            {rangeExample ? (
              <form className="chapter-quick-row chapter-range" onSubmit={applyChapterRange}>
                <label className="chapter-quick-label" htmlFor="chapter-range-input">
                  范围
                </label>
                <input
                  id="chapter-range-input"
                  className="input chapter-range-input"
                  type="text"
                  autoComplete="off"
                  placeholder={`如 ${rangeExample.start} ~ ${rangeExample.end}`}
                  aria-label="章节范围"
                  aria-invalid={rangeError ? true : undefined}
                  aria-describedby={rangeError ? 'chapter-range-error' : undefined}
                  value={rangeText}
                  onChange={(event) => {
                    setRangeText(event.target.value)
                    if (rangeError) setRangeError('')
                  }}
                />
                <button
                  type="submit"
                  className="btn btn-small btn-ghost"
                  disabled={rangeText.trim() === ''}
                >
                  选这段
                </button>
                {rangeError ? (
                  <span className="chapter-range-error" id="chapter-range-error" role="alert">
                    {rangeError}
                  </span>
                ) : null}
              </form>
            ) : null}

            <div className="chapter-quick-row chapter-quick-start">
              <button
                type="button"
                className="btn btn-small btn-primary"
                onClick={() =>
                  startMutation.mutate({ mode: 'daily', chapters: selectedPaths, limit })
                }
                disabled={selectedQuestionCount === 0 || startMutation.isPending}
              >
                {startMutation.isPending ? '正在开始…' : '练习所选章节'}
              </button>
              <button
                type="button"
                className="btn btn-small btn-ghost"
                onClick={() => startMutation.mutate({ mode: 'endless', chapters: selectedPaths })}
                disabled={selectedQuestionCount === 0 || startMutation.isPending}
              >
                无限刷题
              </button>
              <span className="chapter-hint">最多 {limit} 题，无限刷题不受限</span>
            </div>
          </div>

          <ul className="chapter-tree">
            {chapters.map((branch) => {
              const { checked: majorChecked, mixed: majorMixed } = branchSelection(branch)

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
                startMutation.mutate({ mode: 'daily', chapters: selectedPaths, limit })
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

      {allQuestionsQuery.data && allQuestionsQuery.data.length === 0 ? (
        <p className="chapter-note chapter-note-block">
          {access.canEdit
            ? '题库还没有题目。到「题目设置」里新建或批量导入。'
            : '题库还没有题目。'}
        </p>
      ) : null}
    </div>
  )
}
