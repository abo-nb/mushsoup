import { useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { api, errorMessage } from '../api'
import { useAuth } from '../auth-context'
import { Icon } from '../components/Icon'
import { ErrorNote } from '../components/Ui'
import { SHUFFLE_HINTS, SHUFFLE_LABELS, SUBMIT_HINTS, SUBMIT_LABELS } from '../format'
import { applyThemeMode, readThemeMode, type ThemeMode } from '../theme'

const THEME_LABELS: Record<ThemeMode, string> = {
  light: '浅色',
  dark: '深色',
  system: '跟随系统',
}

const THEME_HINTS: Record<ThemeMode, string> = {
  light: '始终使用浅色界面。',
  dark: '始终使用深色界面。',
  system: '跟随操作系统的深浅色设置。',
}

export function SettingsPage() {
  const { user, logout } = useAuth()
  const queryClient = useQueryClient()
  const [themeMode, setThemeMode] = useState<ThemeMode>(readThemeMode)
  const [shuffle, setShuffle] = useState(user?.prefs.shuffleOptions ?? 'always')
  const [submitMode, setSubmitMode] = useState(user?.prefs.submitMode ?? 'space')

  const invalidateMe = async () => {
    await queryClient.invalidateQueries({ queryKey: ['me'] })
  }

  const shuffleMutation = useMutation({
    mutationFn: (value: string) => api.updatePrefs({ shuffleOptions: value }),
    onSuccess: invalidateMe,
    onError: () => {
      if (user) setShuffle(user.prefs.shuffleOptions)
    },
  })

  const submitMutation = useMutation({
    mutationFn: (value: string) => api.updatePrefs({ submitMode: value }),
    onSuccess: invalidateMe,
    onError: () => {
      if (user) setSubmitMode(user.prefs.submitMode)
    },
  })

  function chooseShuffle(value: string) {
    setShuffle(value)
    shuffleMutation.mutate(value)
  }

  function chooseSubmitMode(value: string) {
    setSubmitMode(value)
    submitMutation.mutate(value)
  }

  function chooseTheme(value: ThemeMode) {
    setThemeMode(value)
    applyThemeMode(value)
  }

  return (
    <div>
      <header className="page-header">
        <div className="page-heading">
          <p className="eyebrow">账号</p>
          <h1>设置</h1>
          <p className="page-sub">调整界面外观与练习时的默认行为。</p>
        </div>
      </header>

      <div className="settings-stack">
        <section className="card card-pad settings-section">
          <div className="settings-head">
            <div>
              <h2 className="section-title" id="theme-title">
                外观
              </h2>
              <p className="muted settings-desc">
                选择界面配色，切换后立即生效。仅影响这台设备。
              </p>
            </div>
          </div>

          <div className="radio-cards" role="radiogroup" aria-labelledby="theme-title">
            {(Object.keys(THEME_LABELS) as ThemeMode[]).map((value) => (
              <label
                key={value}
                className={themeMode === value ? 'radio-card selected' : 'radio-card'}
              >
                <input
                  type="radio"
                  name="themeMode"
                  value={value}
                  checked={themeMode === value}
                  onChange={() => chooseTheme(value)}
                />
                <span className="radio-card-text">
                  <strong>{THEME_LABELS[value]}</strong>
                  <small>{THEME_HINTS[value]}</small>
                </span>
                <span className="radio-card-check" aria-hidden="true">
                  {themeMode === value ? <Icon name="check" size={16} /> : null}
                </span>
              </label>
            ))}
          </div>
        </section>

        <section className="card card-pad settings-section">
          <div className="settings-head">
            <div>
              <h2 className="section-title" id="shuffle-title">
                选项乱序
              </h2>
              <p className="muted settings-desc">
                控制练习时选项的排列顺序。「总是」或「从不」会覆盖题目自身的设置；「跟随题库」则按题目和题库的默认值决定。
              </p>
            </div>
            <span className="settings-status" role="status" aria-live="polite">
              {shuffleMutation.isPending ? (
                <span className="form-hint">保存中…</span>
              ) : shuffleMutation.isSuccess ? (
                <span className="save-hint">已保存</span>
              ) : null}
            </span>
          </div>

          <div className="radio-cards" role="radiogroup" aria-labelledby="shuffle-title">
            {Object.entries(SHUFFLE_LABELS).map(([value, label]) => (
              <label
                key={value}
                className={shuffle === value ? 'radio-card selected' : 'radio-card'}
              >
                <input
                  type="radio"
                  name="shuffle"
                  value={value}
                  checked={shuffle === value}
                  onChange={() => chooseShuffle(value)}
                  disabled={shuffleMutation.isPending}
                />
                <span className="radio-card-text">
                  <strong>{label}</strong>
                  <small>{SHUFFLE_HINTS[value]}</small>
                </span>
                <span className="radio-card-check" aria-hidden="true">
                  {shuffle === value ? <Icon name="check" size={16} /> : null}
                </span>
              </label>
            ))}
          </div>

          {shuffleMutation.isError ? (
            <ErrorNote message={errorMessage(shuffleMutation.error)} />
          ) : null}
        </section>

        <section className="card card-pad settings-section">
          <div className="settings-head">
            <div>
              <h2 className="section-title" id="submit-title">
                答题提交
              </h2>
              <p className="muted settings-desc">
                控制作答后怎么提交答案。Enter 始终可以提交；提交后按空格进入下一题。
              </p>
            </div>
            <span className="settings-status" role="status" aria-live="polite">
              {submitMutation.isPending ? (
                <span className="form-hint">保存中…</span>
              ) : submitMutation.isSuccess ? (
                <span className="save-hint">已保存</span>
              ) : null}
            </span>
          </div>

          <div className="radio-cards" role="radiogroup" aria-labelledby="submit-title">
            {Object.entries(SUBMIT_LABELS).map(([value, label]) => (
              <label
                key={value}
                className={submitMode === value ? 'radio-card selected' : 'radio-card'}
              >
                <input
                  type="radio"
                  name="submitMode"
                  value={value}
                  checked={submitMode === value}
                  onChange={() => chooseSubmitMode(value)}
                  disabled={submitMutation.isPending}
                />
                <span className="radio-card-text">
                  <strong>{label}</strong>
                  <small>{SUBMIT_HINTS[value]}</small>
                </span>
                <span className="radio-card-check" aria-hidden="true">
                  {submitMode === value ? <Icon name="check" size={16} /> : null}
                </span>
              </label>
            ))}
          </div>

          {submitMutation.isError ? (
            <ErrorNote message={errorMessage(submitMutation.error)} />
          ) : null}
        </section>

        <section className="card card-pad settings-section">
          <div className="settings-head">
            <div>
              <h2 className="section-title">账号</h2>
            </div>
          </div>
          <dl className="account-list">
            <div>
              <dt>昵称</dt>
              <dd>{user?.displayName}</dd>
            </div>
            <div>
              <dt>Discord</dt>
              <dd>{user?.username ? `@${user.username}` : '—'}</dd>
            </div>
            <div>
              <dt>用户 ID</dt>
              <dd>{user?.id ?? '—'}</dd>
            </div>
          </dl>
          <button type="button" className="btn btn-ghost" onClick={() => void logout()}>
            退出登录
          </button>
        </section>
      </div>
    </div>
  )
}
