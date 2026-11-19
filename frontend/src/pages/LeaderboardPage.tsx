import { useMemo, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { api, errorMessage } from '../api'
import type { LeaderboardEntry } from '../api'
import { useAuth } from '../auth-context'
import { Avatar } from '../components/Avatar'
import { EmptyState, ErrorNote, Loading } from '../components/Ui'

type SortKey = 'composite' | 'accuracy' | 'contributions' | 'volume'

const SORTS: { key: SortKey; label: string }[] = [
  { key: 'composite', label: '综合' },
  { key: 'accuracy', label: '答对率' },
  { key: 'contributions', label: '资料贡献' },
  { key: 'volume', label: '刷题量' },
]

function byName(a: LeaderboardEntry, b: LeaderboardEntry): number {
  return a.displayName.localeCompare(b.displayName, 'zh-CN')
}

/** 答对率：没作答的人排最后，其余按答对率、作答数降序。 */
function compareAccuracy(a: LeaderboardEntry, b: LeaderboardEntry): number {
  const answeredA = a.answered > 0
  const answeredB = b.answered > 0
  if (answeredA !== answeredB) return answeredA ? -1 : 1
  if (b.accuracy !== a.accuracy) return b.accuracy - a.accuracy
  return b.answered - a.answered
}

function sortEntries(entries: LeaderboardEntry[], sortKey: SortKey): LeaderboardEntry[] {
  const sorted = [...entries]
  sorted.sort((a, b) => {
    if (sortKey === 'accuracy') {
      return compareAccuracy(a, b) || byName(a, b)
    }
    if (sortKey === 'contributions') {
      return (
        b.contributions - a.contributions ||
        b.questionsCreated - a.questionsCreated ||
        b.answered - a.answered ||
        byName(a, b)
      )
    }
    if (sortKey === 'volume') {
      return (
        b.answered - a.answered ||
        b.correct - a.correct ||
        compareAccuracy(a, b) ||
        byName(a, b)
      )
    }
    return b.compositeScore - a.compositeScore || compareAccuracy(a, b) || byName(a, b)
  })
  return sorted
}

function formatScore(value: number): string {
  return (Math.round(value * 10) / 10).toFixed(1)
}

function formatPercent(entry: LeaderboardEntry): string {
  if (entry.answered <= 0) return '—'
  const percent = Math.round(entry.accuracy * 1000) / 10
  return `${Number.isInteger(percent) ? percent.toFixed(0) : percent.toFixed(1)}%`
}

function breakdownText(entry: LeaderboardEntry): string {
  return `题 ${entry.questionsCreated} · 库 ${entry.banksOwned} · 讲义 ${entry.notesOwned}`
}

export function LeaderboardPage() {
  const { user } = useAuth()
  const [sortKey, setSortKey] = useState<SortKey>('composite')
  const leaderboardQuery = useQuery({
    queryKey: ['leaderboard'],
    queryFn: api.leaderboard,
    staleTime: 5 * 60_000,
  })

  const data = leaderboardQuery.data
  const entries = useMemo(() => sortEntries(data?.entries ?? [], sortKey), [data, sortKey])

  const isSelf = (entry: LeaderboardEntry) => entry.userId === user?.id

  return (
    <div>
      <header className="page-header">
        <div className="page-heading">
          <p className="eyebrow">班级</p>
          <h1>排行榜</h1>
        </div>
      </header>

      {leaderboardQuery.isPending ? <Loading /> : null}
      {leaderboardQuery.isError ? (
        <ErrorNote message={errorMessage(leaderboardQuery.error)} />
      ) : null}

      {data ? (
        data.entries.length === 0 ? (
          <EmptyState
            title="还没有人上榜"
            hint="先答过 50 题才能上榜。"
          />
        ) : (
          <>
            <div className="lb-controls">
              <div className="lb-sort" role="group" aria-label="排序依据">
                {SORTS.map((option) => (
                  <button
                    key={option.key}
                    type="button"
                    className={sortKey === option.key ? 'chip active' : 'chip'}
                    aria-pressed={sortKey === option.key}
                    onClick={() => setSortKey(option.key)}
                  >
                    {option.label}
                  </button>
                ))}
              </div>
              <span className="lb-count muted">{entries.length} 人</span>
            </div>

            <div className="lb-table-wrap">
              <table className="lb-table">
                <caption className="lb-caption">
                  班级排行榜：排名、同学、综合分、答对率、资料贡献与刷题数
                </caption>
                <thead>
                  <tr>
                    <th scope="col" className="lb-rank">
                      排名
                    </th>
                    <th scope="col">同学</th>
                    <th scope="col" className="lb-num">
                      综合分
                    </th>
                    <th scope="col" className="lb-num">
                      答对率
                    </th>
                    <th scope="col" className="lb-num">
                      资料贡献
                    </th>
                    <th scope="col" className="lb-num">
                      刷题数
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {entries.map((entry, index) => (
                    <tr key={entry.userId} data-self={isSelf(entry) ? 'true' : 'false'}>
                      <td className="lb-rank">{index + 1}</td>
                      <td>
                        <div className="lb-person">
                          <Avatar name={entry.displayName} src={entry.avatarUrl} size="sm" />
                          <span className="lb-name">{entry.displayName}</span>
                          {isSelf(entry) ? <span className="lb-you">你</span> : null}
                        </div>
                      </td>
                      <td className="lb-num lb-score">{formatScore(entry.compositeScore)}</td>
                      <td className="lb-num">{formatPercent(entry)}</td>
                      <td className="lb-num">
                        <span className="lb-contrib">
                          <span className="lb-contrib-main">{entry.contributions}</span>
                          <span
                            className="lb-contrib-break"
                            title={`题 ${entry.questionsCreated} · 库 ${entry.banksOwned} · 讲义 ${entry.notesOwned}`}
                          >
                            {breakdownText(entry)}
                          </span>
                        </span>
                      </td>
                      <td className="lb-num">{entry.answered}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            <ul className="lb-cards">
              {entries.map((entry, index) => (
                <li
                  key={entry.userId}
                  className="lb-card"
                  data-self={isSelf(entry) ? 'true' : 'false'}
                >
                  <div className="lb-card-head">
                    <span className="lb-card-rank">{index + 1}</span>
                    <Avatar name={entry.displayName} src={entry.avatarUrl} size="sm" />
                    <span className="lb-name">{entry.displayName}</span>
                    {isSelf(entry) ? <span className="lb-you">你</span> : null}
                    <span className="lb-card-score">
                      <span className="lb-metric-label">综合分</span>
                      <strong>{formatScore(entry.compositeScore)}</strong>
                    </span>
                  </div>
                  <div className="lb-card-metrics">
                    <span className="lb-metric">
                      <span className="lb-metric-label">答对率</span>
                      <span className="lb-metric-value">{formatPercent(entry)}</span>
                    </span>
                    <span className="lb-metric">
                      <span className="lb-metric-label">资料贡献</span>
                      <span className="lb-metric-value">{entry.contributions}</span>
                    </span>
                    <span className="lb-metric">
                      <span className="lb-metric-label">刷题数</span>
                      <span className="lb-metric-value">{entry.answered}</span>
                    </span>
                  </div>
                  <p className="lb-card-note">
                    {entry.contributions > 0 ? breakdownText(entry) : '暂无资料贡献'}
                  </p>
                </li>
              ))}
            </ul>

            <p className="lb-updated muted">先答过 50 题才能上榜</p>
          </>
        )
      ) : null}
    </div>
  )
}
