import { useState } from 'react'
import type { FormEvent } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, errorMessage } from '../api'
import type { Note } from '../api'
import { useAuth } from '../auth-context'
import { Icon } from '../components/Icon'
import { TagInput } from '../components/TagInput'
import { ErrorNote, Loading } from '../components/Ui'

const VISIBILITY_OPTIONS = [
  { value: 'private', label: '私有', hint: '只有你能在讲义列表里看到' },
  { value: 'public', label: '公开', hint: '所有人能在讲义列表里看到并阅读' },
]

export function NoteSettingsPage() {
  const { id } = useParams<{ id: string }>()
  const noteId = id ?? ''
  const { user } = useAuth()
  const noteQuery = useQuery({
    queryKey: ['note', noteId],
    queryFn: () => api.note(noteId),
    enabled: Boolean(noteId),
    staleTime: 5 * 60_000,
  })

  if (!noteId) {
    return <ErrorNote message="找不到这份讲义。" />
  }

  if (noteQuery.isPending) {
    return <Loading full label="正在载入讲义…" />
  }

  if (noteQuery.isError) {
    return (
      <div>
        <Link to="/notes" className="back-link">
          ← 返回讲义列表
        </Link>
        <ErrorNote message={errorMessage(noteQuery.error)} />
      </div>
    )
  }

  const note = noteQuery.data
  const isOwner = Boolean(user?.id && note.ownerId && note.ownerId === user.id)

  return (
    <div className="note-settings-page">
      <Link to={`/notes/${note.id}`} className="back-link">
        ← 返回讲义
      </Link>

      <header className="page-header">
        <div className="page-heading">
          <p className="eyebrow">讲义设置</p>
          <h1>{note.title}</h1>
          <p className="page-sub">管理这份讲义的标题、简介、可见性和学科标签。</p>
        </div>
      </header>

      {isOwner ? (
        <NoteSettingsForm key={note.id} note={note} />
      ) : (
        <ErrorNote message="只有创建者可以修改讲义设置" />
      )}
    </div>
  )
}

function NoteSettingsForm({ note }: { note: Note }) {
  const queryClient = useQueryClient()
  const navigate = useNavigate()
  const [title, setTitle] = useState(note.title || note.content?.title || '')
  const [subtitle, setSubtitle] = useState(note.subtitle ?? note.content?.subtitle ?? '')
  const [visibility, setVisibility] = useState(
    (note.visibility ?? note.content?.visibility) === 'public' ? 'public' : 'private',
  )
  const [tags, setTags] = useState<string[]>(note.tags ?? note.content?.tags ?? [])
  const [year, setYear] = useState(note.year ?? note.content?.year ?? '')
  const initialSemester = note.semester ?? note.content?.semester ?? ''
  const [semester, setSemester] = useState(
    initialSemester === '上' || initialSemester === '下' ? initialSemester : '',
  )

  const missingTitle = title.trim().length === 0
  const missingTags = visibility === 'public' && tags.length === 0

  const updateMutation = useMutation({
    mutationFn: () =>
      api.updateNoteMeta(note.id, {
        title: title.trim(),
        subtitle: subtitle.trim(),
        visibility,
        tags,
        year: year.trim(),
        semester,
      }),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['note', note.id] })
      void queryClient.invalidateQueries({ queryKey: ['notes'] })
    },
  })

  function markDirty() {
    if (updateMutation.isSuccess || updateMutation.isError) updateMutation.reset()
  }

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (missingTitle || missingTags || updateMutation.isPending) return
    updateMutation.mutate()
  }

  const deleteMutation = useMutation({
    mutationFn: () => api.deleteNote(note.id),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['notes'] })
      void queryClient.invalidateQueries({ queryKey: ['note', note.id] })
      navigate('/notes')
    },
  })

  function handleDelete() {
    if (deleteMutation.isPending) return
    if (!window.confirm('确定删除这份讲义吗？删除后无法恢复。')) return
    deleteMutation.mutate()
  }

  return (
    <>
      <form className="note-settings-form" onSubmit={handleSubmit}>
        <section className="card card-pad note-settings-section">
          <div className="note-settings-head">
            <div>
              <h2 className="section-title">基本信息</h2>
              <p className="muted note-settings-desc">
                标题和简介会显示在讲义列表、阅读页和公开内容里。
              </p>
            </div>
          </div>

          <div className="field">
            <label htmlFor="note-settings-title">标题</label>
            <input
              id="note-settings-title"
              className="input"
              type="text"
              required
              maxLength={120}
              value={title}
              onChange={(event) => {
                markDirty()
                setTitle(event.target.value)
              }}
              placeholder="如 企業管理概論"
            />
          </div>

          <div className="field">
            <label htmlFor="note-settings-subtitle">简介（选填）</label>
            <textarea
              id="note-settings-subtitle"
              className="textarea"
              rows={2}
              maxLength={500}
              value={subtitle}
              onChange={(event) => {
                markDirty()
                setSubtitle(event.target.value)
              }}
              placeholder="一句话说明这份讲义的范围"
            />
          </div>

          {missingTitle ? (
            <p className="form-hint note-settings-warning" role="status">
              标题不能为空。
            </p>
          ) : null}
        </section>

        <section className="card card-pad note-settings-section">
          <div className="note-settings-head">
            <div>
              <h2 className="section-title" id="note-visibility-title">
                可见性
              </h2>
              <p className="muted note-settings-desc">公开的讲义会出现在所有人的讲义列表里。</p>
            </div>
          </div>

          <div className="radio-cards" role="radiogroup" aria-labelledby="note-visibility-title">
            {VISIBILITY_OPTIONS.map((option) => (
              <label
                key={option.value}
                className={visibility === option.value ? 'radio-card selected' : 'radio-card'}
              >
                <input
                  type="radio"
                  name="note-visibility"
                  value={option.value}
                  checked={visibility === option.value}
                  onChange={() => {
                    markDirty()
                    setVisibility(option.value)
                  }}
                />
                <span className="radio-card-text">
                  <strong>{option.label}</strong>
                  <small>{option.hint}</small>
                </span>
                <span className="radio-card-check" aria-hidden="true">
                  {visibility === option.value ? <Icon name="check" size={16} /> : null}
                </span>
              </label>
            ))}
          </div>
        </section>

        <section className="card card-pad note-settings-section">
          <div className="note-settings-head">
            <div>
              <h2 className="section-title">学年与学期</h2>
              <p className="muted note-settings-desc">
                标记这份讲义所属的学年和学期，方便按时间归档。
              </p>
            </div>
          </div>

          <div className="form-row">
            <div className="field">
              <label htmlFor="note-settings-year">学年</label>
              <input
                id="note-settings-year"
                className="input"
                type="text"
                maxLength={20}
                value={year}
                onChange={(event) => {
                  markDirty()
                  setYear(event.target.value)
                }}
                placeholder="如 高一 / 2025-2026"
              />
            </div>

            <div className="field">
              <label htmlFor="note-settings-semester">学期</label>
              <select
                id="note-settings-semester"
                className="select"
                value={semester}
                onChange={(event) => {
                  markDirty()
                  setSemester(event.target.value)
                }}
              >
                <option value="">未设置</option>
                <option value="上">上学期</option>
                <option value="下">下学期</option>
              </select>
            </div>
          </div>
        </section>

        <section className="card card-pad note-settings-section">
          <div className="note-settings-head">
            <div>
              <h2 className="section-title">学科标签</h2>
              <p className="muted note-settings-desc">
                标签会显示在阅读页和讲义卡片上，方便按学科归类。
              </p>
            </div>
          </div>

          <TagInput
            value={tags}
            onChange={(next) => {
              markDirty()
              setTags(next)
            }}
            hint="公开讲义至少需要一个学科标签；私有讲义可以不填。"
          />

          {missingTags ? (
            <p className="form-hint note-settings-warning" role="status">
              公开讲义至少需要一个学科标签，补上后才能保存。
            </p>
          ) : null}
        </section>

        {updateMutation.isError ? (
          <ErrorNote message={errorMessage(updateMutation.error)} />
        ) : null}

        <div className="form-actions note-settings-actions">
          <span
            className="note-settings-status"
            data-state={updateMutation.isSuccess ? 'saved' : undefined}
            role="status"
            aria-live="polite"
          >
            {updateMutation.isPending ? '保存中…' : updateMutation.isSuccess ? '已保存' : ''}
          </span>
          <button
            type="submit"
            className="btn btn-primary"
            disabled={missingTitle || missingTags || updateMutation.isPending}
          >
            {updateMutation.isPending ? '保存中…' : '保存设置'}
          </button>
        </div>
      </form>

      <section className="card card-pad note-settings-section note-danger">
        <div className="note-settings-head">
          <div>
            <h2 className="section-title" id="note-danger-title">
              删除讲义
            </h2>
            <p className="muted note-settings-desc">
              删除后这份讲义会从讲义列表和公开内容中移除，无法恢复。
            </p>
          </div>
        </div>

        {deleteMutation.isError ? (
          <ErrorNote message={errorMessage(deleteMutation.error)} />
        ) : null}

        <div className="note-danger-actions">
          <button
            type="button"
            className="btn btn-danger-ghost"
            onClick={handleDelete}
            disabled={deleteMutation.isPending}
          >
            {deleteMutation.isPending ? '删除中…' : '删除讲义'}
          </button>
        </div>
      </section>
    </>
  )
}
