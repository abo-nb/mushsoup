import { Link } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, errorMessage } from '../api'
import type { Bank, NoteSummary } from '../api'
import { Icon } from '../components/Icon'
import { EmptyState, ErrorNote, Loading } from '../components/Ui'
import { formatDate } from '../format'

export function FavoritesPage() {
  const queryClient = useQueryClient()

  const favoritesQuery = useQuery({
    queryKey: ['favorites'],
    queryFn: api.favorites,
    staleTime: 60_000,
  })
  const banksQuery = useQuery({ queryKey: ['banks'], queryFn: api.banks, staleTime: 60_000 })
  const notesQuery = useQuery({
    queryKey: ['notes'],
    queryFn: api.notes,
    staleTime: 2 * 60_000,
  })

  const removeMutation = useMutation({
    mutationFn: (input: { kind: 'bank' | 'note'; itemId: string }) =>
      api.removeFavorite(input.kind, input.itemId),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['favorites'] }),
  })

  const banksById = new Map((banksQuery.data ?? []).map((bank) => [bank.id, bank]))
  const notesById = new Map((notesQuery.data ?? []).map((note) => [note.id, note]))
  const favorites = favoritesQuery.data ?? []

  // 收藏只记录 id；内容改成私有或删掉后，这里就自然不显示。
  const banks = favorites
    .filter((item) => item.kind === 'bank')
    .map((item) => banksById.get(item.itemId))
    .filter((bank): bank is Bank => Boolean(bank) && bank?.visibility === 'public')
  const notes = favorites
    .filter((item) => item.kind === 'note')
    .map((item) => notesById.get(item.itemId))
    .filter((note): note is NoteSummary => Boolean(note) && note?.visibility === 'public')

  const loading =
    favoritesQuery.isPending || banksQuery.isPending || notesQuery.isPending
  const failed = favoritesQuery.isError || banksQuery.isError || notesQuery.isError
  const empty = !loading && !failed && banks.length === 0 && notes.length === 0

  function remove(kind: 'bank' | 'note', itemId: string) {
    removeMutation.mutate({ kind, itemId })
  }

  return (
    <div>
      <header className="page-header">
        <div className="page-heading">
          <h1>收藏</h1>
          <p className="page-sub">收藏的公开题库和讲义，随时回来练。</p>
        </div>
        <div className="header-actions">
          <Link to="/public" className="btn btn-ghost">
            <Icon name="globe" size={16} />
            去公开看看
          </Link>
        </div>
      </header>

      {favoritesQuery.isError ? (
        <ErrorNote message={errorMessage(favoritesQuery.error)} />
      ) : null}
      {loading ? <Loading /> : null}

      {banks.length > 0 ? (
        <section className="card favorite-card" aria-labelledby="favorite-banks-title">
          <h2 id="favorite-banks-title" className="section-title favorite-heading">
            题库
          </h2>
          <ul className="favorite-list">
            {banks.map((bank) => (
              <li key={bank.id} className="favorite-row">
                <span className="favorite-icon" aria-hidden="true">
                  <Icon name="book" size={18} />
                </span>
                <div className="favorite-main">
                  <Link to={`/banks/${bank.id}`} className="favorite-title">
                    {bank.name}
                  </Link>
                  <p className="favorite-meta">
                    <span>{bank.questionCount} 道题</span>
                    {bank.tags.length > 0 ? <span>{bank.tags.slice(0, 3).join(' · ')}</span> : null}
                    <span>更新于 {formatDate(bank.updatedAt || bank.createdAt)}</span>
                  </p>
                </div>
                <button
                  type="button"
                  className="btn btn-small btn-ghost"
                  onClick={() => remove('bank', bank.id)}
                  disabled={removeMutation.isPending}
                >
                  取消收藏
                </button>
              </li>
            ))}
          </ul>
        </section>
      ) : null}

      {notes.length > 0 ? (
        <section className="card favorite-card" aria-labelledby="favorite-notes-title">
          <h2 id="favorite-notes-title" className="section-title favorite-heading">
            讲义
          </h2>
          <ul className="favorite-list">
            {notes.map((note) => (
              <li key={note.id} className="favorite-row">
                <span className="favorite-icon" aria-hidden="true">
                  <Icon name="document" size={18} />
                </span>
                <div className="favorite-main">
                  <Link to={`/notes/${note.id}`} className="favorite-title">
                    {note.title}
                  </Link>
                  <p className="favorite-meta">
                    {note.sectionCount !== undefined ? <span>{note.sectionCount} 节</span> : null}
                    {(note.tags ?? []).length > 0 ? (
                      <span>{(note.tags ?? []).slice(0, 3).join(' · ')}</span>
                    ) : null}
                    {note.updatedAt ? <span>更新于 {formatDate(note.updatedAt)}</span> : null}
                  </p>
                </div>
                <button
                  type="button"
                  className="btn btn-small btn-ghost"
                  onClick={() => remove('note', note.id)}
                  disabled={removeMutation.isPending}
                >
                  取消收藏
                </button>
              </li>
            ))}
          </ul>
        </section>
      ) : null}

      {removeMutation.isError ? <ErrorNote message={errorMessage(removeMutation.error)} /> : null}

      {empty ? (
        <EmptyState title="还没有收藏" hint="在公开题库或讲义页点「收藏」，就会出现在这里。">
          <Link to="/public" className="btn btn-primary">
            <Icon name="globe" size={16} />
            去公开看看
          </Link>
        </EmptyState>
      ) : null}
    </div>
  )
}
