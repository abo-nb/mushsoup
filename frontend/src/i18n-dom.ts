import { toTraditional } from './i18n-chars'

export type Lang = 'hans' | 'hant'

const SKIP_SELECTOR = 'script,style,textarea,input,pre,code'
const ATTRS = ['placeholder', 'title', 'aria-label', 'alt'] as const

let convert: (source: string) => string = (source) => source

const textOriginals = new WeakMap<Text, string>()
const textOutputs = new WeakMap<Text, string>()
const attrOriginals = new WeakMap<Element, Map<string, string>>()
const attrOutputs = new WeakMap<Element, Map<string, string>>()

function convertTextNode(node: Text, force: boolean) {
  if (node.parentElement?.closest(SKIP_SELECTOR)) return
  const current = node.data
  const previous = textOutputs.get(node)
  let original = textOriginals.get(node)
  if (force || original === undefined || current !== previous) {
    original = current
    textOriginals.set(node, original)
  }
  const next = convert(original)
  textOutputs.set(node, next)
  if (next !== current) node.data = next
}

function convertAttr(element: Element, name: string, force: boolean) {
  const value = element.getAttribute(name)
  if (value === null) return
  let originals = attrOriginals.get(element)
  let outputs = attrOutputs.get(element)
  if (!originals) {
    originals = new Map()
    attrOriginals.set(element, originals)
  }
  if (!outputs) {
    outputs = new Map()
    attrOutputs.set(element, outputs)
  }
  const previous = outputs.get(name)
  let original = originals.get(name)
  if (force || original === undefined || value !== previous) {
    original = value
    originals.set(name, original)
  }
  const next = convert(original)
  outputs.set(name, next)
  if (next !== value) element.setAttribute(name, next)
}

function convertSubtree(root: Node, force: boolean) {
  if (root.nodeType === Node.TEXT_NODE) {
    convertTextNode(root as Text, force)
    return
  }
  if (!(root instanceof Element)) return
  for (const name of ATTRS) {
    if (root.hasAttribute(name)) convertAttr(root, name, force)
  }
  const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT)
  let node = walker.nextNode()
  while (node) {
    convertTextNode(node as Text, force)
    node = walker.nextNode()
  }
  for (const element of root.querySelectorAll('*')) {
    for (const name of ATTRS) {
      if (element.hasAttribute(name)) convertAttr(element, name, force)
    }
  }
}

const pending = new Set<Node>()
let scheduled = false
let observer: MutationObserver | null = null

function flush() {
  scheduled = false
  for (const node of pending) {
    if (node.isConnected) convertSubtree(node, false)
  }
  pending.clear()
}

function schedule() {
  if (scheduled) return
  scheduled = true
  requestAnimationFrame(flush)
}

function observe() {
  if (observer) return
  observer = new MutationObserver((records) => {
    for (const record of records) {
      if (record.type === 'characterData') {
        pending.add(record.target)
      } else if (record.type === 'childList') {
        for (const node of record.addedNodes) pending.add(node)
      } else if (record.type === 'attributes') {
        pending.add(record.target)
      }
    }
    schedule()
  })
  observer.observe(document.documentElement, {
    childList: true,
    characterData: true,
    subtree: true,
    attributes: true,
    attributeFilter: [...ATTRS],
  })
}

export function applyLanguage(lang: Lang) {
  convert = lang === 'hant' ? toTraditional : (source) => source
  observe()
  pending.clear()
  convertSubtree(document.documentElement, true)
}
