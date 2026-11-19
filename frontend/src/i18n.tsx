import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
  type ReactNode,
} from 'react'
import { applyLanguage, type Lang } from './i18n-dom'

export type { Lang }

export const LANG_STORAGE_KEY = 'ms_lang'

export function readLang(): Lang {
  try {
    const stored = window.localStorage.getItem(LANG_STORAGE_KEY)
    if (stored === 'hans' || stored === 'hant') return stored
  } catch {
    // localStorage may be unavailable; fall back to simplified.
  }
  return 'hans'
}

type LanguageContextValue = {
  lang: Lang
  setLang: (lang: Lang) => void
}

const LanguageContext = createContext<LanguageContextValue | null>(null)

export function LanguageProvider({ children }: { children: ReactNode }) {
  const [lang, setLangState] = useState<Lang>(readLang)

  useEffect(() => {
    applyLanguage(lang)
    document.documentElement.lang = lang === 'hant' ? 'zh-Hant' : 'zh-Hans'
    try {
      window.localStorage.setItem(LANG_STORAGE_KEY, lang)
    } catch {
      // Persistence is best-effort; the choice still applies for this load.
    }
  }, [lang])

  const setLang = useCallback((next: Lang) => setLangState(next), [])
  const value = useMemo(() => ({ lang, setLang }), [lang, setLang])

  return <LanguageContext.Provider value={value}>{children}</LanguageContext.Provider>
}

export function useLanguage(): LanguageContextValue {
  const context = useContext(LanguageContext)
  if (!context) throw new Error('useLanguage must be used inside LanguageProvider')
  return context
}
