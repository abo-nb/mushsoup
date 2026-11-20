export type MemoryAlgorithm = 'sm2' | 'speed' | 'fsrs' | 'leitner'

export interface AlgorithmOption {
  value: MemoryAlgorithm
  label: string
  hint: string
}

export const ALGORITHM_OPTIONS: AlgorithmOption[] = [
  {
    value: 'sm2',
    label: '间隔复习',
    hint: 'SM-2：先走两步短学习步，再按 1 天、6 天、间隔 × ease 拉长；答错回到学习步。',
  },
  {
    value: 'speed',
    label: '速度优先',
    hint: '答对一次就算学会；答错的题隔一题再出现。',
  },
  {
    value: 'fsrs',
    label: '保持率优先',
    hint: 'FSRS：按 90% 的目标保持率排间隔，答得越吃力，下次来得越早。',
  },
  {
    value: 'leitner',
    label: '固定阶梯',
    hint: '六个盒子，答对进一格、答错回第一格，间隔固定 1 / 2 / 4 / 8 / 16 / 32 天。',
  },
]

export const ALGORITHM_STORAGE_KEY = 'ms_memory_algorithm'

export function isMemoryAlgorithm(value: string | null | undefined): value is MemoryAlgorithm {
  return value === 'sm2' || value === 'speed' || value === 'fsrs' || value === 'leitner'
}

/** 算法在界面上显示的名字；未知值按默认算法处理。 */
export function algorithmLabel(value: string | null | undefined): string {
  return ALGORITHM_OPTIONS.find((option) => option.value === value)?.label ?? ALGORITHM_OPTIONS[0].label
}

/** 上一次在开始弹窗里选的算法；没有就返回 null，由调用方回落到账号设置。 */
export function readStoredAlgorithm(): MemoryAlgorithm | null {
  try {
    const stored = window.localStorage.getItem(ALGORITHM_STORAGE_KEY)
    return isMemoryAlgorithm(stored) ? stored : null
  } catch {
    return null
  }
}

export function storeAlgorithm(value: MemoryAlgorithm) {
  try {
    window.localStorage.setItem(ALGORITHM_STORAGE_KEY, value)
  } catch {
    // 隐私模式下写入会失败，本次选择仍然生效。
  }
}
