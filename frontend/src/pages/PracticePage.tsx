import { useCallback, useEffect, useRef, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { useMutation, useQuery } from '@tanstack/react-query'
import { api, errorMessage } from '../api'
import type { AnswerFeedback, SubmitAnswerBody } from '../api'
import { useAuth } from '../auth-context'
import { Icon } from '../components/Icon'
import { MarkdownView } from '../components/MarkdownView'
import { ErrorNote, Loading } from '../components/Ui'
import { typeLabel } from '../format'

const MODE_LABELS: Record<string, string> = {
  sequence: '顺序练习',
  daily: '每日练习',
  endless: '无限刷题',
}

export function PracticePage() {
  const { sessionId } = useParams<{ sessionId: string }>()
  const navigate = useNavigate()
  const { user } = useAuth()
  const submitMode = user?.prefs.submitMode === 'auto' ? 'auto' : 'space'
  const [selected, setSelected] = useState<string[]>([])
  const [feedback, setFeedback] = useState<AnswerFeedback | null>(null)
  const [advancing, setAdvancing] = useState(false)
  const startedAt = useRef(0)

  const nextQuery = useQuery({
    queryKey: ['next-card', sessionId],
    queryFn: () => api.nextCard(sessionId ?? ''),
    enabled: Boolean(sessionId),
    retry: false,
    refetchOnWindowFocus: false,
    refetchOnReconnect: false,
  })

  const card = nextQuery.data?.card ?? null
  const session = nextQuery.data?.session

  const submitMutation = useMutation({
    mutationFn: (body: SubmitAnswerBody) => api.submitAnswer(sessionId ?? '', body),
    onSuccess: (result) => {
      setFeedback(result)
    },
  })

  useEffect(() => {
    startedAt.current = performance.now()
  }, [card?.questionId])

  const submitWithKeys = useCallback(
    (keys: string[]) => {
      if (!card || feedback || keys.length === 0 || submitMutation.isPending) return
      const elapsedMs = Math.max(0, Math.round(performance.now() - startedAt.current))
      submitMutation.mutate({
        questionId: card.questionId,
        selectedKeys: keys,
        elapsedMs,
      })
    },
    [card, feedback, submitMutation],
  )

  const selectOption = useCallback(
    (key: string) => {
      if (!card || feedback || submitMutation.isPending) return
      if (card.type === 'multiple_choice') {
        setSelected((current) =>
          current.includes(key)
            ? current.filter((item) => item !== key)
            : [...current, key],
        )
        return
      }

      setSelected([key])
      if (submitMode === 'auto') {
        submitWithKeys([key])
      }
    },
    [card, feedback, submitMutation, submitMode, submitWithKeys],
  )

  const handleSubmit = useCallback(
    () => submitWithKeys(selected),
    [selected, submitWithKeys],
  )

  const advancingRef = useRef(false)

  async function handleNext() {
    if (advancingRef.current) return
    advancingRef.current = true
    setAdvancing(true)
    try {
      if (feedback?.finished && sessionId) {
        await api.finishSession(sessionId).catch(() => undefined)
      }
      const result = await nextQuery.refetch()
      if (!result.isError && result.data) {
        setSelected([])
        setFeedback(null)
        submitMutation.reset()
        startedAt.current = performance.now()
      }
    } finally {
      advancingRef.current = false
      setAdvancing(false)
    }
  }

  const handleNextRef = useRef(handleNext)
  handleNextRef.current = handleNext

  useEffect(() => {
    function onKeyDown(event: KeyboardEvent) {
      const target = event.target as HTMLElement | null
      if (target?.closest('input, textarea, select, [contenteditable="true"]')) return
      if (event.metaKey || event.ctrlKey || event.altKey) return
      if (!card) return

      if (!feedback) {
        const index = Number(event.key)
        if (Number.isInteger(index) && index >= 1 && index <= 9) {
          const option = card.options[index - 1]
          if (!option) return
          event.preventDefault()
          selectOption(option.key)
          return
        }

        if (event.key === 'Enter' && !target?.closest('button, a')) {
          event.preventDefault()
          handleSubmit()
          return
        }

        if (event.code === 'Space' || event.key === ' ') {
          if (target?.closest('button, a')) return
          event.preventDefault()
          if (submitMode === 'space') {
            handleSubmit()
          }
        }
        return
      }

      if (event.code === 'Space' || event.key === ' ') {
        if (target?.closest('button, a')) return
        event.preventDefault()
        void handleNextRef.current()
      }
    }

    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [card, feedback, selectOption, handleSubmit, submitMode])

  const retryMutation = useMutation({
    mutationFn: (input: { bankId: string; mode: string }) => api.startSession(input.bankId, input.mode),
    onSuccess: (newSession) => navigate(`/practice/${newSession.id}`),
  })

  if (!sessionId) {
    return <ErrorNote message="找不到这次练习。" />
  }

  if (nextQuery.isPending) {
    return <Loading full label="正在准备题目…" />
  }

  if (nextQuery.isError && !nextQuery.data) {
    return (
      <div>
        <Link to="/" className="back-link">
          ← 返回题库
        </Link>
        <ErrorNote message={errorMessage(nextQuery.error)} />
      </div>
    )
  }

  if (!nextQuery.data) {
    return <Loading full label="正在准备题目…" />
  }

  const stats = feedback?.stats ?? nextQuery.data.session.stats
  const total = nextQuery.data.session.total
  const activeSession = feedback?.session ?? nextQuery.data.session
  const isEndless = activeSession.mode === 'endless'

  if (!card) {
    const answered = activeSession.answered || stats.correct + stats.wrong
    const rate = answered > 0 ? Math.round((stats.correct / answered) * 100) : 0
    const modeLabel = MODE_LABELS[activeSession.mode] ?? '练习'
    return (
      <div className="result-wrap">
        <div className="card result-card">
          <div className="result-head">
            <p className="eyebrow">{isEndless ? '全部练熟' : '练习完成'}</p>
            <span className="badge badge-quiet result-mode">{modeLabel}</span>
          </div>
          <p className="result-score">
            {isEndless ? activeSession.mastered : stats.correct}
            <span className="result-total"> / {activeSession.total}</span>
          </p>
          <p className="result-caption">{isEndless ? '已掌握题目' : '答对题数'}</p>
          <p className="muted">
            {isEndless
              ? `本轮作答 ${answered} 次 · 答对 ${stats.correct} 次 · 答错 ${stats.wrong} 次`
              : `答对 ${stats.correct} 题 · 答错 ${stats.wrong} 题`}
          </p>
          <div className="result-rate">
            <strong>{rate}%</strong>
            <span className="muted">正确率</span>
          </div>
          <div className="result-actions">
            {isEndless ? null : (
              <button
                type="button"
                className="btn btn-primary"
                onClick={() =>
                  retryMutation.mutate({ bankId: activeSession.bankId, mode: activeSession.mode })
                }
                disabled={retryMutation.isPending}
              >
                {retryMutation.isPending ? '正在开始…' : '再练一次'}
              </button>
            )}
            <Link
              to={`/banks/${activeSession.bankId}`}
              className={isEndless ? 'btn btn-primary' : 'btn btn-ghost'}
            >
              返回题库
            </Link>
          </div>
          {retryMutation.isError ? (
            <ErrorNote message={errorMessage(retryMutation.error)} />
          ) : null}
        </div>
      </div>
    )
  }

  const labelByKey = new Map(card.options.map((option) => [option.key, option.label]))
  const displayOptions = feedback
    ? feedback.options.map((option) => ({
        key: option.key,
        label: labelByKey.get(option.key) ?? option.key,
        text: option.text,
        state: option.state,
      }))
    : card.options.map((option) => ({ ...option, state: '' }))

  const answeredIndex = feedback ? feedback.index : nextQuery.data.index
  const position = Math.max(1, Math.min(total, answeredIndex))
  const progressCount = feedback ? feedback.index : Math.max(0, nextQuery.data.index - 1)
  const progress = isEndless
    ? total > 0
      ? Math.round((activeSession.mastered / total) * 100)
      : 0
    : total > 0
      ? Math.round((Math.min(progressCount, total) / total) * 100)
      : 0
  const attemptNumber = activeSession.answered + (feedback ? 0 : 1)

  return (
    <div className="practice-page">
      <div className="practice-head">
        <Link to={`/banks/${session?.bankId ?? nextQuery.data.session.bankId}`} className="back-link">
          ← 退出练习
        </Link>
        <span className="practice-meta">
          {isEndless ? <span className="badge badge-quiet">无限刷题</span> : null}
          <span className="muted">{nextQuery.data.session.bankName}</span>
        </span>
      </div>

      <div className={progress >= 100 ? 'practice-progress is-complete' : 'practice-progress'}>
        <div className="practice-progress-row">
          <span className="progress-label">
            {isEndless ? `已掌握 ${activeSession.mastered} / ${total}` : `第 ${position} / ${total} 题`}
          </span>
          <span className="progress-stats muted">
            {isEndless
              ? `第 ${attemptNumber} 次作答 · 答对 ${stats.correct}`
              : `答对 ${stats.correct} · 答错 ${stats.wrong}`}
          </span>
        </div>
        <div
          className="progress-track"
          role="progressbar"
          aria-label={isEndless ? '已掌握进度' : '练习进度'}
          aria-valuemin={0}
          aria-valuemax={100}
          aria-valuenow={progress}
        >
          <div
            className={
              progress <= 0
                ? 'progress-fill is-empty'
                : progress >= 100
                  ? 'progress-fill is-full'
                  : 'progress-fill'
            }
            style={{ width: `${progress}%` }}
          />
        </div>
      </div>

      <article className="card question-card">
        <div className="question-card-head">
          <span className="badge badge-quiet">{typeLabel(card.type)}</span>
          <span className="muted">
            {feedback
              ? '本题已提交'
              : isEndless
                ? '答对两次就移出本轮'
                : submitMode === 'auto' && card.type === 'single_choice'
                  ? '选中后自动提交，答案会立即批改'
                  : '选择后提交，答案会立即批改'}
          </span>
        </div>

        <div className="question-stem">
          <MarkdownView source={card.stem} />
        </div>

        <div className="options" role="group" aria-label="选项">
          {displayOptions.map((option) => {
            const isSelected = selected.includes(option.key)
            const classes = [
              'option',
              isSelected ? 'selected' : '',
              option.state,
            ]
              .filter(Boolean)
              .join(' ')

            return (
              <div
                key={option.key}
                role="button"
                tabIndex={feedback ? -1 : 0}
                aria-pressed={isSelected}
                aria-disabled={feedback ? true : undefined}
                className={classes}
                onClick={() => selectOption(option.key)}
              >
                <span className="option-letter">{option.label}</span>
                <span className="option-text">
                  <MarkdownView source={option.text} />
                </span>
                {option.state === 'hit' || option.state === 'wrong' ? (
                  <span
                    className={option.state === 'hit' ? 'option-state is-hit' : 'option-state is-wrong'}
                  >
                    <Icon name={option.state === 'hit' ? 'check' : 'x'} size={18} />
                    <span className="option-state-text">
                      {option.state === 'hit' ? '正确' : '错误'}
                    </span>
                  </span>
                ) : option.state === 'missed' ? (
                  <span className="option-state is-missed">
                    <span className="option-flag">漏选</span>
                  </span>
                ) : card.type === 'multiple_choice' && !feedback ? (
                  <span
                    className={isSelected ? 'option-check checked' : 'option-check'}
                    aria-hidden="true"
                  />
                ) : null}
              </div>
            )
          })}
        </div>
      </article>

      <p className="practice-live" role="status" aria-live="polite">
        {feedback
          ? `${feedback.correct ? '回答正确' : '回答错误'}，得分 ${Math.round(feedback.score * 100)}%`
          : ''}
      </p>

      {feedback ? (
        <section
          className={feedback.correct ? 'card feedback-card is-correct' : 'card feedback-card is-wrong'}
        >
          <div className="feedback-head">
            <strong className={feedback.correct ? 'feedback-title correct' : 'feedback-title wrong'}>
              <Icon
                name={feedback.correct ? 'check' : 'x'}
                size={18}
                className="feedback-icon"
              />
              {feedback.correct ? '回答正确' : '回答错误'}
            </strong>
            <span className="feedback-score muted">得分 {Math.round(feedback.score * 100)}%</span>
          </div>
          <div className="explanation">
            {feedback.explanation?.trim() ? (
              <MarkdownView source={feedback.explanation} />
            ) : (
              <p className="muted">这道题还没有解析。</p>
            )}
          </div>
        </section>
      ) : null}

      {submitMutation.isError || nextQuery.isError ? (
        <div className="practice-errors">
          {submitMutation.isError ? <ErrorNote message={errorMessage(submitMutation.error)} /> : null}
          {nextQuery.isError ? <ErrorNote message={errorMessage(nextQuery.error)} /> : null}
        </div>
      ) : null}

      <div className="practice-actions">
        <p className="kbd-hint">
          {feedback
            ? 'Space 下一题'
            : submitMode === 'auto' && card.type === 'single_choice'
              ? '按 1–9 或点选项直接提交'
              : submitMode === 'space'
                ? '按 1–9 选择选项 · Space 提交'
                : '按 1–9 选择选项 · Enter 提交'}
        </p>
        {feedback ? (
          <button
            type="button"
            className="btn btn-primary"
            onClick={() => void handleNext()}
            disabled={advancing}
          >
            {advancing
              ? '正在载入…'
              : feedback.finished
                ? isEndless
                  ? '完成'
                  : '查看结果'
                : '下一题'}
          </button>
        ) : (
          <button
            type="button"
            className="btn btn-primary"
            onClick={handleSubmit}
            disabled={selected.length === 0 || submitMutation.isPending}
          >
            {submitMutation.isPending ? '提交中…' : '提交答案'}
          </button>
        )}
      </div>
    </div>
  )
}
