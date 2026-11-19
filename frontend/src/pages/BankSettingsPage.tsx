import { useMemo, useState } from 'react'
import type { FormEvent } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, errorMessage } from '../api'
import type { Bank, VerifyMember } from '../api'
import { Avatar } from '../components/Avatar'
import { Icon } from '../components/Icon'
import { TagInput } from '../components/TagInput'
import { ErrorNote, Loading } from '../components/Ui'

const VISIBILITY_OPTIONS = [
  { value: 'private', label: '私有', hint: '只有你和你添加的成员能看到' },
  { value: 'public', label: '公开', hint: '所有人能在公开题库里看到并练习' },
]

export function BankSettingsPage() {
  const { id } = useParams<{ id: string }>()
  const bankId = id ?? ''

  const accessQuery = useQuery({
    queryKey: ['bank', bankId],
    queryFn: () => api.bank(bankId),
    enabled: Boolean(bankId),
    staleTime: 60_000,
  })

  if (!bankId) {
    return <ErrorNote message="找不到这个题库。" />
  }

  if (accessQuery.isPending) {
    return <Loading full />
  }

  if (accessQuery.isError) {
    return (
      <div>
        <Link to="/banks" className="back-link">
          ← 返回题库
        </Link>
        <ErrorNote message={errorMessage(accessQuery.error)} />
      </div>
    )
  }

  const access = accessQuery.data

  return (
    <div>
      <Link to={`/banks/${access.bank.id}`} className="back-link">
        ← 返回题库
      </Link>

      <header className="page-header">
        <div className="page-heading">
          <p className="eyebrow">题库设置</p>
          <h1>{access.bank.name}</h1>
          <p className="page-sub">管理题库信息、可见性与成员。</p>
        </div>
      </header>

      {access.isOwner ? (
        <BankSettingsForm key={access.bank.id} bank={access.bank} />
      ) : (
        <ErrorNote message="只有题库创建者可以修改设置" />
      )}
    </div>
  )
}

function BankSettingsForm({ bank }: { bank: Bank }) {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [name, setName] = useState(bank.name)
  const [description, setDescription] = useState(bank.description ?? '')
  const [year, setYear] = useState(bank.year ?? '')
  const [semester, setSemester] = useState(
    bank.semester === '上' || bank.semester === '下' ? bank.semester : '',
  )
  const [tags, setTags] = useState(bank.tags ?? [])
  const [visibility, setVisibility] = useState(
    bank.visibility === 'public' ? 'public' : 'private',
  )
  const [shuffleOptions, setShuffleOptions] = useState(bank.settings.defaultShuffleOptions)
  const [selectedMemberId, setSelectedMemberId] = useState('')

  const verifyQuery = useQuery({
    queryKey: ['auth', 'verify-members'],
    queryFn: () => api.verifyMembers(),
    staleTime: 60_000,
  })

  function invalidateBank() {
    void queryClient.invalidateQueries({ queryKey: ['bank', bank.id] })
    void queryClient.invalidateQueries({ queryKey: ['banks'] })
  }

  const updateMutation = useMutation({
    mutationFn: () =>
      api.updateBank(bank.id, {
        name: name.trim(),
        description: description.trim(),
        year: year.trim(),
        semester,
        visibility,
        tags,
        settings: {
          defaultShuffleOptions: shuffleOptions,
          requireReview: bank.settings.requireReview,
        },
      }),
    onSuccess: invalidateBank,
  })

  const addMemberMutation = useMutation({
    mutationFn: (userId: string) => api.addBankMember(bank.id, userId),
    onSuccess: () => {
      setSelectedMemberId('')
      invalidateBank()
    },
  })

  const removeMemberMutation = useMutation({
    mutationFn: (userId: string) => api.removeBankMember(bank.id, userId),
    onSuccess: invalidateBank,
  })

  const deleteMutation = useMutation({
    mutationFn: () => api.deleteBank(bank.id),
    onSuccess: () => {
      queryClient.removeQueries({ queryKey: ['bank', bank.id] })
      void queryClient.invalidateQueries({ queryKey: ['banks'] })
      void queryClient.invalidateQueries({ queryKey: ['notes'] })
      navigate('/')
    },
  })

  const memberMap = useMemo(() => {
    const map = new Map<string, VerifyMember>()
    for (const member of verifyQuery.data?.members ?? []) {
      map.set(member.id, member)
    }
    return map
  }, [verifyQuery.data])

  const candidates = useMemo(() => {
    const members = verifyQuery.data?.members ?? []
    return members.filter(
      (member) => member.id !== bank.ownerId && !bank.memberIds.includes(member.id),
    )
  }, [verifyQuery.data, bank.ownerId, bank.memberIds])

  const selectedCandidate = candidates.some((member) => member.id === selectedMemberId)
    ? selectedMemberId
    : ''

  const missingPublicTags = visibility === 'public' && tags.length === 0

  function markDirty() {
    if (updateMutation.isSuccess) updateMutation.reset()
  }

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    updateMutation.mutate()
  }

  function handleDelete() {
    if (deleteMutation.isPending) return
    if (!window.confirm('确定删除这个题库吗？删除后无法恢复。')) return
    deleteMutation.mutate()
  }

  return (
    <form className="settings-stack" onSubmit={handleSubmit}>
      <section className="card card-pad settings-section">
        <div className="settings-head">
          <div>
            <h2 className="section-title">基本信息</h2>
            <p className="muted settings-desc">名称、简介和学科标签会显示在题库卡片上。</p>
          </div>
        </div>

        <div className="field">
          <label htmlFor="bank-settings-name">名称</label>
          <input
            id="bank-settings-name"
            className="input"
            type="text"
            required
            maxLength={80}
            value={name}
            onChange={(event) => {
              markDirty()
              setName(event.target.value)
            }}
          />
        </div>

        <div className="field">
          <label htmlFor="bank-settings-description">简介（选填）</label>
          <textarea
            id="bank-settings-description"
            className="textarea"
            rows={3}
            maxLength={300}
            value={description}
            onChange={(event) => {
              markDirty()
              setDescription(event.target.value)
            }}
            placeholder="简单说明这个题库的内容"
          />
        </div>

        <div className="form-row">
          <div className="field">
            <label htmlFor="bank-settings-year">学年</label>
            <input
              id="bank-settings-year"
              className="input"
              type="text"
              maxLength={20}
              value={year}
              onChange={(event) => {
                markDirty()
                setYear(event.target.value)
              }}
              placeholder="如 高一 / 2025-2026"
            />
          </div>

          <div className="field">
            <label htmlFor="bank-settings-semester">学期</label>
            <select
              id="bank-settings-semester"
              className="select"
              value={semester}
              onChange={(event) => {
                markDirty()
                setSemester(event.target.value)
              }}
            >
              <option value="">未设置</option>
              <option value="上">上学期</option>
              <option value="下">下学期</option>
            </select>
          </div>
        </div>

        <TagInput
          label="学科标签"
          value={tags}
          onChange={(next) => {
            markDirty()
            setTags(next)
          }}
          hint="公开题库至少需要一个标签；数学 / 數學 视为同一科"
        />
        {missingPublicTags ? (
          <p className="settings-tags-warning" role="status">
            公开题库至少需要一个学科标签才能保存。
          </p>
        ) : null}
      </section>

      <section className="card card-pad settings-section">
        <div className="settings-head">
          <div>
            <h2 className="section-title" id="bank-visibility-title">
              可见性
            </h2>
            <p className="muted settings-desc">公开后所有人可见。</p>
          </div>
        </div>

        <div className="radio-cards" role="radiogroup" aria-labelledby="bank-visibility-title">
          {VISIBILITY_OPTIONS.map((option) => (
            <label
              key={option.value}
              className={visibility === option.value ? 'radio-card selected' : 'radio-card'}
            >
              <input
                type="radio"
                name="bank-visibility"
                value={option.value}
                checked={visibility === option.value}
                onChange={() => {
                  markDirty()
                  setVisibility(option.value)
                }}
              />
              <span className="radio-card-text">
                <strong>{option.label}</strong>
                <small>{option.hint}</small>
              </span>
              <span className="radio-card-check" aria-hidden="true">
                {visibility === option.value ? <Icon name="check" size={16} /> : null}
              </span>
            </label>
          ))}
        </div>
      </section>

      <section className="card card-pad settings-section">
        <div className="settings-head">
          <div>
            <h2 className="section-title" id="bank-shuffle-title">
              默认选项乱序
            </h2>
            <p className="muted settings-desc">
              练习时如果题目没有单独设置，就使用这里的默认值。
            </p>
          </div>
        </div>

        <div className="radio-cards" role="radiogroup" aria-labelledby="bank-shuffle-title">
          <label className={shuffleOptions ? 'radio-card selected' : 'radio-card'}>
            <input
              type="radio"
              name="bank-shuffle"
              value="shuffle"
              checked={shuffleOptions}
              onChange={() => {
                markDirty()
                setShuffleOptions(true)
              }}
            />
            <span className="radio-card-text">
              <strong>打乱</strong>
              <small>练习时默认打乱选项顺序</small>
            </span>
            <span className="radio-card-check" aria-hidden="true">
              {shuffleOptions ? <Icon name="check" size={16} /> : null}
            </span>
          </label>
          <label className={!shuffleOptions ? 'radio-card selected' : 'radio-card'}>
            <input
              type="radio"
              name="bank-shuffle"
              value="keep"
              checked={!shuffleOptions}
              onChange={() => {
                markDirty()
                setShuffleOptions(false)
              }}
            />
            <span className="radio-card-text">
              <strong>保持</strong>
              <small>练习时保持题目中的选项顺序</small>
            </span>
            <span className="radio-card-check" aria-hidden="true">
              {!shuffleOptions ? <Icon name="check" size={16} /> : null}
            </span>
          </label>
        </div>
      </section>

      {updateMutation.isError ? (
        <ErrorNote message={errorMessage(updateMutation.error)} />
      ) : null}

      <div className="form-actions bank-settings-actions">
        <span
          className="bank-settings-status"
          data-state={updateMutation.isSuccess ? 'saved' : undefined}
          role="status"
          aria-live="polite"
        >
          {updateMutation.isPending ? '保存中…' : updateMutation.isSuccess ? '已保存' : ''}
        </span>
        <button
          type="submit"
          className="btn btn-primary"
          disabled={
            !name.trim() || name.trim().length > 80 || missingPublicTags || updateMutation.isPending
          }
        >
          {updateMutation.isPending ? '保存中…' : '保存设置'}
        </button>
      </div>

      <section className="card card-pad settings-section">
        <div className="settings-head">
          <div>
            <h2 className="section-title" id="bank-members-title">
              成员
            </h2>
            <p className="muted settings-desc">成员可以查看、练习并编辑这个题库。</p>
          </div>
          <span className="settings-status">{bank.memberIds.length} 人</span>
        </div>

        {bank.memberIds.length > 0 ? (
          <ul className="member-list" aria-labelledby="bank-members-title">
            {bank.memberIds.map((memberId) => {
              const member = memberMap.get(memberId)
              const label = member?.displayName ?? memberId
              return (
                <li key={memberId} className="member-row">
                  <Avatar name={label} src={member?.avatarUrl ?? null} size="sm" />
                  <span className="member-text">
                    <span className="member-name">{label}</span>
                    {member ? <span className="member-username">@{member.username}</span> : null}
                  </span>
                  <button
                    type="button"
                    className="btn btn-small btn-danger-ghost"
                    onClick={() => removeMemberMutation.mutate(memberId)}
                    disabled={removeMemberMutation.isPending}
                    aria-label={`移除 ${label}`}
                  >
                    移除
                  </button>
                </li>
              )
            })}
          </ul>
        ) : (
          <p className="form-hint">还没有成员。</p>
        )}

        {verifyQuery.isPending ? <p className="form-hint">正在读取验证频道成员…</p> : null}
        {verifyQuery.isError ? <ErrorNote message={errorMessage(verifyQuery.error)} /> : null}

        {candidates.length > 0 ? (
          <div className="bank-settings-add">
            <div className="field">
              <label htmlFor="bank-settings-member">添加成员</label>
              <select
                id="bank-settings-member"
                className="select"
                value={selectedCandidate}
                onChange={(event) => setSelectedMemberId(event.target.value)}
              >
                <option value="">选择验证频道成员</option>
                {candidates.map((member) => (
                  <option key={member.id} value={member.id}>
                    {member.displayName}
                    {member.username ? ` (@${member.username})` : ''}
                  </option>
                ))}
              </select>
            </div>
            <button
              type="button"
              className="btn btn-primary"
              onClick={() => addMemberMutation.mutate(selectedCandidate)}
              disabled={!selectedCandidate || addMemberMutation.isPending}
            >
              {addMemberMutation.isPending ? '添加中…' : '添加'}
            </button>
          </div>
        ) : verifyQuery.data ? (
          <p className="form-hint">验证频道里没有可添加的成员。</p>
        ) : null}

        {addMemberMutation.isError ? (
          <ErrorNote message={errorMessage(addMemberMutation.error)} />
        ) : null}
        {removeMemberMutation.isError ? (
          <ErrorNote message={errorMessage(removeMemberMutation.error)} />
        ) : null}
      </section>

      <section className="card card-pad settings-section bank-danger">
        <div className="settings-head">
          <div>
            <h2 className="section-title">删除题库</h2>
            <p className="muted settings-desc">
              删除后这个题库会从题库列表和公开内容中移除，无法恢复。
            </p>
          </div>
        </div>

        {deleteMutation.isError ? (
          <ErrorNote message={errorMessage(deleteMutation.error)} />
        ) : null}

        <div className="bank-danger-actions">
          <button
            type="button"
            className="btn btn-danger-ghost"
            onClick={handleDelete}
            disabled={deleteMutation.isPending}
          >
            {deleteMutation.isPending ? '删除中…' : '删除题库'}
          </button>
        </div>
      </section>
    </form>
  )
}
