export type ThemeMode = 'light' | 'dark' | 'system'
export type ResolvedTheme = 'light' | 'dark'

export const THEME_STORAGE_KEY = 'ms_theme'

const DARK_QUERY = '(prefers-color-scheme: dark)'
const THEME_COLORS: Record<ResolvedTheme, string> = {
  light: '#fafbfc',
  dark: '#101312',
}

export function readThemeMode(): ThemeMode {
  try {
    const stored = window.localStorage.getItem(THEME_STORAGE_KEY)
    if (stored === 'light' || stored === 'dark' || stored === 'system') {
      return stored
    }
  } catch {
    // localStorage may be unavailable; fall back to the system preference.
  }
  return 'system'
}

export function resolveTheme(mode: ThemeMode): ResolvedTheme {
  if (mode === 'system') {
    return window.matchMedia(DARK_QUERY).matches ? 'dark' : 'light'
  }
  return mode
}

function paint(theme: ResolvedTheme) {
  document.documentElement.dataset.theme = theme
  const meta = document.querySelector('meta[name="theme-color"]')
  if (meta) meta.setAttribute('content', THEME_COLORS[theme])
}

export function applyThemeMode(mode: ThemeMode): ResolvedTheme {
  const theme = resolveTheme(mode)
  paint(theme)
  try {
    window.localStorage.setItem(THEME_STORAGE_KEY, mode)
  } catch {
    // Persistence is best-effort; the chosen theme still applies for this load.
  }
  return theme
}

export function subscribeTheme(callback: (theme: ResolvedTheme) => void): () => void {
  const media = window.matchMedia(DARK_QUERY)
  const onChange = () => {
    if (readThemeMode() !== 'system') return
    const theme = resolveTheme('system')
    paint(theme)
    callback(theme)
  }

  media.addEventListener('change', onChange)
  return () => media.removeEventListener('change', onChange)
}
