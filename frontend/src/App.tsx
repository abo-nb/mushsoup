import { Navigate, Route, Routes } from 'react-router-dom'
import { useAuth } from './auth-context'
import { Layout } from './components/Layout'
import { Loading } from './components/Ui'
import { BankDetailPage } from './pages/BankDetailPage'
import { BankSettingsPage } from './pages/BankSettingsPage'
import { BanksPage } from './pages/BanksPage'
import { ImportPage } from './pages/ImportPage'
import { LeaderboardPage } from './pages/LeaderboardPage'
import { LoginPage } from './pages/LoginPage'
import { NoteImportPage } from './pages/NoteImportPage'
import { NotePage } from './pages/NotePage'
import { NoteSettingsPage } from './pages/NoteSettingsPage'
import { NotesPage } from './pages/NotesPage'
import { PracticePage } from './pages/PracticePage'
import { PoolsPage } from './pages/PoolsPage'
import { PublicBanksPage } from './pages/PublicBanksPage'
import { QuestionEditorPage } from './pages/QuestionEditorPage'
import { SettingsPage } from './pages/SettingsPage'

function ProtectedLayout() {
  const { user, isLoading } = useAuth()

  if (isLoading) {
    return <Loading full label="正在载入…" />
  }

  if (!user) {
    return <Navigate to="/login" replace />
  }

  return <Layout />
}

export default function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route element={<ProtectedLayout />}>
        <Route path="/" element={<BanksPage />} />
        <Route path="/public" element={<PublicBanksPage />} />
        <Route path="/leaderboard" element={<LeaderboardPage />} />
        <Route path="/notes" element={<NotesPage />} />
        <Route path="/notes/import" element={<NoteImportPage />} />
        <Route path="/notes/:id/settings" element={<NoteSettingsPage />} />
        <Route path="/notes/:id" element={<NotePage />} />
        <Route path="/banks/:id" element={<BankDetailPage />} />
        <Route path="/banks/:id/settings" element={<BankSettingsPage />} />
        <Route path="/banks/:id/questions/new" element={<QuestionEditorPage />} />
        <Route path="/banks/:id/questions/:qid" element={<QuestionEditorPage />} />
        <Route path="/banks/:id/pools" element={<PoolsPage />} />
        <Route path="/banks/:id/import" element={<ImportPage />} />
        <Route path="/practice/:sessionId" element={<PracticePage />} />
        <Route path="/settings" element={<SettingsPage />} />
      </Route>
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  )
}
