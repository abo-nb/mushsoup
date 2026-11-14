import { Link, NavLink, Outlet } from 'react-router-dom'
import { useAuth } from '../auth-context'
import { Avatar } from './Avatar'
import { Icon } from './Icon'

export function Layout() {
  const { user, logout } = useAuth()

  return (
    <div className="app-shell">
      <a className="skip-link" href="#main">
        跳到主要内容
      </a>
      <header className="topbar">
        <div className="topbar-inner">
          <Link to="/" className="brand">
            <span className="brand-mark" aria-hidden="true" />
            蘑菇汤题库
          </Link>
          <nav className="main-nav" aria-label="主导航">
            <NavLink
              to="/"
              end
              className={({ isActive }) => (isActive ? 'nav-link active' : 'nav-link')}
            >
              <Icon name="clock" size={18} />
              最近
            </NavLink>
            <NavLink
              to="/banks"
              className={({ isActive }) => (isActive ? 'nav-link active' : 'nav-link')}
            >
              <Icon name="book" size={18} />
              题库
            </NavLink>
            <NavLink
              to="/public"
              className={({ isActive }) => (isActive ? 'nav-link active' : 'nav-link')}
            >
              <Icon name="globe" size={18} />
              公开
            </NavLink>
            <NavLink
              to="/leaderboard"
              className={({ isActive }) => (isActive ? 'nav-link active' : 'nav-link')}
            >
              <Icon name="trophy" size={18} />
              排行
            </NavLink>
            <NavLink
              to="/notes"
              className={({ isActive }) => (isActive ? 'nav-link active' : 'nav-link')}
            >
              <Icon name="document" size={18} />
              讲义
            </NavLink>
            <NavLink
              to="/settings"
              className={({ isActive }) => (isActive ? 'nav-link active' : 'nav-link')}
            >
              <Icon name="settings" size={18} />
              设置
            </NavLink>
          </nav>
          <div className="topbar-account">
            <Avatar name={user?.displayName ?? ''} src={user?.avatarUrl ?? null} size="sm" />
            <span className="user-name">{user?.displayName}</span>
            <button type="button" className="btn btn-small btn-ghost" onClick={() => void logout()}>
              <Icon name="logout" size={16} />
              退出
            </button>
          </div>
        </div>
      </header>
      <main id="main" className="container" tabIndex={-1}>
        <Outlet />
      </main>
    </div>
  )
}
