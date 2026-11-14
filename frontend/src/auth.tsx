import { useMemo } from 'react'
import type { ReactNode } from 'react'
import { useNavigate } from 'react-router-dom'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from './api'
import type { User } from './api'
import { AuthContext } from './auth-context'
import type { AuthContextValue } from './auth-context'

async function fetchMe(): Promise<User | null> {
  return api.me()
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const meQuery = useQuery<User | null>({
    queryKey: ['me'],
    queryFn: fetchMe,
    retry: false,
    staleTime: 30_000,
  })

  const value = useMemo<AuthContextValue>(
    () => ({
      user: meQuery.data ?? null,
      isLoading: meQuery.isPending,
      logout: async () => {
        await api.logout().catch(() => undefined)
        queryClient.setQueryData(['me'], null)
        queryClient.removeQueries({ predicate: (query) => query.queryKey[0] !== 'me' })
        navigate('/login', { replace: true })
      },
    }),
    [meQuery.data, meQuery.isPending, navigate, queryClient],
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}
