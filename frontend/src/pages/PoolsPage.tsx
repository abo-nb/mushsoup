import { useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, errorMessage } from '../api'
import { EmptyState, ErrorNote, Loading } from '../components/Ui'

export function PoolsPage() {
  const { id } = useParams<{ id: string }>()
  const bankId = id ?? ''
  const queryClient = useQueryClient()

  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [poolName, setPoolName] = useState('')
  const [poolDescription, setPoolDescription] = useState('')
  const [entryText, setEntryText] = useState('')

  const accessQuery = useQuery({
    queryKey: ['bank', bankId],
    queryFn: () => api.bank(bankId),
    enabled: Boolean(bankId),
  })

  const poolsQuery = useQuery({
    queryKey: ['pools', bankId],
    queryFn: () => api.pools(bankId),
    enabled: Boolean(bankId) && Boolean(accessQuery.data?.canView),
  })

  const detailQuery = useQuery({
    queryKey: ['pool', selectedId],
    queryFn: () => api.pool(selectedId ?? ''),
    enabled: Boolean(selectedId),
  })

  const healthQuery = useQuery({
    queryKey: ['pool-health', selectedId],
    queryFn: () => api.poolHealth(selectedId ?? ''),
    enabled: Boolean(selectedId),
  })

  function refresh(poolId?: string) {
    void queryClient.invalidateQueries({ queryKey: ['pools', bankId] })
    if (poolId) {
      void queryClient.invalidateQueries({ queryKey: ['pool', poolId] })
      void queryClient.invalidateQueries({ queryKey: ['pool-health', poolId] })
    }
  }

  const createPoolMutation = useMutation({
    mutationFn: () =>
      api.createPool(bankId, {
        name: poolName.trim(),
        description: poolDescription.trim() || undefined,
      }),
    onSuccess: (pool) => {
      setPoolName('')
      setPoolDescription('')
      setSelectedId(pool.id)
      refresh()
    },
  })

  const addEntryMutation = useMutation({
    mutationFn: () =>
      api.addPoolEntry(selectedId ?? '', {
        text: entryText.trim(),
      }),
    onSuccess: () => {
      setEntryText('')
      refresh(selectedId ?? undefined)
    },
  })

  const toggleEntryMutation = useMutation({
    mutationFn: (input: { entryId: string; disabled: boolean }) =>
      api.updatePoolEntry(selectedId ?? '', input.entryId, { disabled: input.disabled }),
    onSuccess: () => refresh(selectedId ?? undefined),
  })

  const deleteEntryMutation = useMutation({
    mutationFn: (entryId: string) => api.deletePoolEntry(selectedId ?? '', entryId),
    onSuccess: () => refresh(selectedId ?? undefined),
  })

  const deletePoolMutation = useMutation({
    mutationFn: (poolId: string) => api.deletePool(poolId),
    onSuccess: () => {
      setSelectedId(null)
      refresh()
    },
  })

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
  const pools = poolsQuery.data ?? []
  const detail = detailQuery.data
  const health = healthQuery.data

  return (
    <div>
      <Link to={`/banks/${bankId}`} className="back-link">
        ← 返回 {bank.name}
      </Link>

      <header className="page-header">
        <div className="page-heading">
          <p className="eyebrow">{bank.name}</p>
          <h1>选项池</h1>
          <p className="page-sub">
            把一组同类干扰项放在这里，出题时随机抽取。同一道题练多次也不会总是那几个选项。
          </p>
        </div>
      </header>

      {access.canEdit ? (
        <form
          className="card card-pad create-bank pool-create"
          onSubmit={(event) => {
            event.preventDefault()
            if (poolName.trim()) createPoolMutation.mutate()
          }}
        >
          <div className="section-head">
            <div>
              <h2 className="section-title">新建选项池</h2>
              <p className="muted">一个池子放一组同类干扰项，命名后即可往里添加选项。</p>
            </div>
          </div>
          <div className="form-row">
            <div className="field">
              <label htmlFor="pool-name">名称</label>
              <input
                id="pool-name"
                className="input"
                value={poolName}
                maxLength={60}
                onChange={(event) => setPoolName(event.target.value)}
                placeholder="例如：细胞器名称"
              />
            </div>
            <div className="field">
              <label htmlFor="pool-description">说明（选填）</label>
              <input
                id="pool-description"
                className="input"
                value={poolDescription}
                maxLength={200}
                onChange={(event) => setPoolDescription(event.target.value)}
                placeholder="这组选项用在哪些题上"
              />
            </div>
          </div>
          {createPoolMutation.isError ? (
            <ErrorNote message={errorMessage(createPoolMutation.error)} />
          ) : null}
          <div className="form-actions">
            <button
              type="submit"
              className="btn btn-primary"
              disabled={!poolName.trim() || createPoolMutation.isPending}
            >
              {createPoolMutation.isPending ? '创建中…' : '创建选项池'}
            </button>
          </div>
        </form>
      ) : null}

      {poolsQuery.isPending ? <Loading /> : null}
      {poolsQuery.isError ? <ErrorNote message={errorMessage(poolsQuery.error)} /> : null}

      {poolsQuery.data && pools.length === 0 ? (
        <EmptyState
          title="还没有选项池"
          hint="建一个池子，把常用来做干扰项的词语放进去，出题时就能复用。"
        >
          {access.canEdit ? (
            <a className="btn btn-ghost btn-small empty-action" href="#pool-name">
              去创建第一个选项池
            </a>
          ) : null}
        </EmptyState>
      ) : null}

      {pools.length > 0 ? (
        <div className="pool-layout">
          <aside className="card list-card pool-list-card" aria-label="选项池列表">
            <div className="list-toolbar">
              <strong>全部选项池</strong>
              <span className="muted">{pools.length} 个</span>
            </div>
            <div className="pool-list">
              {pools.map((pool) => (
                <button
                  key={pool.id}
                  type="button"
                  className={pool.id === selectedId ? 'pool-item active' : 'pool-item'}
                  onClick={() => setSelectedId(pool.id)}
                  aria-pressed={pool.id === selectedId}
                >
                  <span className="pool-item-name">{pool.name}</span>
                  <span className="pool-item-meta">
                    <span
                      className={
                        pool.availableCount === 0 ? 'pool-item-count is-empty' : 'pool-item-count'
                      }
                    >
                      {pool.availableCount} / {pool.entryCount} 可用
                    </span>
                    {pool.entryCount === 0 ? (
                      <span className="pool-item-flag">空池</span>
                    ) : pool.availableCount === 0 ? (
                      <span className="pool-item-flag is-warn">全部停用</span>
                    ) : null}
                  </span>
                </button>
              ))}
            </div>
          </aside>

          <section className="card card-pad pool-detail">
            {!selectedId ? (
              <div className="pool-detail-empty">
                <p className="muted">从左边选一个选项池查看和编辑。</p>
                <p className="muted">还没有池子？在上面填写名称即可新建。</p>
              </div>
            ) : detailQuery.isPending ? (
              <Loading />
            ) : detailQuery.isError ? (
              <ErrorNote message={errorMessage(detailQuery.error)} />
            ) : detail ? (
              <>
                <div className="section-head pool-detail-head">
                  <div>
                    <h2 className="section-title">{detail.pool.name}</h2>
                    {detail.pool.description ? (
                      <p className="muted">{detail.pool.description}</p>
                    ) : null}
                  </div>
                  {access.canEdit ? (
                    <button
                      type="button"
                      className="btn btn-small btn-danger-ghost"
                      onClick={() => {
                        if (window.confirm('删除这个选项池？被题目引用时无法删除。')) {
                          deletePoolMutation.mutate(detail.pool.id)
                        }
                      }}
                      disabled={deletePoolMutation.isPending}
                    >
                      删除池子
                    </button>
                  ) : null}
                </div>

                {deletePoolMutation.isError ? (
                  <ErrorNote message={errorMessage(deletePoolMutation.error)} />
                ) : null}

                {health ? (
                  <div className={health.warnings.length > 0 ? 'pool-health has-warning' : 'pool-health'}>
                    <div className="pool-health-stats">
                      <span className="pool-health-line">
                        被 <strong>{health.referencingQuestions}</strong> 道题引用
                        {health.requiredEntries > 0
                          ? `，至少需要 ${health.requiredEntries} 条可用`
                          : ''}
                      </span>
                      <span className="pool-health-line">
                        可用 <strong>{health.availableCount}</strong> / 共 {health.entryCount} 条
                      </span>
                    </div>
                    {health.warnings.length > 0 ? (
                      <ul className="pool-health-warnings">
                        {health.warnings.map((warning) => (
                          <li key={warning}>{warning}</li>
                        ))}
                      </ul>
                    ) : null}
                  </div>
                ) : null}

                {access.canEdit ? (
                  <form
                    className="entry-add"
                    onSubmit={(event) => {
                      event.preventDefault()
                      if (entryText.trim()) addEntryMutation.mutate()
                    }}
                  >
                    <input
                      className="input"
                      value={entryText}
                      maxLength={200}
                      onChange={(event) => setEntryText(event.target.value)}
                      placeholder="加一个选项，例如：高尔基体"
                      aria-label="新选项内容"
                    />
                    <button
                      type="submit"
                      className="btn btn-primary"
                      disabled={!entryText.trim() || addEntryMutation.isPending}
                    >
                      添加
                    </button>
                  </form>
                ) : null}

                {addEntryMutation.isError ? (
                  <ErrorNote message={errorMessage(addEntryMutation.error)} />
                ) : null}
                {toggleEntryMutation.isError ? (
                  <ErrorNote message={errorMessage(toggleEntryMutation.error)} />
                ) : null}
                {deleteEntryMutation.isError ? (
                  <ErrorNote message={errorMessage(deleteEntryMutation.error)} />
                ) : null}

                <div className="entry-list-head">
                  <span>选项</span>
                  <span className="muted">{detail.entries.length} 条</span>
                </div>

                <div className="entry-list">
                  {detail.entries.length === 0 ? (
                    <p className="muted entry-empty">还没有选项，先加几条。</p>
                  ) : null}
                  {detail.entries.map((entry) => (
                    <div
                      key={entry.id}
                      className={entry.disabled ? 'entry-row disabled' : 'entry-row'}
                    >
                      <span className="entry-text">{entry.text}</span>
                      {entry.disabled ? <span className="entry-badge">已停用</span> : null}
                      <span className="entry-actions">
                        {access.canEdit ? (
                          <button
                            type="button"
                            className="btn btn-small btn-ghost"
                            onClick={() =>
                              toggleEntryMutation.mutate({
                                entryId: entry.id,
                                disabled: !entry.disabled,
                              })
                            }
                          >
                            {entry.disabled ? '启用' : '停用'}
                          </button>
                        ) : null}
                        {access.canEdit ? (
                          <button
                            type="button"
                            className="btn btn-small btn-danger-ghost"
                            onClick={() => deleteEntryMutation.mutate(entry.id)}
                          >
                            删除
                          </button>
                        ) : null}
                      </span>
                    </div>
                  ))}
                </div>
              </>
            ) : null}
          </section>
        </div>
      ) : null}
    </div>
  )
}
