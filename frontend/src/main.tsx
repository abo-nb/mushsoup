import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import App from './App'
import { AuthProvider } from './auth'
import { ApiError } from './api'
import { applyThemeMode, readThemeMode, subscribeTheme } from './theme'
import './styles.css'
import './styles/editorial.css'
import './styles/manage.css'
import './styles/practice.css'
import './styles/chapters.css'
import './styles/notes.css'
import './styles/leaderboard.css'

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
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
subscribeTheme(() => undefined)

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <AuthProvider>
          <App />
        </AuthProvider>
      </BrowserRouter>
    </QueryClientProvider>
  </StrictMode>,
)
