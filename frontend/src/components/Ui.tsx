import type { ReactNode } from 'react'

export function Loading({ label = '加载中…', full = false }: { label?: string; full?: boolean }) {
  return (
    <div
      className={full ? 'page-loading' : 'loading'}
      role="status"
      aria-live="polite"
      aria-busy="true"
    >
      <span className="spinner" aria-hidden="true" />
      <span>{label}</span>
    </div>
  )
}

export function ErrorNote({ message }: { message: string }) {
  return (
    <div className="notice notice-error" role="alert" aria-atomic="true">
      {message}
    </div>
  )
}

export function EmptyState({
  title,
  hint,
  children,
}: {
  title: string
  hint?: string
  children?: ReactNode
}) {
  return (
    <div className="empty-state">
      <p className="empty-title">{title}</p>
      {hint ? <p className="muted">{hint}</p> : null}
      {children}
    </div>
  )
}

export function DifficultyDots({ value }: { value: number }) {
  const level = Math.min(5, Math.max(1, value || 1))
  return (
    <span className="difficulty" aria-label={`难度 ${level} 级`} title={`难度 ${level} / 5`}>
      {[1, 2, 3, 4, 5].map((step) => (
        <span key={step} className={step <= level ? 'dot on' : 'dot'} aria-hidden="true" />
      ))}
    </span>
  )
}
