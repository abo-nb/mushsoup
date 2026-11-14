import { useEffect, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import type { NoteBlock } from '../api'
import {
  asBlocks,
  asBoolean,
  asNumber,
  asRecords,
  asString,
  asStringArray,
  isRecord,
} from '../note-format'
import { Icon } from './Icon'
import { MarkdownView } from './MarkdownView'

const CARD_TONES = ['blue', 'teal', 'amber', 'purple'] as const
const CALLOUT_TONES = ['tip', 'warn', 'note', 'example'] as const
const QUICK_TONES = ['blue', 'purple', 'amber', 'red', 'green', 'teal'] as const
const CHIP_TONES = ['red', 'amber', 'green', 'blue', 'navy'] as const

function pickTone(value: unknown, allowed: readonly string[], fallback: string): string {
  const tone = asString(value)
  return allowed.includes(tone) ? tone : fallback
}

export function NoteBlocks({
  blocks,
  idPrefix,
}: {
  blocks: NoteBlock[]
  idPrefix?: string
}) {
  return (
    <div className="note-blocks">
      {blocks.map((block, index) => {
        const anchored = block.type === 'card' || block.type === 'summary'
        return (
          <NoteBlockView
            key={index}
            block={block}
            id={idPrefix && anchored ? `${idPrefix}${index}` : undefined}
          />
        )
      })}
    </div>
  )
}

function NoteBlockView({ block, id }: { block: NoteBlock; id?: string }) {
  switch (block.type) {
    case 'card':
      return <CardBlock block={block} id={id} />
    case 'heading':
      return <HeadingBlock block={block} />
    case 'paragraph':
      return <ParagraphBlock block={block} />
    case 'list':
      return <ListBlock block={block} />
    case 'table':
      return <TableBlock block={block} />
    case 'flow':
      return <FlowBlock block={block} />
    case 'callout':
      return <CalloutBlock block={block} />
    case 'quote':
      return <QuoteBlock block={block} />
    case 'grid':
      return <GridBlock block={block} />
    case 'quickGroup':
      return <QuickGroupBlock block={block} />
    case 'figure':
      return <FigureBlock block={block} />
    case 'summary':
      return <SummaryBlock block={block} id={id} />
    case 'chips':
      return <ChipsBlock block={block} />
    default:
      return null
  }
}

function CardBlock({ block, id }: { block: NoteBlock; id?: string }) {
  const title = asString(block.title)
  const tone = pickTone(block.tone, CARD_TONES, 'blue')

  return (
    <section className="note-card" data-tone={tone} id={id}>
      {title ? (
        <h3 className="note-card-title">
          <MarkdownView source={title} inline />
        </h3>
      ) : null}
      <NoteBlocks blocks={asBlocks(block.blocks)} />
    </section>
  )
}

function HeadingBlock({ block }: { block: NoteBlock }) {
  const text = asString(block.text)
  if (!text) return null

  return (
    <h4 className="note-heading">
      <MarkdownView source={text} inline />
    </h4>
  )
}

function ParagraphBlock({ block }: { block: NoteBlock }) {
  const text = asString(block.text)
  if (!text) return null
  const small = asString(block.size) === 'small'

  return (
    <div className={small ? 'note-para note-para-small' : 'note-para'}>
      <MarkdownView source={text} />
    </div>
  )
}

function ListBlock({ block }: { block: NoteBlock }) {
  const items = asStringArray(block.items).filter((item) => item.trim())
  if (items.length === 0) return null

  const cells = items.map((item, index) => (
    <li key={index}>
      <MarkdownView source={item} inline />
    </li>
  ))

  return asBoolean(block.ordered) ? (
    <ol className="note-list">{cells}</ol>
  ) : (
    <ul className="note-list">{cells}</ul>
  )
}

function TableBlock({ block }: { block: NoteBlock }) {
  const matrix = asString(block.variant) === 'matrix'
  const headers = asStringArray(block.headers)
  const widths = Array.isArray(block.widths) ? block.widths : []
  const rawRows = Array.isArray(block.rows) ? block.rows : []

  if (headers.length === 0 && rawRows.length === 0) return null

  const widthFor = (index: number) => {
    const width = widths[index]
    return typeof width === 'number' && width > 0 ? `${width}px` : undefined
  }

  const columnCount = headers.length + (matrix ? 1 : 0)
  const minWidth = columnCount > 3 ? `${Math.max(560, columnCount * 120)}px` : undefined

  return (
    <div className="note-table-wrap">
      <table
        className={matrix ? 'note-table note-table-matrix' : 'note-table'}
        style={{ minWidth }}
      >
        <colgroup>
          {matrix ? <col style={{ width: widthFor(0) }} /> : null}
          {headers.map((_, index) => (
            <col key={index} style={{ width: widthFor(matrix ? index + 1 : index) }} />
          ))}
        </colgroup>
        <thead>
          <tr>
            {matrix ? <th className="note-table-corner" scope="col" /> : null}
            {headers.map((header, index) => (
              <th key={index} scope="col">
                {header ? <MarkdownView source={header} inline /> : null}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {matrix ? (
            <MatrixRows rows={rawRows} />
          ) : (
            <PlainRows rows={rawRows} />
          )}
        </tbody>
      </table>
    </div>
  )
}

function PlainRows({ rows }: { rows: unknown[] }) {
  return rows.map((row, rowIndex) => {
    const cells = Array.isArray(row) ? row.map(asString) : []
    return (
      <tr key={rowIndex}>
        {cells.map((cell, cellIndex) => (
          <td key={cellIndex}>
            {cell ? <MarkdownView source={cell} inline /> : null}
          </td>
        ))}
      </tr>
    )
  })
}

function MatrixRows({ rows }: { rows: unknown[] }) {
  return rows.map((row, rowIndex) => {
    const record = isRecord(row) ? row : {}
    const header = asString(record.header)
    const cells = asStringArray(record.cells)
    return (
      <tr key={rowIndex}>
        <th scope="row">{header ? <MarkdownView source={header} inline /> : null}</th>
        {cells.map((cell, cellIndex) => (
          <td key={cellIndex}>
            {cell ? <MarkdownView source={cell} inline /> : null}
          </td>
        ))}
      </tr>
    )
  })
}

function FlowBlock({ block }: { block: NoteBlock }) {
  const items = asStringArray(block.items).filter((item) => item.trim())
  if (items.length === 0) return null

  return (
    <ol className="note-flow">
      {items.map((item, index) => (
        <li className="note-flow-item" key={index}>
          {index > 0 ? (
            <span className="note-flow-arrow" aria-hidden="true">
              →
            </span>
          ) : null}
          <MarkdownView source={item} inline />
        </li>
      ))}
    </ol>
  )
}

function CalloutBlock({ block }: { block: NoteBlock }) {
  const text = asString(block.text)
  if (!text) return null
  const label = asString(block.label)
  const tone = pickTone(block.tone, CALLOUT_TONES, 'note')

  return (
    <div className="note-callout" data-tone={tone}>
      {label ? <p className="note-callout-label">{label}</p> : null}
      <div className="note-callout-body">
        <MarkdownView source={text} />
      </div>
    </div>
  )
}

function QuoteBlock({ block }: { block: NoteBlock }) {
  const text = asString(block.text)
  const source = asString(block.source)
  if (!text) return null

  return (
    <figure className="note-quote">
      <blockquote>
        <MarkdownView source={text} />
      </blockquote>
      {source ? <figcaption>— {source}</figcaption> : null}
    </figure>
  )
}

function GridBlock({ block }: { block: NoteBlock }) {
  const items = asRecords(block.items)
  if (items.length === 0) return null
  const columns = asNumber(block.columns) === 3 ? '3' : '2'

  return (
    <div className="note-grid" data-columns={columns}>
      {items.map((item, index) => {
        const title = asString(item.title)
        return (
          <div className="note-grid-item" key={index}>
            {title ? <p className="note-grid-title">{title}</p> : null}
            <NoteBlocks blocks={asBlocks(item.blocks)} />
          </div>
        )
      })}
    </div>
  )
}

function QuickGroupBlock({ block }: { block: NoteBlock }) {
  const items = asRecords(block.items)
  if (items.length === 0) return null
  const label = asString(block.label)
  const title = asString(block.title)

  return (
    <section className="note-quickgroup">
      {label ? <p className="note-quickgroup-label">{label}</p> : null}
      {title ? <p className="note-quickgroup-title">{title}</p> : null}
      <div className="note-quickgrid">
        {items.map((item, index) => {
          const itemTitle = asString(item.title)
          const text = asString(item.text)
          return (
            <div
              className="note-quick"
              data-tone={pickTone(item.tone, QUICK_TONES, 'teal')}
              key={index}
            >
              {itemTitle ? <p className="note-quick-title">{itemTitle}</p> : null}
              {text ? (
                <div className="note-quick-text">
                  <MarkdownView source={text} inline />
                </div>
              ) : null}
            </div>
          )
        })}
      </div>
    </section>
  )
}

function FigureBlock({ block }: { block: NoteBlock }) {
  const svg = asString(block.svg)
  const caption = asString(block.caption)
  const [open, setOpen] = useState(false)
  const triggerRef = useRef<HTMLButtonElement | null>(null)
  const closeRef = useRef<HTMLButtonElement | null>(null)
  const canvasRef = useRef<HTMLDivElement | null>(null)

  useEffect(() => {
    if (!open) return

    const previousOverflow = document.body.style.overflow
    document.body.style.overflow = 'hidden'

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        setOpen(false)
      }
      if (event.key === 'Tab') {
        event.preventDefault()
        closeRef.current?.focus()
      }
    }

    document.addEventListener('keydown', handleKeyDown)
    closeRef.current?.focus()

    return () => {
      document.body.style.overflow = previousOverflow
      document.removeEventListener('keydown', handleKeyDown)
      triggerRef.current?.focus()
    }
  }, [open])

  useEffect(() => {
    if (!open || !canvasRef.current) return
    const canvas = canvasRef.current
    canvas.scrollLeft = Math.max(0, (canvas.scrollWidth - canvas.clientWidth) / 2)
  }, [open])

  if (!svg.trim()) return null

  const src = `data:image/svg+xml;charset=utf-8,${encodeURIComponent(svg)}`
  const label = caption || '讲义插图'

  return (
    <>
      <figure className="note-figure">
        <button
          ref={triggerRef}
          type="button"
          className="note-figure-trigger"
          onClick={() => setOpen(true)}
          aria-label={`放大图片：${label}`}
          title="点击放大"
        >
          <img src={src} alt={caption} loading="lazy" />
        </button>
        {caption ? <figcaption>{caption}</figcaption> : null}
      </figure>
      {open
        ? createPortal(
            <div className="note-zoom" role="dialog" aria-modal="true" aria-label="图片预览">
              <div className="note-zoom-bar">
                <span className="note-zoom-caption">{label}</span>
                <button
                  ref={closeRef}
                  type="button"
                  className="note-zoom-close"
                  onClick={() => setOpen(false)}
                  aria-label="关闭图片预览"
                >
                  <Icon name="x" size={20} />
                </button>
              </div>
              <div
                className="note-zoom-canvas"
                ref={canvasRef}
                onClick={(event) => {
                  if (event.target === event.currentTarget) setOpen(false)
                }}
              >
                <img src={src} alt={caption} />
              </div>
            </div>,
            document.body,
          )
        : null}
    </>
  )
}

function SummaryBlock({ block, id }: { block: NoteBlock; id?: string }) {
  const title = asString(block.title)

  return (
    <section className="note-summary" id={id}>
      {title ? <h3 className="note-summary-title">{title}</h3> : null}
      <NoteBlocks blocks={asBlocks(block.blocks)} />
    </section>
  )
}

function ChipsBlock({ block }: { block: NoteBlock }) {
  const items = asRecords(block.items)
  if (items.length === 0) return null

  return (
    <ul className="note-chips">
      {items.map((item, index) => {
        const label = asString(item.label)
        const note = asString(item.note)
        return (
          <li
            className="note-chip"
            data-tone={pickTone(item.tone, CHIP_TONES, 'blue')}
            key={index}
          >
            {label ? <span className="note-chip-label">{label}</span> : null}
            {note ? <span className="note-chip-note">{note}</span> : null}
          </li>
        )
      })}
    </ul>
  )
}
