import { useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { api, errorMessage } from '../api'
import { useAuth } from '../auth-context'
import { Icon } from '../components/Icon'
import { EmptyState, ErrorNote, Loading } from '../components/Ui'
import { formatDate } from '../format'
import {
  SUBJECTS,
  canonicalSubject,
  dedupeTags,
  matchesSubject,
  subjectLabel,
} from '../subjects'

const ALL = 'all'
const UNSET = '__unset'
const UNCLASSIFIED = '__unclassified'

type FilterKey = 'year' | 'semester' | 'subject' | 'chapter'

interface FilterState {
  year: string
  semester: string
  subject: string
  chapter: string
}

const FILTER_KEYS: FilterKey[] = ['year', 'semester', 'subject', 'chapter']

interface PublicEntry {
  kind: 'bank' | 'note'
  id: string
  year?: string
  semester?: string
  tags: string[]
  /** undefined 表示题库章节索引还没拿到，既不算有章节也不算未标注。 */
  chapters?: string[]
  search: (string | null | undefined)[]
}

interface Choice {
  value: string
  label: string
}

interface DimensionOptions {
  choices: Choice[]
  blank?: Choice
}

function TagPills({ tags, max }: { tags: string[]; max: number }) {
  const unique = dedupeTags(tags)
  if (unique.length === 0) {
    return (
      <div className="tag-row">
        <span className="tag-pill">未分类</span>
      </div>
    )
  }

  const visible = unique.slice(0, max)
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

function termLabel(year?: string, semester?: string): string {
  const grade = (year ?? '').trim()
  const term = (semester ?? '').trim()
  return [grade, term ? `${term}学期` : ''].filter(Boolean).join(' · ')
}

function matchesTerm(value: string | undefined, filter: string): boolean {
  if (filter === ALL) return true
  const term = (value ?? '').trim()
  return filter === UNSET ? term === '' : term === filter
}

function matchesSubjectFilter(tags: string[] | undefined, filter: string): boolean {
  if (filter === ALL) return true
  const list = tags ?? []
  if (filter === UNCLASSIFIED) {
    return !list.some((tag) => canonicalSubject(tag) !== null)
  }
  return list.some((tag) => matchesSubject(tag, filter))
}

function normalizeChapters(chapters: string[] | undefined): string[] | undefined {
  if (chapters === undefined) return undefined
  return chapters.map((chapter) => chapter.trim()).filter(Boolean)
}

function matchesChapter(chapters: string[] | undefined, filter: string): boolean {
  if (filter === ALL) return true
  const values = normalizeChapters(chapters)
  if (values === undefined) return false
  return filter === UNSET ? values.length === 0 : values.includes(filter)
}

function entryMatches(
  entry: PublicEntry,
  filters: FilterState,
  query: string,
  except?: FilterKey,
): boolean {
  if (except !== 'year' && !matchesTerm(entry.year, filters.year)) return false
  if (except !== 'semester' && !matchesTerm(entry.semester, filters.semester)) return false
  if (except !== 'subject' && !matchesSubjectFilter(entry.tags, filters.subject)) return false
  if (except !== 'chapter' && !matchesChapter(entry.chapters, filters.chapter)) return false
  if (!query) return true
  return entry.search.some((value) => (value ?? '').toLowerCase().includes(query))
}

/** 收集某个维度在给定候选集里实际出现的取值。 */
function collectOptions(dimension: FilterKey, entries: PublicEntry[]): DimensionOptions {
  if (dimension === 'subject') {
    const found = new Set<string>()
    let hasBlank = false
    for (const entry of entries) {
      const subjects = entry.tags
        .map((tag) => canonicalSubject(tag))
        .filter((subject) => subject !== null)
      if (subjects.length === 0) {
        hasBlank = true
        continue
      }
      for (const subject of subjects) found.add(subject.id)
    }
    return {
      choices: SUBJECTS.filter((subject) => found.has(subject.id)).map((subject) => ({
        value: subject.id,
        label: subject.label,
      })),
      blank: hasBlank ? { value: UNCLASSIFIED, label: '未分类' } : undefined,
    }
  }

  const values = new Set<string>()
  let hasBlank = false
  for (const entry of entries) {
    if (dimension === 'chapter') {
      if (entry.chapters === undefined) continue
      if (entry.chapters.length === 0) hasBlank = true
      else for (const chapter of entry.chapters) values.add(chapter)
      continue
    }
    const value = ((dimension === 'year' ? entry.year : entry.semester) ?? '').trim()
    if (value) values.add(value)
    else hasBlank = true
  }

  const sorted = [...values].sort((left, right) => left.localeCompare(right, 'zh-CN'))
  return {
    choices: sorted.map((value) => ({
      value,
      label: dimension === 'semester' ? `${value}学期` : value,
    })),
    blank: hasBlank ? { value: UNSET, label: '未标注' } : undefined,
  }
}

function choiceValues(options: DimensionOptions): string[] {
  return [
    ...options.choices.map((choice) => choice.value),
    ...(options.blank ? [options.blank.value] : []),
  ]
}

function activeValue(value: string, choices: string[]): string {
  return value === ALL || choices.includes(value) ? value : ALL
}

export function PublicBanksPage() {
  const { user } = useAuth()
  const [yearFilter, setYearFilter] = useState(ALL)
  const [semesterFilter, setSemesterFilter] = useState(ALL)
  const [subjectFilter, setSubjectFilter] = useState(ALL)
  const [chapterFilter, setChapterFilter] = useState(ALL)
  const [query, setQuery] = useState('')
  const banksQuery = useQuery({ queryKey: ['banks'], queryFn: api.banks })
  const notesQuery = useQuery({ queryKey: ['notes'], queryFn: api.notes })

  const publicBanks = useMemo(
    () => (banksQuery.data ?? []).filter((bank) => bank.visibility === 'public'),
    [banksQuery.data],
  )
  const publicNotes = useMemo(
    () => (notesQuery.data ?? []).filter((note) => note.visibility === 'public'),
    [notesQuery.data],
  )

  const publicBankIds = useMemo(
    () => publicBanks.map((bank) => bank.id).sort(),
    [publicBanks],
  )

  const chapterIndexQuery = useQuery({
    queryKey: ['bank-chapter-index', publicBankIds],
    queryFn: () => api.bankChapterIndex(publicBankIds),
    enabled: publicBankIds.length > 0,
  })

  const bankChapters = useMemo(() => {
    const map = new Map<string, string[]>()
    for (const entry of chapterIndexQuery.data ?? []) {
      map.set(entry.bankId, normalizeChapters(entry.chapters) ?? [])
    }
    return map
  }, [chapterIndexQuery.data])

  const entries = useMemo(() => {
    const items: PublicEntry[] = []
    for (const bank of publicBanks) {
      const tags = bank.tags ?? []
      const chapters = bankChapters.has(bank.id) ? bankChapters.get(bank.id) : undefined
      items.push({
        kind: 'bank',
        id: bank.id,
        year: bank.year,
        semester: bank.semester,
        tags,
        chapters,
        search: [
          bank.name,
          bank.description,
          bank.year,
          bank.semester,
          termLabel(bank.year, bank.semester),
          ...tags,
          ...tags.map(subjectLabel),
          ...(chapters ?? []),
        ],
      })
    }
    for (const note of publicNotes) {
      const tags = note.tags ?? []
      const chapters = normalizeChapters(note.chapters ?? []) ?? []
      items.push({
        kind: 'note',
        id: note.id,
        year: note.year,
        semester: note.semester,
        tags,
        chapters,
        search: [
          note.title,
          note.subtitle,
          note.year,
          note.semester,
          termLabel(note.year, note.semester),
          ...tags,
          ...tags.map(subjectLabel),
          ...chapters,
        ],
      })
    }
    return items
  }, [publicBanks, publicNotes, bankChapters])

  const queryText = query.trim().toLowerCase()
  const rawFilters = useMemo<FilterState>(
    () => ({
      year: yearFilter,
      semester: semesterFilter,
      subject: subjectFilter,
      chapter: chapterFilter,
    }),
    [yearFilter, semesterFilter, subjectFilter, chapterFilter],
  )

  // 选项只在「其他筛选 + 搜索」都生效后出现，选中值若已不存在则回落到全部。
  const rawOptions = useMemo(() => {
    const options = {} as Record<FilterKey, DimensionOptions>
    for (const dimension of FILTER_KEYS) {
      options[dimension] = collectOptions(
        dimension,
        entries.filter((entry) => entryMatches(entry, rawFilters, queryText, dimension)),
      )
    }
    return options
  }, [entries, rawFilters, queryText])

  const activeYear = activeValue(yearFilter, choiceValues(rawOptions.year))
  const activeSemester = activeValue(semesterFilter, choiceValues(rawOptions.semester))
  const activeSubject = activeValue(subjectFilter, choiceValues(rawOptions.subject))
  const activeChapter = activeValue(chapterFilter, choiceValues(rawOptions.chapter))

  const activeFilters = useMemo<FilterState>(
    () => ({
      year: activeYear,
      semester: activeSemester,
      subject: activeSubject,
      chapter: activeChapter,
    }),
    [activeYear, activeSemester, activeSubject, activeChapter],
  )

  const options = useMemo(() => {
    const resolved = {} as Record<FilterKey, DimensionOptions>
    for (const dimension of FILTER_KEYS) {
      resolved[dimension] = collectOptions(
        dimension,
        entries.filter((entry) => entryMatches(entry, activeFilters, queryText, dimension)),
      )
    }
    return resolved
  }, [entries, activeFilters, queryText])

  const visible = useMemo(
    () => entries.filter((entry) => entryMatches(entry, activeFilters, queryText)),
    [entries, activeFilters, queryText],
  )
  const visibleBankIds = useMemo(
    () => new Set(visible.filter((entry) => entry.kind === 'bank').map((entry) => entry.id)),
    [visible],
  )
  const visibleNoteIds = useMemo(
    () => new Set(visible.filter((entry) => entry.kind === 'note').map((entry) => entry.id)),
    [visible],
  )

  const visibleBanks = publicBanks.filter((bank) => visibleBankIds.has(bank.id))
  const visibleNotes = publicNotes.filter((note) => visibleNoteIds.has(note.id))

  const loaded = banksQuery.data !== undefined && notesQuery.data !== undefined
  const isEmpty = publicBanks.length === 0 && publicNotes.length === 0
  const hasFilters =
    yearFilter !== ALL ||
    semesterFilter !== ALL ||
    subjectFilter !== ALL ||
    chapterFilter !== ALL ||
    queryText.length > 0
  const noMatches =
    loaded && !isEmpty && hasFilters && visibleBanks.length === 0 && visibleNotes.length === 0

  function clearFilters() {
    setYearFilter(ALL)
    setSemesterFilter(ALL)
    setSubjectFilter(ALL)
    setChapterFilter(ALL)
    setQuery('')
  }

  return (
    <div>
      <header className="page-header">
        <div className="page-heading">
          <p className="eyebrow">发现</p>
          <h1>公开内容</h1>
        </div>
      </header>

      {banksQuery.isPending || notesQuery.isPending ? <Loading /> : null}
      {banksQuery.isError ? (
        <ErrorNote message={`题库：${errorMessage(banksQuery.error)}`} />
      ) : null}
      {notesQuery.isError ? (
        <ErrorNote message={`讲义：${errorMessage(notesQuery.error)}`} />
      ) : null}

      <div className="public-filters">
        <div className="search-field public-search">
          <Icon name="search" size={16} className="search-field-icon" />
          <input
            type="search"
            className="input search-input"
            placeholder="搜索题库、讲义、学年或章节…"
            aria-label="搜索公开内容"
            value={query}
            onChange={(event) => setQuery(event.target.value)}
          />
          {query ? (
            <button
              type="button"
              className="search-clear"
              aria-label="清除搜索"
              onClick={() => setQuery('')}
            >
              <Icon name="x" size={14} />
            </button>
          ) : null}
        </div>

        <div className="public-filter-grid">
          <label className="field public-filter-field">
            <span className="label">年</span>
            <select
              className="select"
              value={activeYear}
              onChange={(event) => setYearFilter(event.target.value)}
            >
              <option value={ALL}>全部</option>
              {options.year.choices.map((choice) => (
                <option key={choice.value} value={choice.value}>
                  {choice.label}
                </option>
              ))}
              {options.year.blank ? (
                <option value={options.year.blank.value}>{options.year.blank.label}</option>
              ) : null}
            </select>
          </label>

          <label className="field public-filter-field">
            <span className="label">学期</span>
            <select
              className="select"
              value={activeSemester}
              onChange={(event) => setSemesterFilter(event.target.value)}
            >
              <option value={ALL}>全部</option>
              {options.semester.choices.map((choice) => (
                <option key={choice.value} value={choice.value}>
                  {choice.label}
                </option>
              ))}
              {options.semester.blank ? (
                <option value={options.semester.blank.value}>
                  {options.semester.blank.label}
                </option>
              ) : null}
            </select>
          </label>

          <label className="field public-filter-field">
            <span className="label">学科</span>
            <select
              className="select"
              value={activeSubject}
              onChange={(event) => setSubjectFilter(event.target.value)}
            >
              <option value={ALL}>全部</option>
              {options.subject.choices.map((choice) => (
                <option key={choice.value} value={choice.value}>
                  {choice.label}
                </option>
              ))}
              {options.subject.blank ? (
                <option value={options.subject.blank.value}>{options.subject.blank.label}</option>
              ) : null}
            </select>
          </label>

          <label className="field public-filter-field">
            <span className="label">包含章</span>
            <select
              className="select"
              value={activeChapter}
              onChange={(event) => setChapterFilter(event.target.value)}
            >
              <option value={ALL}>全部</option>
              {options.chapter.choices.map((choice) => (
                <option key={choice.value} value={choice.value}>
                  {choice.label}
                </option>
              ))}
              {options.chapter.blank ? (
                <option value={options.chapter.blank.value}>{options.chapter.blank.label}</option>
              ) : null}
            </select>
          </label>
        </div>

        {hasFilters ? (
          <div className="public-filter-actions">
            <button type="button" className="btn btn-small btn-ghost" onClick={clearFilters}>
              清除筛选
            </button>
          </div>
        ) : null}
      </div>

      {visibleBanks.length > 0 ? (
        <section className="public-section" aria-labelledby="public-banks-heading">
          <div className="section-head">
            <h2 className="section-title" id="public-banks-heading">
              公开题库
            </h2>
            <span className="muted">{visibleBanks.length} 个</span>
          </div>
          <div className="bank-grid">
            {visibleBanks.map((bank) => {
              const term = termLabel(bank.year, bank.semester)
              return (
                <Link key={bank.id} to={`/banks/${bank.id}`} className="bank-card">
                  <div className="bank-card-top">
                    <h2>{bank.name}</h2>
                    <span className="badge">公开</span>
                  </div>
                  <div className="bank-stats">
                    <span className="bank-stat">
                      <strong>{bank.questionCount}</strong>
                      <span>道题</span>
                    </span>
                    <span className="bank-owner">
                      {bank.ownerId === user?.id ? '我创建的' : '同学分享'}
                    </span>
                  </div>
                  <p className="bank-desc">{bank.description || '暂无简介'}</p>
                  <TagPills tags={bank.tags ?? []} max={3} />
                  {term ? <span className="public-term">{term}</span> : null}
                  <div className="bank-meta">
                    <span className="bank-created">创建于 {formatDate(bank.createdAt)}</span>
                    <Icon name="arrow-left" size={14} className="bank-open-icon" />
                  </div>
                </Link>
              )
            })}
          </div>
        </section>
      ) : null}

      {visibleNotes.length > 0 ? (
        <section className="public-section" aria-labelledby="public-notes-heading">
          <div className="section-head">
            <h2 className="section-title" id="public-notes-heading">
              公开讲义
            </h2>
            <span className="muted">{visibleNotes.length} 份</span>
          </div>
          <div className="notes-grid">
            {visibleNotes.map((note) => {
              const term = termLabel(note.year, note.semester)
              return (
                <Link key={note.id} to={`/notes/${note.id}`} className="note-list-card">
                  <h2>{note.title}</h2>
                  {note.subtitle ? <p className="note-list-sub">{note.subtitle}</p> : null}
                  <TagPills tags={note.tags ?? []} max={4} />
                  <div className="note-list-foot">
                    {term ? <span>{term}</span> : null}
                    {note.sectionCount !== undefined ? (
                      <span>{note.sectionCount} 节</span>
                    ) : null}
                    {note.updatedAt ? <span>更新于 {formatDate(note.updatedAt)}</span> : null}
                  </div>
                </Link>
              )
            })}
          </div>
        </section>
      ) : null}

      {loaded && isEmpty ? (
        <EmptyState
          title="还没有公开内容"
          hint="有题库或讲义的同学可以在设置里把它公开，所有人就能在这里看到。"
        />
      ) : null}

      {noMatches ? (
        <EmptyState title="没有匹配的公开内容" hint="换个关键词，或者清除筛选再看看。">
          <button type="button" className="btn btn-small btn-ghost" onClick={clearFilters}>
            清除筛选
          </button>
        </EmptyState>
      ) : null}
    </div>
  )
}
