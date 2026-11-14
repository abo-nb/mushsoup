type AvatarProps = {
  name: string
  src?: string | null
  size?: 'sm' | 'md'
}

export function Avatar({ name, src, size = 'md' }: AvatarProps) {
  const initial = Array.from(name.trim())[0]?.toUpperCase() ?? '?'
  const className = `avatar avatar-${size}`

  if (src) {
    return <img className={className} src={src} alt="" loading="lazy" />
  }

  return (
    <span className={`${className} avatar-fallback`} aria-hidden="true">
      {initial}
    </span>
  )
}
