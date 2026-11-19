/**
 * 章节快速选择：把「1-1 ~ 4-5」这类范围写法解析成章节 key 集合。
 * 只做顺序比较，不依赖分隔符，所以 1-1、1.1、第一章/第一节 都能匹配。
 */

export interface ChapterMinorLike {
  name: string
  key: string
  count: number
}

export interface ChapterBranchLike {
  name: string
  key: string
  count: number
  directCount: number
  minors: ReadonlyArray<ChapterMinorLike>
}

export interface ChapterLeaf {
  major: string
  minor: string | null
  key: string
  count: number
  tokens: number[]
}

const CHINESE_DIGITS: Record<string, number> = {
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

const NUMERAL_RUN = /[0-9０-９]+|[零〇一二三四五六七八九十百千两]+/g
const RANGE_SEPARATOR = /\s*(?:~|～|至|到|→)\s*/

function parseChineseNumeral(text: string): number {
  let total = 0
  let current = 0
  for (const char of text) {
    if (char === '十') {
      total += (current || 1) * 10
      current = 0
    } else if (char === '百') {
      total += (current || 1) * 100
      current = 0
    } else if (char === '千') {
      total += (current || 1) * 1000
      current = 0
    } else if (char in CHINESE_DIGITS) {
      current = CHINESE_DIGITS[char]
    }
  }
  return total + current
}

/** 抽取名字里的编号：`1-1` → [1, 1]，`第二十三章` → [23]，`CH 02` → [2]。 */
export function chapterNumbers(name: string): number[] {
  const numbers: number[] = []
  for (const match of name.matchAll(NUMERAL_RUN)) {
    const run = match[0]
    if (/^[0-9０-９]+$/.test(run)) {
      const half = run.replace(/[０-９]/g, (char) =>
        String.fromCharCode(char.charCodeAt(0) - 0xfee0),
      )
      numbers.push(Number(half))
    } else {
      numbers.push(parseChineseNumeral(run))
    }
  }
  return numbers
}

/**
 * 小章自带完整编号（`2-1`）时不要重复拼大章编号，否则 `2-1` 会落到 `[2,2,1]`。
 * 只有编号本身是多段（`1-1`）且首段和大章一致时才视为完整路径。
 */
function combineTokens(major: number[], minor: number[]): number[] {
  if (minor.length === 0) return major
  if (major.length === 0) return minor
  if (minor.length >= 2 && minor[0] === major[0]) return minor
  return [...major, ...minor]
}

const ORDINAL_MARKER = /第\s*[0-9０-９零〇一二三四五六七八九十百千两]+\s*小?[章节節篇課课部]/g

function nameKey(name: string): string {
  return name
    .replace(ORDINAL_MARKER, '')
    .replace(NUMERAL_RUN, '')
    .replace(/[\s\-_.,，、·()（）\[\]【】]+/g, '')
    .toLowerCase()
}

export function buildChapterLeaves(branches: ReadonlyArray<ChapterBranchLike>): ChapterLeaf[] {
  const leaves: ChapterLeaf[] = []
  for (const branch of branches) {
    const majorTokens = chapterNumbers(branch.name)
    if (branch.minors.length === 0) {
      leaves.push({
        major: branch.name,
        minor: null,
        key: branch.key,
        count: branch.count,
        tokens: majorTokens,
      })
      continue
    }
    for (const minor of branch.minors) {
      leaves.push({
        major: branch.name,
        minor: minor.name,
        key: minor.key,
        count: minor.count,
        tokens: combineTokens(majorTokens, chapterNumbers(minor.name)),
      })
    }
  }
  return leaves
}

/** 叶子的编号写法，用来生成示范范围：`1-1`、`12-4`。 */
export function leafNumberLabel(leaf: ChapterLeaf): string {
  return leaf.tokens.join('-')
}

function startsWithTokens(tokens: number[], prefix: number[]): boolean {
  if (prefix.length > tokens.length) return false
  return prefix.every((value, index) => tokens[index] === value)
}

interface Resolved {
  first: number
  last: number
}

function resolveEndpoint(
  token: string,
  leaves: ReadonlyArray<ChapterLeaf>,
  branches: ReadonlyArray<ChapterBranchLike>,
): Resolved | null {
  const clean = token.trim().replace(/\s+/g, ' ')
  if (!clean) return null

  const wantedName = nameKey(clean)
  const branch = branches.find(
    (item) => item.name === clean || (wantedName !== '' && nameKey(item.name) === wantedName),
  )
  if (branch) {
    const indices = leaves.flatMap((leaf, index) => (leaf.major === branch.name ? [index] : []))
    if (indices.length > 0) {
      return { first: indices[0], last: indices[indices.length - 1] }
    }
  }

  const minorIndices = leaves.flatMap((leaf, index) =>
    leaf.minor !== null &&
    (leaf.minor === clean || (wantedName !== '' && nameKey(leaf.minor) === wantedName))
      ? [index]
      : [],
  )
  if (minorIndices.length > 0) {
    return { first: minorIndices[0], last: minorIndices[minorIndices.length - 1] }
  }

  const numbers = chapterNumbers(clean)
  if (numbers.length > 0) {
    const indices = leaves.flatMap((leaf, index) =>
      startsWithTokens(leaf.tokens, numbers) ? [index] : [],
    )
    if (indices.length > 0) {
      return { first: indices[0], last: indices[indices.length - 1] }
    }
  }

  return null
}

function collectKeys(
  leaves: ReadonlyArray<ChapterLeaf>,
  branches: ReadonlyArray<ChapterBranchLike>,
  from: number,
  to: number,
): Set<string> {
  const keys = new Set<string>()
  for (let index = from; index <= to; index += 1) keys.add(leaves[index].key)

  // 整章都选中时把「直属大章」的题目一并纳入，和逐个小章勾选的行为一致。
  for (const branch of branches) {
    if (branch.directCount === 0 || branch.minors.length === 0) continue
    if (branch.minors.every((minor) => keys.has(minor.key))) keys.add(branch.key)
  }
  return keys
}

export type RangeResult = { keys: Set<string> } | { error: string }

export function resolveChapterRange(
  input: string,
  branches: ReadonlyArray<ChapterBranchLike>,
): RangeResult {
  const leaves = buildChapterLeaves(branches)
  const text = input.trim()
  if (!text) return { error: '先输入要选的章节，例如 1-1 ~ 4-5。' }
  if (leaves.length === 0) return { error: '这个题库还没有带章节的题目。' }

  const parts = text.split(RANGE_SEPARATOR).filter((part) => part.trim() !== '')
  if (parts.length === 0 || parts.length > 2) {
    return { error: `没看懂「${text}」，试试 1-1 ~ 4-5 或直接写大章名称。` }
  }

  const start = resolveEndpoint(parts[0], leaves, branches)
  if (!start) {
    return { error: `没找到「${parts[0].trim()}」，试试 1-1 ~ 4-5 或直接写大章名称。` }
  }

  let from = start.first
  let to = start.last
  if (parts.length === 2) {
    const end = resolveEndpoint(parts[1], leaves, branches)
    if (!end) {
      return { error: `没找到「${parts[1].trim()}」，试试 1-1 ~ 4-5 或直接写大章名称。` }
    }
    from = Math.min(start.first, end.first)
    to = Math.max(start.last, end.last)
  }

  return { keys: collectKeys(leaves, branches, from, to) }
}
