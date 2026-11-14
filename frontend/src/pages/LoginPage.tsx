import { useEffect, useMemo, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import { Navigate, useNavigate } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, errorMessage } from '../api'
import type { VerificationTicket, VerifyMember } from '../api'
import { useAuth } from '../auth-context'
import { Avatar } from '../components/Avatar'
import { Icon } from '../components/Icon'
import { ErrorNote, Loading } from '../components/Ui'

type Selection = {
  id: string
  username: string
  displayName: string
  avatarUrl: string | null
  manual?: boolean
}

const MANUAL_SELECTION_NAME = 'Discord 用户'

function matchesQuery(member: VerifyMember, query: string): boolean {
  const needle = query.toLowerCase()
  return [member.username, member.displayName, member.nickname, member.globalName].some(
    (value) => typeof value === 'string' && value.toLowerCase().includes(needle),
  )
}

function deliveryText(delivery: VerificationTicket['delivery']): string {
  return delivery === 'thread'
    ? '验证码在验证频道里只有你能看到的私密线程中。'
    : '验证码已私信给你。'
}

function secondsUntil(expiresAt: string): number {
  const end = Date.parse(expiresAt)
  if (Number.isNaN(end)) return 0
  return Math.max(0, Math.ceil((end - Date.now()) / 1000))
}

function formatCountdown(seconds: number): string {
  const minutes = Math.floor(seconds / 60)
  const rest = seconds % 60
  return `${String(minutes).padStart(2, '0')}:${String(rest).padStart(2, '0')}`
}

export function LoginPage() {
  const { user, isLoading } = useAuth()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [selection, setSelection] = useState<Selection | null>(null)
  const [ticket, setTicket] = useState<VerificationTicket | null>(null)
  const [query, setQuery] = useState('')
  const [manualId, setManualId] = useState('')
  const [manualError, setManualError] = useState<string | null>(null)
  const [code, setCode] = useState('')
  const [codeError, setCodeError] = useState<string | null>(null)
  const [remaining, setRemaining] = useState(0)
  const [refreshing, setRefreshing] = useState(false)
  const searchInputRef = useRef<HTMLInputElement>(null)
  const codeInputRef = useRef<HTMLInputElement>(null)
  const hadTicketRef = useRef(false)
  const forceMembersRefreshRef = useRef(false)

  const membersQuery = useQuery({
    queryKey: ['auth', 'verify-members'],
    queryFn: () => api.verifyMembers(forceMembersRefreshRef.current),
    enabled: !user && !ticket,
    retry: false,
    staleTime: 60_000,
  })

  async function handleRefreshMembers() {
    if (refreshing || membersQuery.isFetching) return
    setRefreshing(true)
    forceMembersRefreshRef.current = true
    try {
      await membersQuery.refetch()
    } finally {
      forceMembersRefreshRef.current = false
      setRefreshing(false)
    }
  }

  const confirmMutation = useMutation({
    mutationFn: () => {
      if (!ticket) throw new Error('验证会话已失效，请重新发送验证码。')
      return api.confirmVerification(ticket.ticketId, code)
    },
    onSuccess: (loggedIn) => {
      queryClient.setQueryData(['me'], loggedIn)
      queryClient.removeQueries({ predicate: (item) => item.queryKey[0] !== 'me' })
      navigate('/', { replace: true })
    },
  })

  const requestMutation = useMutation({
    mutationFn: (userId: string) => api.requestVerification(userId),
    onSuccess: (result) => {
      confirmMutation.reset()
      setTicket(result)
      setRemaining(secondsUntil(result.expiresAt))
      setCode('')
      setCodeError(null)
    },
  })

  useEffect(() => {
    if (ticket) {
      hadTicketRef.current = true
      codeInputRef.current?.focus()
      return
    }
    if (hadTicketRef.current) {
      hadTicketRef.current = false
      searchInputRef.current?.focus()
    }
  }, [ticket])

  useEffect(() => {
    if (!ticket) return
    const end = Date.parse(ticket.expiresAt)
    if (Number.isNaN(end)) return
    let timer = 0
    const tick = () => {
      const next = Math.max(0, Math.ceil((end - Date.now()) / 1000))
      setRemaining(next)
      if (next === 0) window.clearInterval(timer)
    }
    timer = window.setInterval(tick, 1000)
    return () => window.clearInterval(timer)
  }, [ticket])

  const members = membersQuery.data?.members
  const filteredMembers = useMemo(
    () =>
      (members ?? []).filter((member) =>
        query.trim() ? matchesQuery(member, query.trim()) : true,
      ),
    [members, query],
  )

  if (isLoading) {
    return <Loading full label="正在载入…" />
  }

  if (user) {
    return <Navigate to="/" replace />
  }

  const expired = ticket !== null && remaining <= 0
  const pickedHandle = selection
    ? selection.manual
      ? `ID ${selection.username}`
      : `@${selection.username}`
    : ''
  const codeInputClass = codeError ? 'input auth-code-input input-invalid' : 'input auth-code-input'
  const showManualEntry = !membersQuery.isPending && (members?.length ?? 0) === 0

  function startVerification(next: Selection) {
    setSelection(next)
    setManualError(null)
    requestMutation.mutate(next.id)
  }

  function handleMemberClick(member: VerifyMember) {
    if (requestMutation.isPending) return
    startVerification({
      id: member.id,
      username: member.username,
      displayName: member.displayName,
      avatarUrl: member.avatarUrl,
    })
  }

  function handleManualSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const userId = manualId.trim()
    if (!userId) {
      setManualError('请填写 Discord 用户 ID。')
      return
    }
    if (!/^\d+$/.test(userId)) {
      setManualError('Discord 用户 ID 应为纯数字。')
      return
    }
    startVerification({
      id: userId,
      username: userId,
      displayName: MANUAL_SELECTION_NAME,
      avatarUrl: null,
      manual: true,
    })
  }

  function handleCodeChange(value: string) {
    if (confirmMutation.isError) confirmMutation.reset()
    setCodeError(null)
    setCode(value.replace(/[^0-9a-zA-Z]/g, '').toUpperCase().slice(0, 4))
  }

  function handleConfirm(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (confirmMutation.isPending || requestMutation.isPending || expired) return
    if (code.length !== 4) {
      setCodeError('请输入 4 位验证码。')
      codeInputRef.current?.focus()
      return
    }
    confirmMutation.mutate()
  }

  function handleResend() {
    if (!selection || requestMutation.isPending) return
    confirmMutation.reset()
    setCodeError(null)
    requestMutation.mutate(selection.id)
  }

  function handleBack() {
    setTicket(null)
    setSelection(null)
    setCode('')
    setCodeError(null)
    setQuery('')
    confirmMutation.reset()
    requestMutation.reset()
  }

  return (
    <div className="auth-page">
      <div className="auth-card">
        <div className="auth-brand">
          <span className="brand-mark" aria-hidden="true" />
          <span>蘑菇汤题库</span>
        </div>

        {ticket ? (
          <>
            <h1 className="auth-title">输入验证码</h1>
            <p className="muted auth-sub">验证码已发送，请在过期前输入。</p>

            <form className="form-stack auth-form" onSubmit={handleConfirm} noValidate>
              <div className="auth-picked">
                <Avatar
                  name={selection?.displayName ?? ''}
                  src={selection?.avatarUrl ?? null}
                  size="md"
                />
                <span className="auth-picked-text">
                  <strong>{selection?.displayName}</strong>
                  <span>{pickedHandle}</span>
                </span>
              </div>

              <p className="auth-delivery">{deliveryText(ticket.delivery)}</p>

              <div className="field">
                <label htmlFor="auth-code">验证码</label>
                <input
                  ref={codeInputRef}
                  id="auth-code"
                  className={codeInputClass}
                  type="text"
                  inputMode="text"
                  autoCapitalize="characters"
                  autoComplete="one-time-code"
                  maxLength={4}
                  value={code}
                  onChange={(event) => handleCodeChange(event.target.value)}
                  placeholder="----"
                  spellCheck={false}
                  aria-invalid={codeError ? true : undefined}
                  aria-describedby="auth-code-hint"
                />
                <p className="form-hint" id="auth-code-hint">
                  4 位字母或数字，不区分大小写。
                </p>
                {codeError ? (
                  <p className="field-error" role="alert">
                    {codeError}
                  </p>
                ) : null}
              </div>

              <div className="auth-code-meta">
                {expired ? (
                  <p className="auth-expired" role="status">
                    验证码已过期，请重新发送。
                  </p>
                ) : (
                  <p className="auth-countdown">
                    剩余 <strong>{formatCountdown(remaining)}</strong>
                  </p>
                )}
                <button
                  type="button"
                  className="btn btn-ghost btn-small"
                  onClick={handleResend}
                  disabled={requestMutation.isPending}
                >
                  {requestMutation.isPending ? '正在发送…' : '重新发送验证码'}
                </button>
              </div>

              {requestMutation.isError ? (
                <ErrorNote message={errorMessage(requestMutation.error)} />
              ) : null}
              {confirmMutation.isError ? (
                <ErrorNote message={errorMessage(confirmMutation.error)} />
              ) : null}

              <button
                type="submit"
                className="btn btn-primary btn-block"
                disabled={confirmMutation.isPending || requestMutation.isPending || expired}
              >
                {confirmMutation.isPending ? '验证中…' : '验证并登录'}
              </button>
            </form>

            <div className="auth-actions">
              <button
                type="button"
                className="btn btn-ghost btn-block"
                onClick={handleBack}
                disabled={confirmMutation.isPending || requestMutation.isPending}
              >
                <Icon name="arrow-left" size={16} />
                返回上一步
              </button>
            </div>
          </>
        ) : (
          <>
            <h1 className="auth-title">用 Discord 登录</h1>
            <p className="muted auth-sub">选择你的 Discord 账号，我们会发送验证码确认身份。</p>

            <div className="auth-pick">
              <div className="field">
                <div className="auth-pick-head">
                  <label htmlFor="auth-member-search">选择账号</label>
                  <button
                    type="button"
                    className="btn btn-ghost btn-small auth-refresh"
                    onClick={() => void handleRefreshMembers()}
                    disabled={refreshing || membersQuery.isPending}
                  >
                    <Icon name="refresh" size={14} />
                    {refreshing ? '刷新中…' : '刷新成员'}
                  </button>
                </div>
                <div className="auth-search">
                  <Icon name="search" size={16} className="auth-search-icon" />
                  <input
                    ref={searchInputRef}
                    id="auth-member-search"
                    className="input auth-search-input"
                    type="search"
                    value={query}
                    onChange={(event) => setQuery(event.target.value)}
                    placeholder="搜索用户名或昵称"
                    autoComplete="off"
                    spellCheck={false}
                  />
                </div>
              </div>

              {membersQuery.isPending ? (
                <Loading label="正在载入成员…" />
              ) : membersQuery.isError && !members ? (
                <ErrorNote message={errorMessage(membersQuery.error)} />
              ) : filteredMembers.length === 0 ? (
                <p className="auth-empty" role="status">
                  {members && members.length > 0
                    ? '没有匹配的成员，换个关键词试试。'
                    : '暂时没有可选成员。'}
                </p>
              ) : (
                <ul className="auth-member-list" aria-label="Discord 成员">
                  {filteredMembers.map((member) => (
                    <li key={member.id}>
                      <button
                        type="button"
                        className="auth-member"
                        onClick={() => handleMemberClick(member)}
                        disabled={requestMutation.isPending}
                      >
                        <Avatar name={member.displayName} src={member.avatarUrl} size="md" />
                        <span className="auth-member-text">
                          <strong className="auth-member-name">{member.displayName}</strong>
                          <span className="auth-member-username">@{member.username}</span>
                        </span>
                      </button>
                    </li>
                  ))}
                </ul>
              )}

              {membersQuery.isError && members ? (
                <ErrorNote message={errorMessage(membersQuery.error)} />
              ) : null}

              {requestMutation.isPending ? (
                <p className="form-hint" role="status">
                  正在发送验证码…
                </p>
              ) : null}
              {requestMutation.isError ? (
                <ErrorNote message={errorMessage(requestMutation.error)} />
              ) : null}

              {showManualEntry ? (
                <details className="auth-manual" open>
                  <summary>输入 Discord 用户 ID</summary>
                  <form className="auth-manual-form" onSubmit={handleManualSubmit} noValidate>
                    <div className="field">
                      <label htmlFor="auth-manual-id">Discord 用户 ID</label>
                      <input
                        id="auth-manual-id"
                        className={manualError ? 'input input-invalid' : 'input'}
                        type="text"
                        inputMode="numeric"
                        value={manualId}
                        onChange={(event) => {
                          setManualError(null)
                          if (requestMutation.isError) requestMutation.reset()
                          setManualId(event.target.value)
                        }}
                        placeholder="例如 123456789012345678"
                        autoComplete="off"
                        aria-invalid={manualError ? true : undefined}
                        aria-describedby="auth-manual-hint"
                      />
                      <p className="form-hint" id="auth-manual-hint">
                        在 Discord 中开启开发者模式，右键自己的头像即可复制 ID。
                      </p>
                      {manualError ? (
                        <p className="field-error" role="alert">
                          {manualError}
                        </p>
                      ) : null}
                    </div>
                    <button
                      type="submit"
                      className="btn btn-primary btn-block"
                      disabled={requestMutation.isPending}
                    >
                      {requestMutation.isPending ? '正在发送…' : '发送验证码'}
                    </button>
                  </form>
                </details>
              ) : null}
            </div>
          </>
        )}

        <p className="auth-foot">
          <Icon name="book" size={14} />
          <span>练习进度将保存在账号里</span>
        </p>
      </div>
    </div>
  )
}
