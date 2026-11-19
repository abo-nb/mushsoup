/**
 * 章节、学年这类标签的自然排序：数字段按数值比较，其余按中文排序。
 * 支持 `1-1` / `1.10` / `CH 02` / `第一章` / `第十二章` / `附錄 A` 等写法，
 * 分隔符不限（`-`、`.`、`_`、空格、`、`、全角符号都行）。
 */

const CN_DIGITS: Record<string, number> = {
  零: 0,
  〇: 0,
  一: 1,
  二: 2,
  两: 2,
  三: 3,
  四: 4,
  五: 5,
  六: 6,
  七: 7,
  八: 8,
  九: 9,
}

const CN_UNITS: Record<string, number> = { 十: 10, 百: 100, 千: 1000 }

const CN_CHARS = Object.keys(CN_DIGITS).join('') + Object.keys(CN_UNITS).join('')

interface Token {
  text: string
  value: number | null
}

function normalize(source: string): string {
  // 全角数字转半角，避免 `１-１０` 被当成纯文本。
  return source.replace(/[０-９]/g, (char) => String.fromCharCode(char.charCodeAt(0) - 0xfee0))
}

function parseChinese(source: string): number | null {
  let total = 0
  let current = 0
  let seen = false

  for (const char of source) {
    const digit = CN_DIGITS[char]
    if (digit !== undefined) {
      current = digit
      seen = true
      continue
    }

    const unit = CN_UNITS[char]
    if (unit !== undefined) {
      total += (current || 1) * unit
      current = 0
      seen = true
      continue
    }

    return null
  }

  return seen ? total + current : null
}

function tokenize(source: string): Token[] {
  const text = normalize(source)
  // 点号在章节里通常也是分隔符（`1.2` 表示第二节），所以只按整数切分。
  const pattern = new RegExp(`(\\d+)|([${CN_CHARS}]+)`, 'g')
  const tokens: Token[] = []
  let index = 0

  for (const match of text.matchAll(pattern)) {
    const start = match.index ?? 0
    if (start > index) {
      tokens.push({ text: text.slice(index, start), value: null })
    }

    const raw = match[0]
    const arabic = Number(raw.replace('．', '.'))
    tokens.push({ text: raw, value: Number.isNaN(arabic) ? parseChinese(raw) : arabic })
    index = start + raw.length
  }

  if (index < text.length) {
    tokens.push({ text: text.slice(index), value: null })
  }

  return tokens
}

export function naturalCompare(left: string, right: string): number {
  if (left === right) return 0

  const a = tokenize(left)
  const b = tokenize(right)
  const shared = Math.min(a.length, b.length)

  for (let i = 0; i < shared; i += 1) {
    const x = a[i]
    const y = b[i]

    if (x.value !== null && y.value !== null) {
      if (x.value !== y.value) return x.value - y.value
      continue
    }

    // 带数字的段排在同位置的纯文本前面：「第一章」先于「附录」。
    if (x.value !== null) return -1
    if (y.value !== null) return 1

    const compared = x.text.localeCompare(y.text, 'zh-CN')
    if (compared !== 0) return compared
  }

  if (a.length !== b.length) return a.length - b.length
  return left.localeCompare(right, 'zh-CN')
}

export function sortNatural(values: string[]): string[] {
  return [...values].sort(naturalCompare)
}
