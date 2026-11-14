import { useEffect, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, errorMessage } from '../api'
import { useAuth } from '../auth-context'
import { Icon } from '../components/Icon'
import { EmptyState, ErrorNote, Loading } from '../components/Ui'
import { formatDate } from '../format'
import { canonicalSubject, dedupeTags, subjectLabel } from '../subjects'

export function BanksPage() {
  const { user } = useAuth()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [showCreate, setShowCreate] = useState(false)
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const nameInputRef = useRef<HTMLInputElement>(null)
  const toggleRef = useRef<HTMLButtonElement>(null)

  const banksQuery = useQuery({ queryKey: ['banks'], queryFn: api.banks })
  const myBanks = (banksQuery.data ?? []).filter(
    (bank) => bank.ownerId === user?.id || bank.memberIds.includes(user?.id ?? ''),
  )

  const createMutation = useMutation({
    mutationFn: () =>
      api.createBank({
        name: name.trim(),
        description: description.trim() || undefined,
      }),
    onSuccess: (bank) => {
      void queryClient.invalidateQueries({ queryKey: ['banks'] })
      navigate(`/banks/${bank.id}`)
    },
  })

  useEffect(() => {
    if (showCreate) nameInputRef.current?.focus()
  }, [showCreate])

  function handleCreate(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    createMutation.mutate()
  }

  function closeCreate() {
    setShowCreate(false)
    createMutation.reset()
    toggleRef.current?.focus()
  }

  return (
    <div>
      <header className="page-header">
        <div className="page-heading">
          <h1>题库</h1>
        </div>
        <div className="header-actions">
          <button
            ref={toggleRef}
            type="button"
            className={showCreate ? 'btn btn-ghost' : 'btn btn-primary'}
            aria-expanded={showCreate}
            aria-controls="create-bank-panel"
            onClick={() => (showCreate ? closeCreate() : setShowCreate(true))}
          >
            <Icon name={showCreate ? 'x' : 'plus'} size={16} />
            {showCreate ? '收起' : '新建题库'}
          </button>
        </div>
      </header>

      <div
        id="create-bank-panel"
        className={showCreate ? 'create-bank-wrap open' : 'create-bank-wrap'}
        aria-hidden={!showCreate}
        inert={!showCreate}
      >
        <div className="create-bank-inner">
          <form className="card card-pad create-bank" onSubmit={handleCreate}>
            <h2 className="section-title">新建题库</h2>
            <div className="field">
              <label htmlFor="bank-name">名称</label>
              <input
                ref={nameInputRef}
                id="bank-name"
                className="input"
                type="text"
                required
                maxLength={60}
                value={name}
                onChange={(event) => setName(event.target.value)}
                placeholder="例如：高一数学 · 函数"
              />
            </div>
            <div className="field">
              <label htmlFor="bank-description">简介（选填）</label>
              <textarea
                id="bank-description"
                className="textarea"
                rows={2}
                maxLength={300}
                value={description}
                onChange={(event) => setDescription(event.target.value)}
                placeholder="简单说明这个题库的内容"
              />
            </div>
            {createMutation.isError ? (
              <ErrorNote message={errorMessage(createMutation.error)} />
            ) : null}
            <div className="form-actions">
              <button type="button" className="btn btn-ghost" onClick={closeCreate}>
                取消
              </button>
              <button
                type="submit"
                className="btn btn-primary"
                disabled={!name.trim() || createMutation.isPending}
              >
                {createMutation.isPending ? '创建中…' : '创建题库'}
              </button>
            </div>
          </form>
        </div>
      </div>

      {banksQuery.isPending ? <Loading /> : null}
      {banksQuery.isError ? <ErrorNote message={errorMessage(banksQuery.error)} /> : null}

      {myBanks.length > 0 ? (
        <div className="bank-grid">
          {myBanks.map((bank) => (
            <Link key={bank.id} to={`/banks/${bank.id}`} className="bank-card">
              <div className="bank-card-top">
                <h2>{bank.name}</h2>
                <span className={bank.visibility === 'public' ? 'badge' : 'badge badge-quiet'}>
                  {bank.visibility === 'public' ? '公开' : '私有'}
                </span>
              </div>
              <div className="bank-stats">
                <span className="bank-stat">
                  <strong>{bank.questionCount}</strong>
                  <span>道题</span>
                </span>
                <span className="bank-owner">
                  {bank.ownerId === user?.id ? '我创建的' : '共享题库'}
                </span>
              </div>
              <p className="bank-desc">{bank.description || '暂无简介'}</p>
              <BankTags tags={bank.tags ?? []} />
              <div className="bank-meta">
                <span className="bank-created">创建于 {formatDate(bank.createdAt)}</span>
                <Icon name="arrow-left" size={14} className="bank-open-icon" />
              </div>
            </Link>
          ))}
        </div>
      ) : null}

      {banksQuery.data && myBanks.length === 0 ? (
        <EmptyState title="还没有题库" hint="创建一个题库，开始整理题目。">
          {!showCreate ? (
            <button type="button" className="btn btn-primary" onClick={() => setShowCreate(true)}>
              <Icon name="plus" size={16} />
              新建题库
            </button>
          ) : null}
        </EmptyState>
      ) : null}
    </div>
  )
}

function BankTags({ tags }: { tags: string[] }) {
  const unique = dedupeTags(tags)
  if (unique.length === 0) return null

  const visible = unique.slice(0, 3)
  return (
    <div className="tag-row">
      {visible.map((tag) => (
        <span
          key={tag}
          className={canonicalSubject(tag) ? 'tag-pill tag-pill-subject' : 'tag-pill'}
        >
          {subjectLabel(tag)}
        </span>
      ))}
      {unique.length > visible.length ? (
        <span className="tag-pill">+{unique.length - visible.length}</span>
      ) : null}
    </div>
  )
}
