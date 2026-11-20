import type { IconName } from './components/Icon'

export type NavKey =
  | 'recent'
  | 'banks'
  | 'notes'
  | 'public'
  | 'leaderboard'
  | 'favorites'
  | 'settings'

export interface NavItem {
  key: NavKey
  label: string
  path: string
  icon: IconName
}

export interface NavConfig {
  order: NavKey[]
  pinned: NavKey[]
}

export const NAV_ITEMS: NavItem[] = [
  { key: 'recent', label: '最近', path: '/', icon: 'clock' },
  { key: 'banks', label: '题库', path: '/banks', icon: 'book' },
  { key: 'notes', label: '讲义', path: '/notes', icon: 'document' },
  { key: 'public', label: '公开', path: '/public', icon: 'globe' },
  { key: 'leaderboard', label: '排行', path: '/leaderboard', icon: 'trophy' },
  { key: 'favorites', label: '收藏', path: '/favorites', icon: 'star' },
  { key: 'settings', label: '设置', path: '/settings', icon: 'settings' },
]

export const NAV_ITEMS_BY_KEY = new Map(NAV_ITEMS.map((item) => [item.key, item]))

export const MAX_PINNED = 5
export const NAV_STORAGE_KEY = 'ms_nav'

const DEFAULT_ORDER: NavKey[] = [
  'recent',
  'banks',
  'notes',
  'public',
  'favorites',
  'leaderboard',
  'settings',
]

const DEFAULT_PINNED: NavKey[] = ['recent', 'banks', 'notes', 'public', 'favorites']

function isNavKey(value: unknown): value is NavKey {
  return typeof value === 'string' && NAV_ITEMS_BY_KEY.has(value as NavKey)
}

/** 补齐缺失项、去掉未知项和重复项；底栏最多 MAX_PINNED 项。 */
export function normalizeNavConfig(raw: unknown): NavConfig {
  const source = (raw ?? {}) as { order?: unknown; pinned?: unknown }
  const order: NavKey[] = []
  if (Array.isArray(source.order)) {
    for (const value of source.order) {
      if (isNavKey(value) && !order.includes(value)) order.push(value)
    }
  }
  for (const key of DEFAULT_ORDER) {
    if (!order.includes(key)) order.push(key)
  }

  const pinned: NavKey[] = []
  if (Array.isArray(source.pinned)) {
    for (const value of source.pinned) {
      if (isNavKey(value) && order.includes(value) && !pinned.includes(value)) {
        pinned.push(value)
      }
    }
  }
  return { order, pinned: pinned.slice(0, MAX_PINNED) }
}

export const DEFAULT_NAV_CONFIG: NavConfig = {
  order: [...DEFAULT_ORDER],
  pinned: [...DEFAULT_PINNED],
}

function loadNavConfig(): NavConfig {
  try {
    const stored = window.localStorage.getItem(NAV_STORAGE_KEY)
    return stored ? normalizeNavConfig(JSON.parse(stored)) : DEFAULT_NAV_CONFIG
  } catch {
    return DEFAULT_NAV_CONFIG
  }
}

let current = typeof window === 'undefined' ? DEFAULT_NAV_CONFIG : loadNavConfig()
const listeners = new Set<() => void>()

function notify() {
  for (const listener of listeners) listener()
}

export function subscribeNav(listener: () => void): () => void {
  listeners.add(listener)
  return () => {
    listeners.delete(listener)
  }
}

export function getNavConfig(): NavConfig {
  return current
}

export function setNavConfig(next: NavConfig) {
  current = normalizeNavConfig(next)
  try {
    window.localStorage.setItem(NAV_STORAGE_KEY, JSON.stringify(current))
  } catch {
    // 隐私模式下写入会失败，本次修改仍然生效。
  }
  notify()
}

export function resetNavConfig() {
  setNavConfig(DEFAULT_NAV_CONFIG)
}

if (typeof window !== 'undefined') {
  window.addEventListener('storage', (event) => {
    if (event.key !== NAV_STORAGE_KEY) return
    current = loadNavConfig()
    notify()
  })
}
