import { Navigate, useParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { api } from '../api'
import { useAuth } from '../auth-context'
import { PublicShell } from '../components/PublicShell'
import { EmptyState, Loading } from '../components/Ui'

/** 短链接入口：登录了就跳真实页面（进度照常保存），没登录就进游客视图。 */
export function ShareLandingPage() {
  const { code } = useParams<{ code: string }>()
  const { user, isLoading } = useAuth()
  const linkQuery = useQuery({
    queryKey: ['share-link', code],
    queryFn: () => api.resolveLink(code ?? ''),
    enabled: Boolean(code),
    retry: false,
  })

  if (isLoading || linkQuery.isPending) {
    return <Loading full label="正在打开分享链接…" />
  }

  if (linkQuery.isError || !linkQuery.data) {
    return (
      <PublicShell>
        <EmptyState
          title="链接不可用"
          hint="可能已经失效，或者内容被改成了私有。可以让分享的人重新发一次。"
        />
      </PublicShell>
    )
  }

  const link = linkQuery.data
  const canonical = link.kind === 'bank' ? `/banks/${link.targetId}` : `/notes/${link.targetId}`
  const guest =
    link.kind === 'bank' ? `/share/bank/${link.targetId}` : `/share/note/${link.targetId}`

  return <Navigate replace to={user ? canonical : guest} />
}
