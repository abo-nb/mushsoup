// 生成一份可以直接刷的演示数据。幂等：重复运行不会重复建库建题。
// 用法：先启动 scripts/verify-stub.mjs 和后端，然后 node scripts/seed.mjs
import { readFileSync } from 'node:fs';

const BASE = process.env.API_BASE ?? 'http://localhost:5080';
const STUB_BASE = process.env.VERIFY_STUB_BASE ?? 'http://localhost:5099';
const USER_ID = '568768836153769996';
const BANK_NAME = '演示 · 细胞生物学';

let cookie = '';

async function call(method, path, body) {
  const headers = {};
  if (cookie) headers.Cookie = cookie;
  if (body !== undefined) headers['Content-Type'] = 'application/json';
  const res = await fetch(BASE + path, {
    method,
    headers,
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  for (const c of res.headers.getSetCookie?.() ?? []) cookie = c.split(';')[0];
  const text = await res.text();
  let json = null;
  try { json = text ? JSON.parse(text) : null; } catch { json = text; }
  if (!res.ok) {
    const detail = json?.detail ?? text;
    const errors = json?.errors ? ' ' + JSON.stringify(json.errors) : '';
    throw new Error(`${method} ${path} -> ${res.status} ${detail}${errors}`);
  }
  return json;
}

async function lastStubCode(userId) {
  const url = `${STUB_BASE}/api/verify/dev/last-code?userId=${encodeURIComponent(userId)}`;
  let res;
  try {
    res = await fetch(url);
  } catch {
    throw new Error(`连不上验证 stub（${STUB_BASE}）。请先运行 node scripts/verify-stub.mjs`);
  }
  const text = await res.text();
  let json = null;
  try { json = text ? JSON.parse(text) : null; } catch { json = text; }
  if (!res.ok) {
    const detail = json?.error ?? text;
    throw new Error(`stub 没有可用的验证码：${detail}。请先运行 node scripts/verify-stub.mjs`);
  }
  return json.code;
}

async function login() {
  let ticket;
  try {
    ticket = await call('POST', '/api/auth/verify/request', { userId: USER_ID });
  } catch (error) {
    throw new Error(
      `申请 Discord 验证码失败：${error.message}\n`
      + `请确认后端已启动（${BASE}），验证 stub 正在 ${STUB_BASE} 运行（node scripts/verify-stub.mjs）`,
    );
  }

  const code = await lastStubCode(USER_ID);

  try {
    const user = await call('POST', '/api/auth/verify/confirm', { ticketId: ticket.ticketId, code });
    return { user, delivery: ticket.delivery };
  } catch (error) {
    throw new Error(
      `提交验证码失败：${error.message}\n`
      + `验证码 ${code} 来自 stub；若提示太频繁、已过期或错误次数过多，等一会儿（stub 默认 15 秒）后重跑本脚本`,
    );
  }
}

const questions = [
  {
    type: 'single_choice',
    stem: '下列关于线粒体的叙述，**错误**的是：',
    options: [
      { key: 'A', text: '具有双层膜结构' },
      { key: 'B', text: '是有氧呼吸的主要场所' },
      { key: 'C', text: '含独立的环状 DNA' },
      { key: 'D', text: '由高尔基体直接分裂产生' },
    ],
    answer: ['D'],
    explanation: '线粒体由已有的线粒体分裂而来，不来自高尔基体。',
    tags: ['细胞器', '线粒体'],
    difficulty: 2,
  },
  {
    type: 'multiple_choice',
    stem: '下列属于细胞器的是：',
    options: [
      { key: 'A', text: '线粒体' },
      { key: 'B', text: '核糖体' },
      { key: 'C', text: '细胞核' },
      { key: 'D', text: '高尔基体' },
    ],
    answer: ['A', 'B', 'D'],
    explanation: '细胞核由核膜包被，是一个独立的结构，不属于细胞器。',
    tags: ['细胞器'],
    difficulty: 1,
  },
  {
    type: 'single_choice',
    stem: '细胞膜的基本骨架是：',
    options: [
      { key: 'A', text: '蛋白质双分子层' },
      { key: 'B', text: '磷脂双分子层' },
      { key: 'C', text: '多糖链' },
      { key: 'D', text: '胆固醇单层' },
    ],
    answer: ['B'],
    explanation: '磷脂双分子层构成细胞膜的基本骨架，蛋白质镶嵌或贯穿其中。',
    tags: ['细胞膜'],
    difficulty: 1,
  },
  {
    type: 'single_choice',
    stem: '已知某细胞器的直径约为 $2\\,\\mu m$，用光学显微镜观察时，其放大倍数为 $400\\times$。像的直径约为多少？',
    options: [
      { key: 'A', text: '$0.8\\,mm$' },
      { key: 'B', text: '$8\\,mm$' },
      { key: 'C', text: '$0.08\\,mm$' },
      { key: 'D', text: '$80\\,mm$' },
    ],
    answer: ['A'],
    explanation: '$2\\,\\mu m \\times 400 = 800\\,\\mu m = 0.8\\,mm$。',
    tags: ['显微镜'],
    difficulty: 3,
  },
  {
    type: 'multiple_choice',
    stem: '下列过程需要消耗 ATP 的有：',
    options: [
      { key: 'A', text: '主动运输' },
      { key: 'B', text: '自由扩散' },
      { key: 'C', text: '蛋白质合成' },
      { key: 'D', text: '有丝分裂' },
    ],
    answer: ['A', 'C', 'D'],
    explanation: '自由扩散顺浓度梯度，不消耗能量；其余均需 ATP。',
    tags: ['代谢'],
    difficulty: 2,
  },
];

let session;
try {
  session = await login();
} catch (error) {
  console.error(`✗ ${error.message}`);
  process.exit(1);
}

const who = session.user?.displayName
  ?? session.user?.nickname
  ?? session.user?.globalName
  ?? USER_ID;
console.log(`✓ Discord 验证码登录成功：${who}（投递方式 ${session.delivery ?? 'dm'}）`);

const banks = await call('GET', '/api/banks');
let bank = banks.find((b) => b.name === BANK_NAME);

if (!bank) {
  bank = await call('POST', '/api/banks', {
    name: BANK_NAME,
    description: '脚本生成的演示题库，可以直接开始练习。',
    tags: ['生物'],
  });
  console.log(`✓ 创建题库：${bank.name}`);
} else {
  console.log(`· 题库已存在：${bank.name}`);
}

const existing = await call('GET', `/api/banks/${bank.id}/questions`);
if (existing.length > 0) {
  console.log(`· 题库已有 ${existing.length} 道题，跳过写入`);
} else {
  for (const question of questions) {
    await call('POST', `/api/banks/${bank.id}/questions`, question);
  }
  console.log(`✓ 写入 ${questions.length} 道题`);
}

const noteFile = new URL('./content/enterprise-management-101.json', import.meta.url);
const noteDocument = JSON.parse(readFileSync(noteFile, 'utf8'));
const notes = await call('GET', '/api/notes');
const existingNote = notes.find((item) => item.title === noteDocument.title);
let noteId;

if (existingNote) {
  const updated = await call('PUT', `/api/notes/${existingNote.id}`, noteDocument);
  noteId = updated.id;
  console.log(`· 讲义已存在，已更新：${noteDocument.title}`);
} else {
  const created = await call('POST', '/api/notes', noteDocument);
  noteId = created.id;
  console.log(`✓ 创建讲义：${noteDocument.title}（${noteDocument.sections.length} 节）`);
}

console.log(`\n打开 http://localhost:5173 ，用 stub 里的 Discord 身份（abohp / 阿博）登录后即可开始练习。`);
console.log(`讲义：http://localhost:5173/notes/${noteId}`);
