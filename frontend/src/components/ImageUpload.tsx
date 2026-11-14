import { useId, useRef, useState } from 'react'
import type { ChangeEvent } from 'react'

// 与后端 Assets.Contracts 的 AssetDto 对应；字段名以上传接口实际返回为准。
interface AssetDto {
  id: string
  ownerId: string
  fileName: string
  mime: string
  size: number
  url: string
}

interface UploadErrorBody {
  detail?: string
  title?: string
  errors?: Record<string, string[] | string>
}

const MAX_BYTES = 5 * 1024 * 1024

// alt 里不能出现未转义的方括号和圆括号，否则会截断 Markdown 图片语法。
function safeAlt(fileName: string): string {
  const withoutExtension = fileName.replace(/\.[^./\\]+$/, '')
  const cleaned = withoutExtension
    .replace(/[[\]()\\`<>!]/g, ' ')
    .replace(/\s+/g, ' ')
    .trim()
  return cleaned || '图片'
}

function problemDetail(body: UploadErrorBody): string {
  if (body.detail) return body.detail
  const firstFieldError = Object.values(body.errors ?? {})
    .flat()
    .find((message) => Boolean(message))
  return firstFieldError ? String(firstFieldError) : (body.title ?? '')
}

export interface ImageUploadProps {
  onInsert: (markdown: string) => void
  disabled?: boolean
  label?: string
}

export function ImageUpload({ onInsert, disabled = false, label = '上传图片' }: ImageUploadProps) {
  const inputRef = useRef<HTMLInputElement>(null)
  const inputId = useId()
  const errorId = `${inputId}-error`
  const [uploading, setUploading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function upload(file: File) {
    if (!file.type.startsWith('image/')) {
      setError('只能上传图片文件。')
      return
    }
    if (file.size > MAX_BYTES) {
      setError('图片不能超过 5 MB。')
      return
    }

    setError(null)
    setUploading(true)
    try {
      const body = new FormData()
      body.append('file', file)

      const response = await fetch('/api/assets', {
        method: 'POST',
        body,
        credentials: 'same-origin',
      })

      if (!response.ok) {
        let message = ''
        try {
          message = problemDetail((await response.json()) as UploadErrorBody)
        } catch {
          message = ''
        }
        setError(message || `上传失败（${response.status}）`)
        return
      }

      const asset = (await response.json()) as AssetDto
      onInsert(`![${safeAlt(asset.fileName)}](asset://${asset.id})`)
    } catch {
      setError('上传失败，请检查网络后重试。')
    } finally {
      setUploading(false)
    }
  }

  function handleChange(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0]
    // 清空 value，让同一张图可以再次选择。
    event.target.value = ''
    if (file) void upload(file)
  }

  const busy = disabled || uploading

  return (
    <div className="image-upload">
      <input
        ref={inputRef}
        id={inputId}
        type="file"
        accept="image/*"
        hidden
        onChange={handleChange}
        disabled={busy}
      />
      <button
        type="button"
        className="btn btn-small btn-ghost"
        onClick={() => inputRef.current?.click()}
        disabled={busy}
        aria-busy={uploading || undefined}
        aria-describedby={error ? errorId : undefined}
      >
        {uploading ? '上传中…' : label}
      </button>
      {error ? (
        <p id={errorId} className="field-error" role="alert">
          {error}
        </p>
      ) : null}
    </div>
  )
}
