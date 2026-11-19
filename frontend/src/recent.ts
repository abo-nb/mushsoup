import { useEffect } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { api } from './api'

const DEDUPE_MS = 60_000
const seen = new Map<string, number>()

/**
 * 打开题库或讲义时记一笔，供「最近」页的常用入口排序。
 * 上报失败不影响页面，React 严格模式下重复挂载也不会重复计数。
 */
export function useRecordRecent(kind: 'bank' | 'note', itemId: string | null | undefined) {
  const queryClient = useQueryClient()

  useEffect(() => {
    if (!itemId) return

    const key = `${kind}:${itemId}`
    const now = Date.now()
    const previous = seen.get(key)
    if (previous !== undefined && now - previous < DEDUPE_MS) return

    if (seen.size > 200) {
      for (const [entry, at] of seen) {
        if (now - at >= DEDUPE_MS) seen.delete(entry)
      }
    }

    seen.set(key, now)
    void api
      .recordRecent(kind, itemId)
      .then(() => queryClient.invalidateQueries({ queryKey: ['recents'] }))
      .catch(() => {
        seen.delete(key)
      })
  }, [kind, itemId, queryClient])
}
