import Markdown from 'react-markdown'
import type { Components } from 'react-markdown'
import remarkGfm from 'remark-gfm'
import remarkMath from 'remark-math'
import rehypeKatex from 'rehype-katex'
import rehypeHighlight from 'rehype-highlight'
import 'katex/dist/katex.min.css'
import 'highlight.js/styles/github.css'

function assetUrl(src: string | undefined): string | undefined {
  if (!src) return src
  if (src.startsWith('asset://')) {
    return `/api/assets/${encodeURIComponent(src.slice('asset://'.length))}/raw`
  }
  return src
}

const components: Components = {
  img({ src, alt, title }) {
    return (
      <img
        src={assetUrl(typeof src === 'string' ? src : undefined)}
        alt={alt ?? ''}
        title={title}
        loading="lazy"
      />
    )
  },
}

const inlineComponents: Components = {
  ...components,
  p({ children }) {
    return <>{children}</>
  },
}

export function MarkdownView({ source, inline = false }: { source: string; inline?: boolean }) {
  const body = (
    <Markdown
      remarkPlugins={[remarkGfm, remarkMath]}
      rehypePlugins={[rehypeKatex, rehypeHighlight]}
      components={inline ? inlineComponents : components}
    >
      {source}
    </Markdown>
  )

  if (inline) {
    return <span className="markdown markdown-inline">{body}</span>
  }

  return <div className="markdown">{body}</div>
}
