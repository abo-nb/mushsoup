import { useEffect, useRef, useState } from 'react'
import { api, errorMessage } from '../api'
import { Icon } from './Icon'

interface ShareButtonProps {
  kind: 'bank' | 'note'
  targetId: string
  disabled?: boolean
}

/** 生成（或复用）短链接并复制到剪贴板。失败时把错误直接说清楚。 */
export function ShareButton({ kind, targetId, disabled }: ShareButtonProps) {
  const [state, setState] = useState<'idle' | 'working' | 'copied' | 'error'>('idle')
  const [message, setMessage] = useState('')
  const timer = useRef<number | undefined>(undefined)

  useEffect(() => () => window.clearTimeout(timer.current), [])

  function settle(next: 'copied' | 'error', text: string) {
    setState(next)
    setMessage(text)
    window.clearTimeout(timer.current)
    timer.current = window.setTimeout(() => {
      setState('idle')
      setMessage('')
    }, 2600)
  }

  async function handleClick() {
    if (state === 'working' || disabled) return
    setState('working')
    try {
      const link = await api.createLink(kind, targetId)
      const url = `${window.location.origin}/s/${link.code}`
      let copied = false
      try {
        await navigator.clipboard.writeText(url)
        copied = true
      } catch {
        copied = false
      }

      if (copied) {
        settle('copied', '短链接已复制')
      } else {
        window.prompt('复制这个短链接', url)
        settle('copied', '短链接已生成')
      }
    } catch (error) {
      settle('error', errorMessage(error))
    }
  }

  return (
    <span className="share-control">
      <button
        type="button"
        className="btn btn-small btn-ghost"
        onClick={() => void handleClick()}
        disabled={disabled || state === 'working'}
      >
        <Icon name="globe" size={15} />
        {state === 'working' ? '正在生成…' : state === 'copied' ? '已复制' : '分享链接'}
      </button>
      {message ? (
        <span
          className={state === 'error' ? 'share-hint is-error' : 'share-hint'}
          role="status"
          aria-live="polite"
        >
          {message}
        </span>
      ) : null}
    </span>
  )
}
