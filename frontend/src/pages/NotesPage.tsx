import { Link } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { api, errorMessage } from '../api'
import { Icon } from '../components/Icon'
import { EmptyState, ErrorNote, Loading } from '../components/Ui'
import { formatDate } from '../format'
import { canonicalSubject, dedupeTags, subjectLabel } from '../subjects'

export function NotesPage() {
  const notesQuery = useQuery({ queryKey: ['notes'], queryFn: api.notes })
  const notes = notesQuery.data ?? []

  return (
    <div>
      <header className="page-header">
        <div className="page-heading">
          <h1>讲义</h1>
        </div>
        <div className="header-actions">
          <Link to="/notes/import" className="btn btn-primary">
            <Icon name="upload" size={16} />
            导入讲义
          </Link>
        </div>
      </header>

      {notesQuery.isPending ? <Loading /> : null}
      {notesQuery.isError ? <ErrorNote message={errorMessage(notesQuery.error)} /> : null}

      {notesQuery.data && notes.length > 0 ? (
        <div className="notes-grid">
          {notes.map((note) => {
            const tags = dedupeTags(note.tags ?? [])
            const visibleTags = tags.slice(0, 4)
            return (
              <Link key={note.id} to={`/notes/${note.id}`} className="note-list-card">
                <h2>{note.title}</h2>
                {note.subtitle ? <p className="note-list-sub">{note.subtitle}</p> : null}
                {visibleTags.length > 0 ? (
                  <div className="tag-row">
                    {visibleTags.map((tag) => (
                      <span
                        key={tag}
                        className={
                          canonicalSubject(tag) ? 'tag-pill tag-pill-subject' : 'tag-pill'
                        }
                      >
                        {subjectLabel(tag)}
                      </span>
                    ))}
                    {tags.length > visibleTags.length ? (
                      <span className="tag-pill">+{tags.length - visibleTags.length}</span>
                    ) : null}
                  </div>
                ) : null}
                <div className="note-list-foot">
                  {note.visibility ? (
                    <span className={note.visibility === 'public' ? 'badge' : 'badge badge-quiet'}>
                      {note.visibility === 'public' ? '公开' : '私有'}
                    </span>
                  ) : null}
                  {note.sectionCount !== undefined ? (
                    <span>{note.sectionCount} 节</span>
                  ) : null}
                  {note.updatedAt ? (
                    <span>更新于 {formatDate(note.updatedAt)}</span>
                  ) : null}
                </div>
              </Link>
            )
          })}
        </div>
      ) : null}

      {notesQuery.data && notes.length === 0 ? (
        <EmptyState title="还没有讲义" hint="把整份讲义 JSON 粘贴进来，发布后就能在这里阅读。">
          <Link to="/notes/import" className="btn btn-primary">
            <Icon name="upload" size={16} />
            导入讲义
          </Link>
        </EmptyState>
      ) : null}
    </div>
  )
}
