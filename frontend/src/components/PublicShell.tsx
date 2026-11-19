import type { ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { useAuth } from '../auth-context'
import { Icon } from './Icon'

/** 分享链接的落地外壳：不放主导航，只留品牌和登录/回主页入口。 */
export function PublicShell({ children }: { children: ReactNode }) {
  const { user } = useAuth()

  return (
    <div className="app-shell">
      <header className="topbar">
        <div className="topbar-inner public-topbar">
          <Link to="/" className="brand">
            <span className="brand-mark" aria-hidden="true" />
            蘑菇汤题库
          </Link>
          <div className="topbar-account">
            {user ? (
              <Link to="/" className="btn btn-small btn-ghost">
                <Icon name="book" size={15} />
                回到主页
              </Link>
            ) : (
              <Link to="/login" className="btn btn-small btn-primary">
                <Icon name="logout" size={15} />
                登录后保存进度
              </Link>
            )}
          </div>
        </div>
      </header>
      <main className="container">{children}</main>
    </div>
  )
}
