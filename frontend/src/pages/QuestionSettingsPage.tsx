import { memo, useCallback, useEffect, useMemo, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, errorMessage } from '../api'
import type { QuestionSummary } from '../api'
import { Icon } from '../components/Icon'
import { DifficultyDots, EmptyState, ErrorNote, Loading } from '../components/Ui'
import { formatDate, previewText, typeLabel } from '../format'

/**
 * 题目行单独 memo：搜索和筛选项变化时才重建列表。
 * 这里用到的 props 在筛选切换时都保持引用稳定。
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

export function QuestionSettingsPage() {
  const { id } = useParams<{ id: string }>()
  const bankId = id ?? ''
  const queryClient = useQueryClient()
  const [search, setSearch] = useState('')
  const [query, setQuery] = useState('')
  const [tag, setTag] = useState('')

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

  const questionsQuery = useQuery({
    queryKey: ['questions', bankId, query, tag],
    queryFn: () => api.questions(bankId, { q: query, tag }),
    enabled: Boolean(bankId) && Boolean(accessQuery.data?.canView),
    staleTime: 60_000,
    placeholderData: keepPreviousData,
  })

  useEffect(() => {
    const timer = window.setTimeout(() => setQuery(search.trim()), 260)
    return () => window.clearTimeout(timer)
  }, [search])

  const tags = useMemo(() => {
    const set = new Set<string>()
    for (const question of allQuestionsQuery.data ?? []) {
      for (const item of question.tags) set.add(item)
    }
    return [...set].sort((a, b) => a.localeCompare(b, 'zh-CN'))
  }, [allQuestionsQuery.data])

  const deleteMutation = useMutation({
    mutationFn: (questionId: string) => api.deleteQuestion(questionId),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['questions', bankId] })
      void queryClient.invalidateQueries({ queryKey: ['bank', bankId] })
      void queryClient.invalidateQueries({ queryKey: ['banks'] })
    },
  })

  const { mutate: deleteQuestion } = deleteMutation

  const handleDelete = useCallback(
    (question: QuestionSummary) => {
      const ok = window.confirm('确定删除这道题吗？此操作不可撤销。')
      if (ok) deleteQuestion(question.id)
    },
    [deleteQuestion],
  )

  function clearFilters() {
    setSearch('')
    setQuery('')
    setTag('')
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

  if (!access.canView) {
    return (
      <div>
        <Link to="/banks" className="back-link">
          ← 返回题库
        </Link>
        <ErrorNote message="找不到这个题库。" />
      </div>
    )
  }

  const filtering = Boolean(query || tag)

  return (
    <div>
      <Link to={`/banks/${bankId}`} className="back-link">
        ← 返回题库
      </Link>

      <header className="page-header">
        <div className="page-heading">
          <p className="eyebrow">题目设置</p>
          <h1>{bank.name}</h1>
          <p className="page-sub">搜索、编辑或删除题目；新建题目和批量导入也在这里。</p>
        </div>
        <div className="header-actions">
          {access.canEdit ? (
            <Link to={`/banks/${bank.id}/questions/new`} className="btn btn-primary">
              <Icon name="plus" size={16} />
              新建题目
            </Link>
          ) : null}
          {access.canEdit ? (
            <Link to={`/banks/${bank.id}/import`} className="btn btn-ghost">
              <Icon name="upload" size={16} />
              批量导入
            </Link>
          ) : null}
        </div>
      </header>

      {deleteMutation.isError ? (
        <ErrorNote message={errorMessage(deleteMutation.error)} />
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
