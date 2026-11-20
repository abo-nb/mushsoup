import { useEffect, useState, useSyncExternalStore } from 'react'
import { Link, NavLink, Outlet, useLocation } from 'react-router-dom'
import { useAuth } from '../auth-context'
import { getNavConfig, NAV_ITEMS_BY_KEY, subscribeNav } from '../nav'
import { Avatar } from './Avatar'
import { Icon } from './Icon'

export function Layout() {
  const { user, logout } = useAuth()
  const nav = useSyncExternalStore(subscribeNav, getNavConfig, getNavConfig)
  const location = useLocation()
  const [moreOpen, setMoreOpen] = useState(false)

  useEffect(() => {
    if (!moreOpen) return
    function onKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape') setMoreOpen(false)
    }

    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [moreOpen])

  const pinnedKeys = nav.order.filter((key) => nav.pinned.includes(key))
  const moreKeys = nav.order.filter((key) => !nav.pinned.includes(key))
  // 当前页在「更多」里时，让底栏的「更多」保持高亮。
  const moreActive = moreKeys.some((key) => {
    const item = NAV_ITEMS_BY_KEY.get(key)
    if (!item) return false
    return key === 'recent'
      ? location.pathname === item.path
      : location.pathname === item.path || location.pathname.startsWith(`${item.path}/`)
  })

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
            {nav.order.map((key) => {
              const item = NAV_ITEMS_BY_KEY.get(key)
              if (!item) return null
              return (
                <NavLink
                  key={key}
                  to={item.path}
                  end={key === 'recent'}
                  className={({ isActive }) => (isActive ? 'nav-link active' : 'nav-link')}
                >
                  <Icon name={item.icon} size={18} />
                  {item.label}
                </NavLink>
              )
            })}
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

      <nav className="bottom-nav" aria-label="主导航">
        {pinnedKeys.map((key) => {
          const item = NAV_ITEMS_BY_KEY.get(key)
          if (!item) return null
          return (
            <NavLink
              key={key}
              to={item.path}
              end={key === 'recent'}
              className={({ isActive }) =>
                isActive ? 'bottom-nav-link active' : 'bottom-nav-link'
              }
              onClick={() => setMoreOpen(false)}
            >
              <Icon name={item.icon} size={20} />
              <span>{item.label}</span>
            </NavLink>
          )
        })}
        <button
          type="button"
          className={moreOpen || moreActive ? 'bottom-nav-link active' : 'bottom-nav-link'}
          aria-expanded={moreOpen}
          aria-controls="bottom-more"
          onClick={() => setMoreOpen((open) => !open)}
        >
          <Icon name="more" size={20} />
          <span>更多</span>
        </button>
      </nav>

      {moreOpen ? (
        <>
          <button
            type="button"
            className="bottom-more-backdrop"
            aria-label="关闭更多导航"
            onClick={() => setMoreOpen(false)}
          />
          <nav id="bottom-more" className="bottom-more" aria-label="更多导航">
            {moreKeys.map((key) => {
              const item = NAV_ITEMS_BY_KEY.get(key)
              if (!item) return null
              return (
                <NavLink
                  key={key}
                  to={item.path}
                  end={key === 'recent'}
                  className={({ isActive }) =>
                    isActive ? 'bottom-more-link active' : 'bottom-more-link'
                  }
                  onClick={() => setMoreOpen(false)}
                >
                  <Icon name={item.icon} size={18} />
                  <span>{item.label}</span>
                </NavLink>
              )
            })}
          </nav>
        </>
      ) : null}
    </div>
  )
}
