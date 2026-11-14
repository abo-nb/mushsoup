export const OPTION_LABELS = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ'.split('')

export const SHUFFLE_LABELS: Record<string, string> = {
  follow: '跟随题库',
  always: '总是乱序',
  never: '从不乱序',
}

export const SHUFFLE_HINTS: Record<string, string> = {
  follow: '使用题库的默认设置',
  always: '每次练习都打乱选项顺序',
  never: '保持题目中选项的顺序',
}

export const SUBMIT_LABELS: Record<string, string> = {
  space: '按空格提交',
  auto: '选中即提交',
}

export const SUBMIT_HINTS: Record<string, string> = {
  space: '选好答案后按空格提交，多选题也适用',
  auto: '单选题按数字或点选项后立即提交，多选题仍用 Enter',
}

export const TYPE_LABELS: Record<string, string> = {
  single_choice: '单选题',
  multiple_choice: '多选题',
}

export function shuffleLabel(value: string | null | undefined): string {
  return (value && SHUFFLE_LABELS[value]) || SHUFFLE_LABELS.follow
}

export function typeLabel(value: string | null | undefined): string {
  return (value && TYPE_LABELS[value]) || '题目'
}

export function formatDate(value: string | null | undefined): string {
  if (!value) return ''
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return ''
  return new Intl.DateTimeFormat('zh-CN', {
    year: 'numeric',
    month: 'numeric',
    day: 'numeric',
  }).format(date)
}

export function previewText(markdown: string, limit = 96): string {
  const plain = markdown
    .replace(/```[\s\S]*?```/g, ' ')
    .replace(/!\[[^\]]*\]\([^)]*\)/g, ' ')
    .replace(/\[([^\]]*)\]\([^)]*\)/g, '$1')
    .replace(/[#>*_`~$]/g, '')
    .replace(/\s+/g, ' ')
    .trim()
  return plain.length > limit ? `${plain.slice(0, limit)}…` : plain
}

export function parseTags(value: string): string[] {
  const seen = new Set<string>()
  const tags: string[] = []
  for (const part of value.split(/[,，、]/)) {
    const tag = part.trim()
    if (tag && !seen.has(tag)) {
      seen.add(tag)
      tags.push(tag)
    }
  }
  return tags
}
