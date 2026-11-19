import { useEffect, useRef } from 'react'
import type { MouseEvent } from 'react'
import { Link, useParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { api, errorMessage } from '../api'
import type { NoteSection } from '../api'
import { useAuth } from '../auth-context'
import { Icon } from '../components/Icon'
import { MarkdownView } from '../components/MarkdownView'
import { NoteBlocks } from '../components/NoteBlocks'
import { ShareButton } from '../components/ShareButton'
import { FavoriteButton } from '../components/FavoriteButton'
import { ErrorNote, Loading } from '../components/Ui'
import { asBlocks, asString } from '../note-format'
import { formatDate } from '../format'
import { useRecordRecent } from '../recent'

const NOTICE_TONES = ['tip', 'warn', 'note', 'example']
const NOTICE_LABELS: Record<string, string> = {
  tip: '提示',
  warn: '注意',
  note: '说明',
  example: '例',
}

interface TocEntry {
  id: string
  title: string
}

interface TocSection {
  key: string
  id: string
  label?: string
  title: string
  entries: TocEntry[]
}

function anchorFor(id: string, index: number): string {
  const slug = id.replace(/[^\p{L}\p{N}_-]+/gu, '-').replace(/^-+|-+$/g, '')
  return `note-section-${slug || index}`
}

/** 目录只显示纯文本：去掉标题里可能存在的行内 Markdown 标记。 */
function plainTitle(source: string): string {
  return source
    .replace(/!\[([^\]]*)\]\([^)]*\)/g, '$1')
    .replace(/\[([^\]]+)\]\([^)]*\)/g, '$1')
    .replace(/[*_~`]+/g, '')
    .replace(/\s+/g, ' ')
    .trim()
}

function buildToc(sections: NoteSection[]): TocSection[] {
  return sections.map((section, index) => {
    const entries: TocEntry[] = []
    asBlocks(section.blocks).forEach((block, blockIndex) => {
      if (block.type !== 'card' && block.type !== 'summary') return
      const title = plainTitle(asString(block.title))
      if (!title) return
      entries.push({
        id: `note-section-${index}-block-${blockIndex}`,
        title,
      })
    })

    return {
      key: section.id || String(index),
      id: anchorFor(section.id ?? '', index),
      label: section.label,
      title: section.title,
      entries,
    }
  })
}

function TocList({
  sections,
  onNavigate,
}: {
  sections: TocSection[]
  onNavigate?: (id: string) => void
}) {
  function handleClick(event: MouseEvent<HTMLAnchorElement>, id: string) {
    if (!onNavigate) return
    event.preventDefault()
    onNavigate(id)
  }

  return (
    <ol className="note-toc-list">
      {sections.map((section) => (
        <li className="note-toc-section" key={section.key}>
          <a
            href={`#${section.id}`}
            className="note-toc-link"
            onClick={(event) => handleClick(event, section.id)}
          >
            {section.label ? <span className="note-toc-label">{section.label}</span> : null}
            <span className="note-toc-text">{section.title}</span>
          </a>
          {section.entries.length > 0 ? (
            <ol className="note-toc-sublist">
              {section.entries.map((entry) => (
                <li key={entry.id}>
                  <a
                    href={`#${entry.id}`}
                    className="note-toc-link note-toc-link-sub"
                    onClick={(event) => handleClick(event, entry.id)}
                  >
                    <span className="note-toc-text">{entry.title}</span>
                  </a>
                </li>
              ))}
            </ol>
          ) : null}
        </li>
      ))}
    </ol>
  )
}

export function NotePage() {
  const { id } = useParams<{ id: string }>()
  const { user, isLoading: authLoading } = useAuth()
  useRecordRecent('note', user ? id : undefined)
  const noteId = id ?? ''
  const isGuest = !user
  const noteQuery = useQuery({
    queryKey: ['note', noteId, isGuest ? 'public' : 'member'],
    queryFn: () => (isGuest ? api.publicNote(noteId) : api.note(noteId)),
    enabled: Boolean(noteId) && !authLoading,
    retry: false,
    staleTime: 5 * 60_000,
  })
  const detailsRef = useRef<HTMLDetailsElement | null>(null)

  useEffect(() => {
    const body = document.body
    body.classList.add('note-reading')
    let lastY = window.scrollY
    let frame = 0

    const setHidden = (hidden: boolean) => {
      if (body.classList.contains('note-topbar-hidden') !== hidden) {
        body.classList.toggle('note-topbar-hidden', hidden)
      }
    }

    const update = () => {
      frame = 0
      const y = window.scrollY
      if (y < 96) {
        setHidden(false)
      } else if (y > lastY + 6) {
        setHidden(true)
      } else if (y < lastY - 6) {
        setHidden(false)
      }
      lastY = y
    }

    const onScroll = () => {
      if (!frame) frame = window.requestAnimationFrame(update)
    }

    window.addEventListener('scroll', onScroll, { passive: true })
    return () => {
      window.removeEventListener('scroll', onScroll)
      if (frame) window.cancelAnimationFrame(frame)
      body.classList.remove('note-reading', 'note-topbar-hidden')
    }
  }, [])

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
  const doc = note.content
  const sections = Array.isArray(doc?.sections) ? doc.sections : []
  const tags = note.tags ?? doc?.tags ?? []
  const subtitle = note.subtitle ?? doc?.subtitle ?? ''
  const title = note.title || doc?.title || '未命名讲义'
  const notice = doc?.notice
  const noticeTone =
    notice && NOTICE_TONES.includes(notice.tone) ? notice.tone : 'note'
  const toc = buildToc(sections)
  const showToc = toc.length > 1 || toc.some((section) => section.entries.length > 0)
  const isOwner = Boolean(user?.id && note.ownerId && note.ownerId === user.id)

  function navigateToSection(id: string) {
    const target = document.getElementById(id)
    if (!target) return
    target.scrollIntoView({ block: 'start' })
    window.history.replaceState(null, '', `#${id}`)
    if (detailsRef.current) detailsRef.current.open = false
  }

  return (
    <div className="note-page">
      {user ? (
        <Link to="/notes" className="back-link">
          ← 返回讲义列表
        </Link>
      ) : null}

      <div className={showToc ? 'note-layout' : 'note-layout note-layout-solo'}>
        <article className="note-main">
          {showToc ? (
            <details className="note-toc-details" ref={detailsRef}>
              <summary>目录</summary>
              <div className="note-toc-details-body">
                <TocList sections={toc} onNavigate={navigateToSection} />
              </div>
            </details>
          ) : null}

          <header className="note-hero">
            <div className="note-hero-head">
              <h1>{title}</h1>
              <div className="note-hero-actions">
                {user && note.visibility === 'public' ? (
                  <>
                    <FavoriteButton kind="note" itemId={noteId} />
                    <ShareButton kind="note" targetId={noteId} />
                  </>
                ) : null}
                {isOwner ? (
                  <Link
                    to={`/notes/${noteId}/settings`}
                    className="btn btn-small btn-ghost note-hero-action"
                  >
                    <Icon name="settings" size={16} />
                    讲义设置
                  </Link>
                ) : null}
              </div>
            </div>
            {subtitle ? <p className="note-subtitle">{subtitle}</p> : null}
            {tags.length > 0 ? (
              <div className="note-tags">
                {tags.map((tag) => (
                  <span className="note-tag" key={tag}>
                    {tag}
                  </span>
                ))}
              </div>
            ) : null}
            {note.updatedAt ? (
              <p className="note-updated">更新于 {formatDate(note.updatedAt)}</p>
            ) : null}
          </header>

          {notice ? (
            <aside className="note-notice" data-tone={noticeTone}>
              <p className="note-notice-label">{NOTICE_LABELS[noticeTone] ?? '说明'}</p>
              <div className="note-notice-body">
                <MarkdownView source={notice.text} />
              </div>
            </aside>
          ) : null}

          {sections.map((section, index) => (
            <section
              className="note-section"
              id={anchorFor(section.id ?? '', index)}
              key={section.id || index}
              tabIndex={-1}
            >
              <header className="note-section-head">
                {section.label ? (
                  <p className="note-section-label">{section.label}</p>
                ) : null}
                <h2 className="note-section-title">{section.title}</h2>
                {section.subtitle ? (
                  <p className="note-section-subtitle">{section.subtitle}</p>
                ) : null}
              </header>
              <NoteBlocks
                blocks={asBlocks(section.blocks)}
                idPrefix={`note-section-${index}-block-`}
              />
            </section>
          ))}

          {sections.length === 0 ? <p className="muted">这份讲义还没有内容。</p> : null}
        </article>

        {showToc ? (
          <nav className="note-toc" aria-label="讲义目录">
            <p className="note-toc-title">目录</p>
            <TocList sections={toc} onNavigate={navigateToSection} />
          </nav>
        ) : null}
      </div>
    </div>
  )
}
