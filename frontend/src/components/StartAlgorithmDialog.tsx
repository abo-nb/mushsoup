import { useEffect, useRef, type MouseEvent } from 'react'
import { ALGORITHM_OPTIONS, type MemoryAlgorithm } from '../algorithms'

interface StartAlgorithmDialogProps {
  open: boolean
  algorithm: MemoryAlgorithm
  /** 这次练习的规模，例如「每日练习 · 最多 20 题」。 */
  context?: string
  onCancel: () => void
  onChangeAlgorithm: (algorithm: MemoryAlgorithm) => void
  onConfirm: () => void
}

/** 开始练习前选算法；Esc、点背景或「取消」都只是关掉，不会开始。 */
export function StartAlgorithmDialog({
  open,
  algorithm,
  context,
  onCancel,
  onChangeAlgorithm,
  onConfirm,
}: StartAlgorithmDialogProps) {
  const dialogRef = useRef<HTMLDialogElement>(null)

  useEffect(() => {
    const dialog = dialogRef.current
    if (!dialog) return

    if (open && !dialog.open) {
      dialog.showModal()
    } else if (!open && dialog.open) {
      dialog.close()
    }
  }, [open])

  useEffect(() => {
    const dialog = dialogRef.current
    if (!dialog) return

    function handleCancel(event: Event) {
      event.preventDefault()
      onCancel()
    }

    dialog.addEventListener('cancel', handleCancel)
    return () => dialog.removeEventListener('cancel', handleCancel)
  }, [onCancel])

  function handleBackdrop(event: MouseEvent<HTMLDialogElement>) {
    if (event.target === dialogRef.current) onCancel()
  }

  return (
    <dialog
      ref={dialogRef}
      className="algorithm-dialog"
      aria-labelledby="algorithm-dialog-title"
      onClick={handleBackdrop}
    >
      <form
        className="algorithm-dialog-form"
        onSubmit={(event) => {
          event.preventDefault()
          onConfirm()
        }}
      >
        <header className="algorithm-dialog-head">
          <p className="eyebrow">开始练习</p>
          <h2 id="algorithm-dialog-title">选择记忆算法</h2>
          {context ? <p className="muted algorithm-dialog-context">{context}</p> : null}
        </header>

        <div className="radio-cards" role="radiogroup" aria-labelledby="algorithm-dialog-title">
          {ALGORITHM_OPTIONS.map((option) => (
            <label
              key={option.value}
              className={algorithm === option.value ? 'radio-card selected' : 'radio-card'}
            >
              <input
                type="radio"
                name="start-algorithm"
                value={option.value}
                checked={algorithm === option.value}
                onChange={() => onChangeAlgorithm(option.value)}
              />
              <span className="radio-card-text">
                <strong>{option.label}</strong>
                <small>{option.hint}</small>
              </span>
            </label>
          ))}
        </div>

        <div className="form-actions algorithm-dialog-actions">
          <button type="button" className="btn btn-ghost" onClick={onCancel}>
            取消
          </button>
          <button type="submit" className="btn btn-primary">
            开始
          </button>
        </div>
      </form>
    </dialog>
  )
}
