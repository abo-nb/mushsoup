import { useEffect, useRef, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, errorMessage } from '../api'
import { Icon } from './Icon'

interface FavoriteButtonProps {
  kind: 'bank' | 'note'
  itemId: string
  /** 卡片右下角只有一个星形图标时用。 */
  compact?: boolean
}

/** 收藏 / 取消收藏公开内容；状态存在账号里，换设备也在。 */
export function FavoriteButton({ kind, itemId, compact = false }: FavoriteButtonProps) {
  const queryClient = useQueryClient()
  const [error, setError] = useState('')
  const timer = useRef<number | undefined>(undefined)

  const favoritesQuery = useQuery({
    queryKey: ['favorites'],
    queryFn: api.favorites,
    staleTime: 60_000,
  })

  const favorited =
    favoritesQuery.data?.some((item) => item.kind === kind && item.itemId === itemId) ?? false

  const mutation = useMutation({
    mutationFn: () =>
      favorited ? api.removeFavorite(kind, itemId) : api.addFavorite(kind, itemId),
    onSuccess: () => {
      setError('')
      void queryClient.invalidateQueries({ queryKey: ['favorites'] })
    },
    onError: (cause) => {
      setError(errorMessage(cause))
      window.clearTimeout(timer.current)
      timer.current = window.setTimeout(() => setError(''), 3200)
    },
  })

  useEffect(() => () => window.clearTimeout(timer.current), [])

  if (compact) {
    return (
      <button
        type="button"
        className={favorited ? 'card-favorite is-favorited' : 'card-favorite'}
        aria-pressed={favorited}
        aria-label={favorited ? '取消收藏' : '收藏'}
        title={error || (favorited ? '取消收藏' : '收藏')}
        onClick={(event) => {
          event.preventDefault()
          event.stopPropagation()
          mutation.mutate()
        }}
        disabled={mutation.isPending || favoritesQuery.isPending}
      >
        <Icon name="star" size={18} />
      </button>
    )
  }

  return (
    <span className="favorite-control">
      <button
        type="button"
        className={favorited ? 'btn btn-small btn-ghost is-favorited' : 'btn btn-small btn-ghost'}
        aria-pressed={favorited}
        title={favorited ? '取消收藏' : '收藏'}
        onClick={() => mutation.mutate()}
        disabled={mutation.isPending || favoritesQuery.isPending}
      >
        <Icon name="star" size={15} />
        {mutation.isPending ? '保存中…' : favorited ? '已收藏' : '收藏'}
      </button>
      {error ? (
        <span className="favorite-hint is-error" role="status" aria-live="polite">
          {error}
        </span>
      ) : null}
    </span>
  )
}
