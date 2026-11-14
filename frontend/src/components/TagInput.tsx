import { useId, useState } from 'react'
import type { KeyboardEvent } from 'react'
import { Icon } from './Icon'
import { SUBJECTS, dedupeTags, subjectLabel } from '../subjects'

export function TagInput({
  value,
  onChange,
  max = 8,
  label = '标签',
  hint,
  disabled = false,
  suggestions = SUBJECTS.map((subject) => subject.label),
}: {
  value: string[]
  onChange: (tags: string[]) => void
  max?: number
  label?: string
  hint?: string
  disabled?: boolean
  suggestions?: string[]
}) {
  const [draft, setDraft] = useState('')
  const listId = useId()

  function add(raw: string) {
    const tags = raw
      .split(/[,，、]/)
      .map((tag) => tag.trim())
      .filter(Boolean)
    if (tags.length === 0) return

    const next = dedupeTags([...value, ...tags]).slice(0, max)
    onChange(next)
    setDraft('')
  }

  function handleKeyDown(event: KeyboardEvent<HTMLInputElement>) {
    if (event.key === 'Enter' || event.key === ',') {
      event.preventDefault()
      add(draft)
      return
    }
    if (event.key === 'Backspace' && !draft && value.length > 0) {
      onChange(value.slice(0, -1))
    }
  }

  return (
    <div className="field">
      <label htmlFor={`${listId}-input`}>{label}</label>
      <div className="tag-input" aria-describedby={hint ? `${listId}-hint` : undefined}>
        {value.length > 0 ? (
          <ul className="tag-input-chips" aria-label="已添加的标签">
            {value.map((tag) => (
              <li key={tag} className="tag-chip">
                <span>{subjectLabel(tag)}</span>
                <button
                  type="button"
                  className="tag-chip-remove"
                  aria-label={`移除 ${subjectLabel(tag)}`}
                  onClick={() => onChange(value.filter((item) => item !== tag))}
                  disabled={disabled}
                >
                  <Icon name="x" size={12} />
                </button>
              </li>
            ))}
          </ul>
        ) : null}
        <input
          id={`${listId}-input`}
          className="tag-input-field"
          type="text"
          list={listId}
          value={draft}
          placeholder={value.length === 0 ? '输入标签后回车，例如 数学' : '继续添加…'}
          onChange={(event) => setDraft(event.target.value)}
          onKeyDown={handleKeyDown}
          onBlur={() => add(draft)}
          disabled={disabled || value.length >= max}
          maxLength={40}
        />
        <datalist id={listId}>
          {suggestions.map((suggestion) => (
            <option key={suggestion} value={suggestion} />
          ))}
        </datalist>
      </div>
      {hint ? (
        <p className="form-hint" id={`${listId}-hint`}>
          {hint}
        </p>
      ) : null}
    </div>
  )
}
