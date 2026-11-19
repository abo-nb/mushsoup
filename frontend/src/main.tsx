import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import App from './App'
import { AuthProvider } from './auth'
import { LanguageProvider, readLang } from './i18n'
import { applyLanguage } from './i18n-dom'
import { ApiError } from './api'
import { applyThemeMode, readThemeMode, subscribeTheme } from './theme'
import './styles.css'
import './styles/editorial.css'
import './styles/manage.css'
import './styles/practice.css'
import './styles/chapters.css'
import './styles/notes.css'
import './styles/leaderboard.css'
import './styles/recent.css'
import './styles/favorites.css'
import './styles/share.css'

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 60_000,
      gcTime: 30 * 60_000,
      refetchOnWindowFocus: false,
      retry: (failureCount, error) => {
        if (error instanceof ApiError && error.status >= 400 && error.status < 500) {
          return false
        }
        return failureCount < 1
      },
    },
  },
})

applyThemeMode(readThemeMode())
applyLanguage(readLang())
subscribeTheme(() => undefined)

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <LanguageProvider>
          <AuthProvider>
            <App />
          </AuthProvider>
        </LanguageProvider>
      </BrowserRouter>
    </QueryClientProvider>
  </StrictMode>,
)
