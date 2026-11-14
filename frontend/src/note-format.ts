import type { NoteBlock, NoteDocument } from './api'

const KNOWN_BLOCK_TYPES = [
  'card',
  'heading',
  'paragraph',
  'list',
  'table',
  'flow',
  'callout',
  'quote',
  'grid',
  'quickGroup',
  'figure',
  'summary',
  'chips',
] as const

const BLOCK_TYPE_SET = new Set<string>(KNOWN_BLOCK_TYPES)
const MAX_ERRORS = 12

export function isKnownBlockType(type: string): boolean {
  return BLOCK_TYPE_SET.has(type)
}

export function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

export function asString(value: unknown): string {
  return typeof value === 'string' ? value : ''
}

export function asNumber(value: unknown): number | undefined {
  return typeof value === 'number' && Number.isFinite(value) ? value : undefined
}

export function asBoolean(value: unknown): boolean {
  return value === true
}

export function asStringArray(value: unknown): string[] {
  return Array.isArray(value) ? value.map(asString) : []
}

export function asRecords(value: unknown): Record<string, unknown>[] {
  return Array.isArray(value) ? value.filter(isRecord) : []
}

export function asBlocks(value: unknown): NoteBlock[] {
  if (!Array.isArray(value)) return []
  return value.filter(
    (item): item is NoteBlock => isRecord(item) && typeof item.type === 'string',
  )
}

export function countBlocks(blocks: NoteBlock[]): number {
  let total = 0
  for (const block of blocks) {
    total += 1
    if (block.type === 'card' || block.type === 'summary') {
      total += countBlocks(asBlocks(block.blocks))
    }
    if (block.type === 'grid') {
      for (const item of asRecords(block.items)) {
        total += countBlocks(asBlocks(item.blocks))
      }
    }
  }
  return total
}

export interface NoteParseResult {
  document: NoteDocument | null
  errors: string[]
}

export function parseNoteDocument(text: string): NoteParseResult {
  if (!text.trim()) {
    return { document: null, errors: ['还没有粘贴讲义内容。'] }
  }

  let raw: unknown
  try {
    raw = JSON.parse(text)
  } catch (error) {
    const detail = error instanceof Error ? error.message : '格式不正确'
    return { document: null, errors: [`JSON 解析失败：${detail}`] }
  }

  const errors = validateDocument(raw)
  if (errors.length > 0) {
    return { document: null, errors }
  }
  return { document: raw as NoteDocument, errors: [] }
}

function validateDocument(raw: unknown): string[] {
  const errors: string[] = []
  const push = (message: string) => {
    if (errors.length < MAX_ERRORS) errors.push(message)
  }

  if (!isRecord(raw)) {
    return ['讲义必须是一个 JSON 对象。']
  }

  if (asNumber(raw.formatVersion) !== 1) {
    push('formatVersion 必须为 1。')
  }

  const title = asString(raw.title).trim()
  if (!title) {
    push('title 不能为空。')
  } else if (title.length > 120) {
    push('title 不能超过 120 字。')
  }

  const subtitle = raw.subtitle
  if (subtitle !== undefined && subtitle !== null && typeof subtitle !== 'string') {
    push('subtitle 必须是字符串。')
  } else if (typeof subtitle === 'string' && subtitle.length > 500) {
    push('subtitle 不能超过 500 字。')
  }

  if (
    raw.visibility !== undefined &&
    raw.visibility !== 'public' &&
    raw.visibility !== 'private'
  ) {
    push('visibility 只能是 public 或 private。')
  }

  if (raw.tags !== undefined) {
    if (!Array.isArray(raw.tags)) {
      push('tags 必须是字符串数组。')
    } else {
      if (raw.tags.length > 30) push('tags 不能超过 30 项。')
      raw.tags.forEach((tag, index) => {
        if (typeof tag !== 'string' || !tag.trim()) {
          push(`tags[${index}] 必须是非空字符串。`)
        } else if (tag.length > 40) {
          push(`tags[${index}] 不能超过 40 字。`)
        }
      })
    }
  }

  if (raw.notice !== undefined) {
    if (!isRecord(raw.notice)) {
      push('notice 必须是对象。')
    } else {
      if (!asString(raw.notice.tone).trim()) push('notice.tone 不能为空。')
      if (!asString(raw.notice.text).trim()) push('notice.text 不能为空。')
    }
  }

  if (!Array.isArray(raw.sections)) {
    push('sections 必须是数组。')
    return errors
  }

  if (raw.sections.length < 1) push('至少需要一个 section。')
  if (raw.sections.length > 60) push('sections 不能超过 60 项。')

  const ids = new Set<string>()
  raw.sections.forEach((section, index) => {
    const path = `sections[${index}]`
    if (!isRecord(section)) {
      push(`${path} 必须是对象。`)
      return
    }

    const id = asString(section.id).trim()
    if (!id) {
      push(`${path}.id 不能为空。`)
    } else if (ids.has(id)) {
      push(`${path}.id 与前面的 section 重复：${id}`)
    } else {
      ids.add(id)
    }

    if (!asString(section.title).trim()) {
      push(`${path}.title 不能为空。`)
    }

    validateBlocks(section.blocks, `${path}.blocks`, push)
  })

  return errors
}

function validateBlocks(
  value: unknown,
  path: string,
  push: (message: string) => void,
) {
  if (!Array.isArray(value)) {
    push(`${path} 必须是数组。`)
    return
  }

  value.forEach((block, index) => {
    const blockPath = `${path}[${index}]`
    if (!isRecord(block)) {
      push(`${blockPath} 必须是对象。`)
      return
    }

    const type = asString(block.type)
    if (!type) {
      push(`${blockPath}.type 不能为空。`)
      return
    }

    if (!isKnownBlockType(type)) {
      push(`${blockPath}.type「${type}」不受支持。`)
      return
    }

    switch (type) {
      case 'card':
      case 'summary':
        validateBlocks(block.blocks, `${blockPath}.blocks`, push)
        break
      case 'grid': {
        if (!Array.isArray(block.items)) {
          push(`${blockPath}.items 必须是数组。`)
          break
        }
        block.items.forEach((item, itemIndex) => {
          const itemPath = `${blockPath}.items[${itemIndex}]`
          if (!isRecord(item)) {
            push(`${itemPath} 必须是对象。`)
            return
          }
          validateBlocks(item.blocks, `${itemPath}.blocks`, push)
        })
        break
      }
      case 'list':
      case 'flow':
      case 'quickGroup':
        if (!Array.isArray(block.items)) push(`${blockPath}.items 必须是数组。`)
        break
      case 'table':
        if (!Array.isArray(block.rows)) push(`${blockPath}.rows 必须是数组。`)
        break
      case 'figure':
        if (!asString(block.svg).trim()) push(`${blockPath}.svg 不能为空。`)
        break
      default:
        break
    }
  })
}
