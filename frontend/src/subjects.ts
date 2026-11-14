export interface Subject {
  id: string
  label: string
  aliases: string[]
}

/** 公开内容必须至少带一个学科标签。alias 让「數學」和「数学」视为同一科。 */
export const SUBJECTS: Subject[] = [
  { id: 'chinese', label: '语文', aliases: ['语文', '語文', '国文', '國文', '中文'] },
  { id: 'english', label: '英文', aliases: ['英文', '英语', '英語', 'english'] },
  { id: 'math', label: '数学', aliases: ['数学', '數學', 'math', 'maths'] },
  { id: 'physics', label: '物理', aliases: ['物理', 'physics'] },
  { id: 'chemistry', label: '化学', aliases: ['化学', '化學', 'chemistry'] },
  { id: 'biology', label: '生物', aliases: ['生物', 'biology'] },
  { id: 'history', label: '历史', aliases: ['历史', '歷史', 'history'] },
  { id: 'geography', label: '地理', aliases: ['地理', 'geography'] },
  { id: 'civics', label: '公民', aliases: ['公民', 'civics'] },
  { id: 'management', label: '企管', aliases: ['企管', '企业管理', '企業管理', 'management'] },
  { id: 'media', label: '多媒体', aliases: ['多媒体', '多媒體', 'multimedia'] },
  { id: 'programming', label: '编程', aliases: ['编程', '程式設計', '程式设计', 'programming'] },
  { id: 'economics', label: '经济', aliases: ['经济', '經濟', 'economics'] },
  { id: 'other', label: '其他', aliases: ['其他', '其它', 'other'] },
]

const SUBJECT_BY_LABEL = new Map<string, Subject>()
for (const subject of SUBJECTS) {
  for (const alias of subject.aliases) {
    SUBJECT_BY_LABEL.set(alias.toLowerCase(), subject)
  }
}

function key(tag: string): string {
  return tag.trim().toLowerCase()
}

export function canonicalSubject(tag: string): Subject | null {
  return SUBJECT_BY_LABEL.get(key(tag)) ?? null
}

/** 匹配用的规范键：认识的学科收敛到学科 id，其余按小写原文。 */
export function subjectKey(tag: string): string {
  const subject = canonicalSubject(tag)
  return subject ? `subject:${subject.id}` : `tag:${key(tag)}`
}

export function matchesSubject(tag: string, subjectId: string): boolean {
  const subject = canonicalSubject(tag)
  return subject?.id === subjectId
}

/** 显示用：认识的学科显示成统一简体标签，其余保留用户原文。 */
export function subjectLabel(tag: string): string {
  return canonicalSubject(tag)?.label ?? tag.trim()
}

export function dedupeTags(tags: string[]): string[] {
  const seen = new Set<string>()
  const result: string[] = []
  for (const tag of tags) {
    const trimmed = tag.trim()
    if (!trimmed) continue
    const k = subjectKey(trimmed)
    if (seen.has(k)) continue
    seen.add(k)
    result.push(trimmed)
  }
  return result
}
