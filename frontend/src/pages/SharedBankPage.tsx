import { useMemo, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { useMutation, useQuery } from '@tanstack/react-query'
import { api, errorMessage } from '../api'
import type { GuestAnswer } from '../api'
import { Icon } from '../components/Icon'
import { MarkdownView } from '../components/MarkdownView'
import { PublicShell } from '../components/PublicShell'
import { EmptyState, ErrorNote, Loading } from '../components/Ui'
import { typeLabel } from '../format'

/**
 * 游客练习：题目和判分都走公开接口，不在服务端写任何记录。
 * 登录后从题库页进入的是正常会话，进度会保存。
 */
export function SharedBankPage() {
  const { id } = useParams<{ id: string }>()
  const bankId = id ?? ''

  const bankQuery = useQuery({
    queryKey: ['public-bank', bankId],
    queryFn: () => api.publicBank(bankId),
    enabled: Boolean(bankId),
    retry: false,
  })
  const questionsQuery = useQuery({
    queryKey: ['public-questions', bankId],
    queryFn: () => api.publicBankQuestions(bankId),
    enabled: Boolean(bankId),
    retry: false,
  })

  const [started, setStarted] = useState(false)
  // 每轮用一个随机 attempt 换卡片 seed，重练同一题时选项与干扰项都会变。
  const [round, setRound] = useState(() => Math.floor(Math.random() * 100_000))
  const [index, setIndex] = useState(0)
  const [selected, setSelected] = useState<string[]>([])
  const [result, setResult] = useState<GuestAnswer | null>(null)
  const [correct, setCorrect] = useState(0)
  const [finished, setFinished] = useState(false)

  const questions = questionsQuery.data ?? []
  const total = questions.length
  const current = questions[index]

  const cardQuery = useQuery({
    queryKey: ['guest-card', bankId, current?.id, round],
    queryFn: () => api.publicCard(bankId, current?.id ?? '', round),
    enabled: started && Boolean(current?.id),
    staleTime: Infinity,
  })

  const answerMutation = useMutation({
    mutationFn: () => api.publicAnswer(bankId, current?.id ?? '', selected),
    onSuccess: (answer) => {
      setResult(answer)
      if (answer.correct) setCorrect((value) => value + 1)
    },
  })

  const card = cardQuery.data
  const optionStates = useMemo(() => {
    const map = new Map<string, 'hit' | 'wrong' | 'missed' | ''>()
    if (!result) return map
    for (const key of result.correctKeys) {
      map.set(key, selected.includes(key) ? 'hit' : 'missed')
    }
    for (const key of selected) {
      if (!result.correctKeys.includes(key)) map.set(key, 'wrong')
    }
    return map
  }, [result, selected])

  function toggle(key: string) {
    if (result) return
    if (card?.type === 'multiple_choice') {
      setSelected((values) =>
        values.includes(key) ? values.filter((value) => value !== key) : [...values, key],
      )
      return
    }
    setSelected([key])
  }

  function next() {
    if (index + 1 >= total) {
      setFinished(true)
      return
    }
    setIndex((value) => value + 1)
    setSelected([])
    setResult(null)
    answerMutation.reset()
  }

  function restart() {
    setIndex(0)
    setSelected([])
    setResult(null)
    setCorrect(0)
    setFinished(false)
    setStarted(true)
    setRound(Math.floor(Math.random() * 100_000))
  }

  const bank = bankQuery.data
  const loading = bankQuery.isPending || (started && cardQuery.isPending)

  return (
    <PublicShell>
      {loading ? <Loading label="正在载入…" /> : null}

      {bankQuery.isError ? (
        <EmptyState title="题库不可用" hint="可能已经改为私有，或者被删除了。" />
      ) : null}

      {bank && !started ? (
        <div className="shared-intro">
          <p className="eyebrow">公开题库 · 游客模式</p>
          <h1>{bank.name}</h1>
          {bank.description ? <p className="muted">{bank.description}</p> : null}
          <div className="shared-meta">
            <span>{bank.questionCount} 道题</span>
            <span>·</span>
            <span>进度不会保存</span>
          </div>
          <div className="shared-actions">
            <button
              type="button"
              className="btn btn-primary"
              onClick={() => setStarted(true)}
              disabled={questionsQuery.isPending || total === 0}
            >
              <Icon name="play" size={16} />
              {questionsQuery.isPending ? '载入题目…' : total === 0 ? '还没有题目' : '开始练习'}
            </button>
            <Link to="/login" className="btn btn-ghost">
              登录后记录进度
            </Link>
          </div>
          {questionsQuery.isError ? (
            <ErrorNote message={errorMessage(questionsQuery.error)} />
          ) : null}
        </div>
      ) : null}

      {bank && started && finished ? (
        <div className="result-wrap">
          <div className="card result-card">
            <div className="result-head">
              <p className="eyebrow">练习完成</p>
              <span className="badge badge-quiet">游客模式</span>
            </div>
            <p className="result-score">
              <strong>{correct}</strong>
              <span className="result-total"> / {total}</span>
            </p>
            <p className="result-caption">答对题数</p>
            <p className="muted">登录之后练习会记录进度和记忆调度，下次可以接着刷。</p>
            <div className="result-actions">
              <button type="button" className="btn btn-primary" onClick={restart}>
                再练一次
              </button>
              <Link to="/login" className="btn btn-ghost">
                去登录
              </Link>
            </div>
          </div>
        </div>
      ) : null}

      {bank && started && !finished ? (
        <div className="practice-page">
          <div className="practice-head">
            <span className="back-link" aria-hidden="true">
              {bank.name}
            </span>
            <span className="practice-meta">
              <span className="badge badge-quiet">游客模式</span>
              <span className="muted">进度不会保存</span>
            </span>
          </div>

          <div className="practice-progress">
            <div className="practice-progress-row">
              <span className="progress-label">
                第 {Math.min(index + 1, total)} / {total} 题
              </span>
              <span className="progress-stats muted">答对 {correct}</span>
            </div>
            <div
              className="progress-track"
              role="progressbar"
              aria-label="练习进度"
              aria-valuemin={0}
              aria-valuemax={100}
              aria-valuenow={total === 0 ? 0 : Math.round((index / total) * 100)}
            >
              <div
                className="progress-fill"
                style={{ width: `${total === 0 ? 0 : Math.round((index / total) * 100)}%` }}
              />
            </div>
          </div>

          {cardQuery.isError ? <ErrorNote message={errorMessage(cardQuery.error)} /> : null}

          {card ? (
            <article className="card question-card">
              <div className="question-card-head">
                <span className="badge badge-quiet">{typeLabel(card.type)}</span>
                <span className="muted">
                  {result
                    ? result.correct
                      ? '回答正确'
                      : '回答错误'
                    : '选择后提交，答案会立即批改'}
                </span>
              </div>

              <div className="question-stem">
                <MarkdownView source={card.stem} />
              </div>

              <div className="options" role="group" aria-label="选项">
                {card.options.map((option) => {
                  const state = optionStates.get(option.key) ?? ''
                  const isSelected = selected.includes(option.key)
                  const classes = ['option', isSelected ? 'selected' : '', state]
                    .filter(Boolean)
                    .join(' ')

                  return (
                    <div
                      key={option.key}
                      role="button"
                      tabIndex={result ? -1 : 0}
                      aria-pressed={isSelected}
                      aria-disabled={result ? true : undefined}
                      className={classes}
                      onClick={() => toggle(option.key)}
                    >
                      <span className="option-letter">{option.label}</span>
                      <span className="option-text">
                        <MarkdownView source={option.text} />
                      </span>
                      {state === 'hit' || state === 'wrong' ? (
                        <span className={state === 'hit' ? 'option-state is-hit' : 'option-state is-wrong'}>
                          <Icon name={state === 'hit' ? 'check' : 'x'} size={18} />
                          <span className="option-state-text">{state === 'hit' ? '正确' : '错误'}</span>
                        </span>
                      ) : state === 'missed' ? (
                        <span className="option-state is-missed">
                          <span className="option-flag">漏选</span>
                        </span>
                      ) : card.type === 'multiple_choice' && !result ? (
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
          ) : null}

          {result ? (
            <section
              className={
                result.correct ? 'card feedback-card is-correct' : 'card feedback-card is-wrong'
              }
            >
              <div className="feedback-head">
                <strong className={result.correct ? 'feedback-title correct' : 'feedback-title wrong'}>
                  <Icon
                    name={result.correct ? 'check' : 'x'}
                    size={18}
                    className="feedback-icon"
                  />
                  {result.correct ? '回答正确' : '回答错误'}
                </strong>
              </div>
              <div className="explanation">
                {result.explanation?.trim() ? (
                  <MarkdownView source={result.explanation} />
                ) : (
                  <p className="muted">这道题还没有解析。</p>
                )}
              </div>
            </section>
          ) : null}

          {answerMutation.isError ? <ErrorNote message={errorMessage(answerMutation.error)} /> : null}

          <div className="practice-actions">
            <p className="kbd-hint">
              {result ? '看解析，然后继续' : '可以多选，选好后提交'}
            </p>
            {result ? (
              <button type="button" className="btn btn-primary" onClick={next}>
                {index + 1 >= total ? '查看结果' : '下一题'}
              </button>
            ) : (
              <button
                type="button"
                className="btn btn-primary"
                onClick={() => answerMutation.mutate()}
                disabled={selected.length === 0 || answerMutation.isPending}
              >
                {answerMutation.isPending ? '提交中…' : '提交答案'}
              </button>
            )}
          </div>
        </div>
      ) : null}
    </PublicShell>
  )
}
