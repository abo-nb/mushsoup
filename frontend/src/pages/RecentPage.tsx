import { useMemo } from 'react'
import { Link } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { api, errorMessage } from '../api'
import type { Bank, NoteSummary } from '../api'
import { Icon } from '../components/Icon'
import { EmptyState, ErrorNote, Loading } from '../components/Ui'
import { formatDate } from '../format'
import { canonicalSubject, dedupeTags, subjectLabel } from '../subjects'

const QUICK_SLOTS = 6
const FEED_LIMIT = 12

interface Entry {
  key: string
  kind: 'bank' | 'note'
  id: string
  title: string
  subtitle: string
  href: string
  tags: string[]
  updatedAt: string
  meta: string
}

interface QuickSlot {
  entry: Entry
  openedAt: string | null
}

function timeAgo(value: string): string {
  const then = new Date(value).getTime()
  if (Number.isNaN(then)) return ''
  const minutes = Math.floor((Date.now() - then) / 60_000)
  if (minutes < 1) return '刚刚'
  if (minutes < 60) return `${minutes} 分钟前`
  const hours = Math.floor(minutes / 60)
  if (hours < 24) return `${hours} 小时前`
  const days = Math.floor(hours / 24)
  if (days < 30) return `${days} 天前`
  return formatDate(value)
}

function bankEntry(bank: Bank): Entry {
  return {
    key: `bank:${bank.id}`,
    kind: 'bank',
    id: bank.id,
    title: bank.name,
    subtitle: bank.description ?? '',
    href: `/banks/${bank.id}`,
    tags: bank.tags ?? [],
    updatedAt: bank.updatedAt || bank.createdAt,
    meta: `${bank.questionCount} 道题`,
  }
}

function noteEntry(note: NoteSummary): Entry {
  return {
    key: `note:${note.id}`,
    kind: 'note',
    id: note.id,
    title: note.title,
    subtitle: note.subtitle ?? '',
    href: `/notes/${note.id}`,
    tags: note.tags ?? [],
    updatedAt: note.updatedAt || note.createdAt || '',
    meta: note.sectionCount !== undefined ? `${note.sectionCount} 节` : '讲义',
  }
}

function KindBadge({ kind }: { kind: Entry['kind'] }) {
  return (
    <span className={kind === 'bank' ? 'recent-kind recent-kind-bank' : 'recent-kind recent-kind-note'}>
      <Icon name={kind === 'bank' ? 'book' : 'document'} size={13} />
      {kind === 'bank' ? '题库' : '讲义'}
    </span>
  )
}

function EntryTags({ tags }: { tags: string[] }) {
  const unique = dedupeTags(tags).slice(0, 2)
  if (unique.length === 0) return null
  return (
    <span className="recent-tags">
      {unique.map((tag) => (
        <span key={tag} className={canonicalSubject(tag) ? 'tag-pill tag-pill-subject' : 'tag-pill'}>
          {subjectLabel(tag)}
        </span>
      ))}
    </span>
  )
}

export function RecentPage() {
  const banksQuery = useQuery({ queryKey: ['banks'], queryFn: api.banks, staleTime: 60_000 })
  const notesQuery = useQuery({ queryKey: ['notes'], queryFn: api.notes, staleTime: 2 * 60_000 })
  const recentsQuery = useQuery({
    queryKey: ['recents'],
    queryFn: () => api.recentItems(12),
    staleTime: 15_000,
  })

  const banks = banksQuery.data ?? []
  const notes = notesQuery.data ?? []
  const recents = recentsQuery.data ?? []

  const allEntries = useMemo(() => {
    const map = new Map<string, Entry>()
    for (const bank of banks) map.set(`bank:${bank.id}`, bankEntry(bank))
    for (const note of notes) map.set(`note:${note.id}`, noteEntry(note))
    return map
  }, [banks, notes])

  const feed = useMemo(() => {
    const items: Entry[] = []
    for (const bank of banks) {
      if (bank.visibility === 'public') items.push(bankEntry(bank))
    }
    for (const note of notes) {
      if (note.visibility === 'public') items.push(noteEntry(note))
    }
    return items.sort(
      (left, right) => new Date(right.updatedAt).getTime() - new Date(left.updatedAt).getTime(),
    )
  }, [banks, notes])

  // 最近打开优先，不足 6 个时用最新公开内容补齐，保证上排始终是满的。
  const quick = useMemo(() => {
    const slots: QuickSlot[] = []
    const used = new Set<string>()

    for (const item of recents) {
      if (slots.length >= QUICK_SLOTS) break
      const entry = allEntries.get(`${item.kind}:${item.itemId}`)
      if (!entry || used.has(entry.key)) continue
      used.add(entry.key)
      slots.push({ entry, openedAt: item.lastOpenedAt })
    }

    const opened = slots.length
    for (const entry of feed) {
      if (slots.length >= QUICK_SLOTS) break
      if (used.has(entry.key)) continue
      used.add(entry.key)
      slots.push({ entry, openedAt: null })
    }

    return { slots, opened }
  }, [recents, allEntries, feed])

  const loaded =
    banksQuery.isSuccess && notesQuery.isSuccess && !recentsQuery.isPending
  const empty = loaded && feed.length === 0 && quick.slots.length === 0

  const quickHint =
    quick.opened === 0
      ? '还没有打开记录，先放上最近公开的内容。'
      : quick.opened < QUICK_SLOTS
        ? '不足 6 个时用最新公开内容补齐。'
        : '按最近打开排序。'

  return (
    <div>
      <header className="page-header">
        <div className="page-heading">
          <p className="eyebrow">动态</p>
          <h1>最近</h1>
        </div>
      </header>

      {banksQuery.isPending || notesQuery.isPending ? <Loading /> : null}
      {banksQuery.isError ? <ErrorNote message={`题库：${errorMessage(banksQuery.error)}`} /> : null}
      {notesQuery.isError ? <ErrorNote message={`讲义：${errorMessage(notesQuery.error)}`} /> : null}
      {recentsQuery.isError ? (
        <ErrorNote message={`最近记录：${errorMessage(recentsQuery.error)}`} />
      ) : null}

      {quick.slots.length > 0 ? (
        <section className="recent-section" aria-labelledby="recent-quick-heading">
          <div className="section-head">
            <h2 className="section-title" id="recent-quick-heading">
              最近常用
            </h2>
            <span className="muted">{quickHint}</span>
          </div>
          <div className="recent-quick-grid">
            {quick.slots.map((slot) => (
              <Link key={slot.entry.key} to={slot.entry.href} className="recent-card">
                <div className="recent-card-top">
                  <KindBadge kind={slot.entry.kind} />
                </div>
                <h3 className="recent-card-title">{slot.entry.title}</h3>
                <p className="recent-card-sub">{slot.entry.subtitle || '暂无简介'}</p>
                <div className="recent-card-foot">
                  <span className="recent-card-meta">{slot.entry.meta}</span>
                  <EntryTags tags={slot.entry.tags} />
                </div>
                <span className="recent-card-time">
                  {slot.openedAt
                    ? `${timeAgo(slot.openedAt)}打开`
                    : `${timeAgo(slot.entry.updatedAt)}更新`}
                </span>
              </Link>
            ))}
          </div>
        </section>
      ) : null}

      {feed.length > 0 ? (
        <section className="recent-section" aria-labelledby="recent-feed-heading">
          <div className="section-head">
            <h2 className="section-title" id="recent-feed-heading">
              最新公开
            </h2>
            <span className="muted">按更新时间排序</span>
          </div>
          <ol className="recent-feed">
            {feed.slice(0, FEED_LIMIT).map((entry) => (
              <li key={entry.key} className="recent-feed-item">
                <span className="recent-feed-date">{timeAgo(entry.updatedAt)}</span>
                <Link to={entry.href} className="recent-feed-link">
                  <span className="recent-feed-main">
                    <KindBadge kind={entry.kind} />
                    <span className="recent-feed-title">{entry.title}</span>
                    {entry.subtitle ? (
                      <span className="recent-feed-sub">{entry.subtitle}</span>
                    ) : null}
                  </span>
                  <span className="recent-feed-aside">
                    <EntryTags tags={entry.tags} />
                    <span className="recent-feed-meta">{entry.meta}</span>
                  </span>
                </Link>
              </li>
            ))}
          </ol>
          {feed.length > FEED_LIMIT ? (
            <div className="recent-more">
              <Link to="/public" className="btn btn-small btn-ghost">
                还有 {feed.length - FEED_LIMIT} 项，查看全部
                <Icon name="arrow-left" size={14} className="bank-open-icon" />
              </Link>
            </div>
          ) : null}
        </section>
      ) : null}

      {empty ? (
        <EmptyState title="还没有公开内容" hint="可以先建一个自己的题库，或者去公开页看看。">
          <div className="recent-empty-actions">
            <Link to="/public" className="btn btn-small btn-ghost">
              <Icon name="globe" size={15} />
              浏览全部公开内容
            </Link>
            <Link to="/banks" className="btn btn-small btn-primary">
              <Icon name="book" size={15} />
              去我的题库
            </Link>
          </div>
        </EmptyState>
      ) : null}
    </div>
  )
}
