// 本地 Discord 验证 API stub，只用于开发联调，不能当线上服务用。
// 用法：node scripts/verify-stub.mjs
// 环境变量：PORT（默认 5099）、HOST（默认所有网卡）、STUB_DELIVERY=dm|thread（默认 dm）、
//          STUB_REQUEST_INTERVAL_SECONDS（同一用户发码间隔，默认 15 秒；线上真 API 是 60 秒）
//
// 除 GET /api/verify/me（Bearer）和 /api/verify/dev/last-code 之外，
// 所有接口都要求非空的 X-Api-Key，任意值都接受（本地用 dev-key 即可）。
import http from 'node:http';
import { randomInt, randomUUID } from 'node:crypto';

const PORT = Number(process.env.PORT ?? 5099);
const HOST = process.env.HOST;
const DELIVERY = process.env.STUB_DELIVERY === 'thread' ? 'thread' : 'dm';

const GUILD_ID = '1170000000000000000';
const CHANNEL_ID = '1170000000000000001';
const CODE_TTL_MS = 5 * 60 * 1000;
const REQUEST_INTERVAL_SECONDS = Math.max(
  1,
  Number(process.env.STUB_REQUEST_INTERVAL_SECONDS ?? 15) || 15,
);
const REQUEST_INTERVAL_MS = REQUEST_INTERVAL_SECONDS * 1000;
const MAX_CODE_ATTEMPTS = 5;
const ACCESS_TTL_SECONDS = 30 * 60;
const ACCESS_TTL_MS = ACCESS_TTL_SECONDS * 1000;
const REFRESH_TTL_MS = 30 * 24 * 60 * 60 * 1000;

const MEMBERS = [
  {
    id: '568768836153769996',
    username: 'abohp',
    globalName: '阿博',
    nickname: '阿博',
    avatarUrl: 'https://cdn.discordapp.com/embed/avatars/0.png',
    roles: ['@everyone'],
    joinedAt: '2025-09-01T00:00:00.000Z',
  },
  {
    id: '468768836153769001',
    username: 'soupchef',
    globalName: 'Soup Chef',
    nickname: '汤厨',
    avatarUrl: 'https://cdn.discordapp.com/embed/avatars/1.png',
    roles: ['@everyone'],
    joinedAt: '2025-09-03T00:00:00.000Z',
  },
  {
    id: '468768836153769002',
    username: 'mycelium',
    globalName: 'Mycelium',
    nickname: null,
    avatarUrl: null,
    roles: ['@everyone'],
    joinedAt: '2025-09-08T00:00:00.000Z',
  },
];

const membersById = new Map(MEMBERS.map((member) => [member.id, member]));

/** requestId -> { userId, code, delivery, attempts, createdAt, expiresAt, consumed } */
const requests = new Map();
/** userId -> 最近一次发码记录，供 dev 接口读取 */
const lastCodeByUser = new Map();
/** userId -> 上次发码时间（毫秒） */
const lastRequestAt = new Map();
/** token -> { userId, expiresAt } */
const accessTokens = new Map();
const refreshTokens = new Map();

function generateCode() {
  const letters = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ';
  const code = letters[randomInt(letters.length)]
    + letters[randomInt(letters.length)]
    + String(randomInt(10))
    + String(randomInt(10));
  return code;
}

function issueTokens(userId) {
  const accessToken = `stub-access-${randomUUID()}`;
  const refreshToken = `stub-refresh-${randomUUID()}`;
  accessTokens.set(accessToken, { userId, expiresAt: Date.now() + ACCESS_TTL_MS });
  refreshTokens.set(refreshToken, { userId, expiresAt: Date.now() + REFRESH_TTL_MS });
  return { accessToken, refreshToken, expiresIn: ACCESS_TTL_SECONDS };
}

function publicMember(member) {
  return {
    id: member.id,
    username: member.username,
    globalName: member.globalName,
    nickname: member.nickname,
    avatarUrl: member.avatarUrl,
  };
}

function mePayload(member) {
  return {
    ...publicMember(member),
    guildId: GUILD_ID,
    roles: member.roles,
    joinedAt: member.joinedAt,
  };
}

function purgeStaleRequests() {
  const cutoff = Date.now() - CODE_TTL_MS;
  for (const [id, record] of requests) {
    if (record.expiresAt < cutoff) requests.delete(id);
  }
}

function send(req, res, status, payload, extraHeaders = {}) {
  const body = JSON.stringify(payload);
  res.writeHead(status, {
    'Content-Type': 'application/json; charset=utf-8',
    'Content-Length': Buffer.byteLength(body),
    'Cache-Control': 'no-store',
    ...extraHeaders,
  });
  res.end(body);
  const pathname = new URL(req.url ?? '/', 'http://localhost').pathname;
  console.log(`[verify-stub] ${req.method} ${pathname} -> ${status}`);
}

function readJson(req) {
  return new Promise((resolve, reject) => {
    let raw = '';
    let settled = false;
    const fail = (error) => {
      if (settled) return;
      settled = true;
      reject(error);
    };
    req.on('data', (chunk) => {
      raw += chunk;
      if (raw.length > 64 * 1024) {
        fail(Object.assign(new Error('请求体过大'), { status: 413 }));
        req.destroy();
      }
    });
    req.on('end', () => {
      if (settled) return;
      settled = true;
      if (!raw.trim()) return resolve({});
      try {
        resolve(JSON.parse(raw));
      } catch {
        reject(Object.assign(new Error('请求体不是合法 JSON'), { status: 400 }));
      }
    });
    req.on('error', (error) => fail(error));
  });
}

function hasApiKey(req) {
  const key = req.headers['x-api-key'];
  return typeof key === 'string' && key.trim() !== '';
}

function requireApiKey(req, res) {
  if (hasApiKey(req)) return true;
  send(req, res, 401, { error: '缺少 X-Api-Key，本地随便填个非空值即可（如 dev-key）' });
  return false;
}

function bearerToken(req) {
  const header = req.headers.authorization;
  if (typeof header !== 'string') return null;
  const match = /^Bearer\s+(.+)$/i.exec(header.trim());
  return match ? match[1].trim() : null;
}

async function handleRequestCode(req, res) {
  const body = await readJson(req);
  const userId = typeof body.userId === 'string' ? body.userId.trim() : '';
  if (!userId) {
    send(req, res, 400, { error: '缺少 userId' });
    return;
  }

  const member = membersById.get(userId);
  if (!member) {
    send(req, res, 404, { error: `用户 ${userId} 不在服务器中` });
    return;
  }

  const now = Date.now();
  const previous = lastRequestAt.get(userId);
  if (previous !== undefined && now - previous < REQUEST_INTERVAL_MS) {
    const waitSeconds = Math.ceil((REQUEST_INTERVAL_MS - (now - previous)) / 1000);
    send(
      req,
      res,
      429,
      { error: `同一用户 ${REQUEST_INTERVAL_SECONDS} 秒只能申请一次，请 ${waitSeconds} 秒后重试` },
      {
        'Retry-After': String(waitSeconds),
      },
    );
    return;
  }

  purgeStaleRequests();

  const delivery = body.delivery === 'dm' || body.delivery === 'thread' ? body.delivery : DELIVERY;
  const code = generateCode();
  const requestId = `stub-req-${randomUUID()}`;
  const expiresAt = now + CODE_TTL_MS;

  for (const [id, record] of requests) {
    if (record.userId === userId && !record.consumed) requests.delete(id);
  }

  requests.set(requestId, {
    userId,
    code,
    delivery,
    attempts: 0,
    createdAt: now,
    expiresAt,
    consumed: false,
  });
  lastRequestAt.set(userId, now);
  lastCodeByUser.set(userId, {
    userId,
    code,
    requestId,
    delivery,
    requestedAt: now,
    expiresAt,
  });

  const label = member.nickname ? `${member.username}（${member.nickname}）` : member.username;
  console.log(`[verify-stub] 发送验证码 ${code} -> ${label}，投递方式 ${delivery}，5 分钟内有效`);
  send(req, res, 200, {
    requestId,
    expiresAt: new Date(expiresAt).toISOString(),
    delivery,
  });
}

async function handleConfirm(req, res) {
  const body = await readJson(req);
  const requestId = typeof body.requestId === 'string' ? body.requestId.trim() : '';
  const code = typeof body.code === 'string' ? body.code.trim().toUpperCase() : '';
  if (!requestId || !code) {
    send(req, res, 400, { error: '缺少 requestId 或 code' });
    return;
  }

  const record = requests.get(requestId);
  if (!record) {
    send(req, res, 400, { error: '验证码无效或已过期' });
    return;
  }
  if (record.consumed) {
    send(req, res, 400, { error: '验证码已使用，请重新申请' });
    return;
  }
  if (Date.now() > record.expiresAt) {
    requests.delete(requestId);
    send(req, res, 400, { error: '验证码已过期，请重新申请' });
    return;
  }

  if (record.code !== code) {
    record.attempts += 1;
    if (record.attempts >= MAX_CODE_ATTEMPTS) {
      requests.delete(requestId);
      send(req, res, 400, { error: '验证码错误次数过多，请重新申请' });
      return;
    }
    send(req, res, 400, {
      error: `验证码不正确，还可尝试 ${MAX_CODE_ATTEMPTS - record.attempts} 次`,
    });
    return;
  }

  record.consumed = true;
  const member = membersById.get(record.userId);
  console.log(`[verify-stub] 验证码通过：${member?.username ?? record.userId}，签发 access/refresh token`);
  send(req, res, 200, issueTokens(record.userId));
}

async function handleRefresh(req, res) {
  const body = await readJson(req);
  const refreshToken = typeof body.refreshToken === 'string' ? body.refreshToken.trim() : '';
  if (!refreshToken) {
    send(req, res, 400, { error: '缺少 refreshToken' });
    return;
  }

  const record = refreshTokens.get(refreshToken);
  if (!record || record.expiresAt < Date.now()) {
    refreshTokens.delete(refreshToken);
    send(req, res, 401, { error: 'refresh token 无效或已过期' });
    return;
  }

  refreshTokens.delete(refreshToken);
  send(req, res, 200, issueTokens(record.userId));
}

function handleMe(req, res) {
  const token = bearerToken(req);
  if (!token) {
    send(req, res, 401, { error: '缺少 Bearer access token' });
    return;
  }

  const record = accessTokens.get(token);
  if (!record || record.expiresAt < Date.now()) {
    accessTokens.delete(token);
    send(req, res, 401, { error: 'access token 无效或已过期' });
    return;
  }

  const member = membersById.get(record.userId);
  if (!member) {
    send(req, res, 401, { error: '令牌对应的用户已不在服务器中' });
    return;
  }

  send(req, res, 200, mePayload(member));
}

function handleDevLastCode(req, res, url) {
  const userId = (url.searchParams.get('userId') ?? '').trim();
  if (!userId) {
    send(req, res, 400, { error: '缺少 userId 查询参数' });
    return;
  }

  const last = lastCodeByUser.get(userId);
  if (!last) {
    send(req, res, 404, { error: `用户 ${userId} 还没有申请过验证码` });
    return;
  }

  const record = requests.get(last.requestId);
  send(req, res, 200, {
    userId,
    code: last.code,
    requestId: last.requestId,
    delivery: last.delivery,
    expiresAt: new Date(last.expiresAt).toISOString(),
    expired: Date.now() > last.expiresAt,
    used: record ? record.consumed : true,
  });
}

async function route(req, res, pathname, url) {
  const method = req.method ?? 'GET';

  if (method === 'GET' && pathname === '/api/verify/status') {
    if (!requireApiKey(req, res)) return;
    send(req, res, 200, {
      configured: true,
      guildId: GUILD_ID,
      channelId: CHANNEL_ID,
      codeTtlMinutes: CODE_TTL_MS / 60000,
      accessTokenMinutes: ACCESS_TTL_SECONDS / 60,
      refreshTokenDays: REFRESH_TTL_MS / (24 * 60 * 60 * 1000),
    });
    return;
  }

  if (method === 'GET' && pathname === '/api/verify/members') {
    if (!requireApiKey(req, res)) return;
    const members = MEMBERS.map(publicMember);
    send(req, res, 200, {
      guildId: GUILD_ID,
      channelId: CHANNEL_ID,
      count: members.length,
      members,
    });
    return;
  }

  if (method === 'POST' && pathname === '/api/verify/request') {
    if (!requireApiKey(req, res)) return;
    await handleRequestCode(req, res);
    return;
  }

  if (method === 'POST' && pathname === '/api/verify/confirm') {
    if (!requireApiKey(req, res)) return;
    await handleConfirm(req, res);
    return;
  }

  if (method === 'GET' && pathname === '/api/verify/me') {
    handleMe(req, res);
    return;
  }

  if (method === 'POST' && pathname === '/api/verify/token') {
    if (!requireApiKey(req, res)) return;
    await handleRefresh(req, res);
    return;
  }

  // 仅 stub 提供的开发接口：给 seed 脚本读最近一次验证码，不校验 API Key。
  if (method === 'GET' && pathname === '/api/verify/dev/last-code') {
    handleDevLastCode(req, res, url);
    return;
  }

  send(req, res, 404, { error: `未知接口：${method} ${pathname}` });
}

const server = http.createServer((req, res) => {
  const url = new URL(req.url ?? '/', 'http://localhost');
  const pathname = url.pathname.replace(/\/+$/, '') || '/';
  route(req, res, pathname, url).catch((error) => {
    const status = error.status ?? 500;
    console.error(`[verify-stub] 处理 ${req.method} ${pathname} 失败：`, error);
    send(req, res, status, { error: status === 500 ? 'stub 内部错误' : error.message });
  });
});

function onListening() {
  console.log(`[verify-stub] 监听 http://localhost:${PORT}（本地开发用的假 Discord 验证 API，不能用于线上）`);
  console.log('[verify-stub] 成员列表：');
  for (const member of MEMBERS) {
    const label = member.nickname ? `${member.username} / ${member.nickname}` : member.username;
    console.log(`  - ${member.id}  ${label}`);
  }
  console.log('[verify-stub] 验证码会打印在这里');
  console.log('[verify-stub] 任意非空 X-Api-Key 都接受，本地用 dev-key 即可');
}

if (HOST) server.listen(PORT, HOST, onListening);
else server.listen(PORT, onListening);

for (const signal of ['SIGINT', 'SIGTERM']) {
  process.on(signal, () => {
    console.log(`\n[verify-stub] 收到 ${signal}，退出`);
    server.close(() => process.exit(0));
    setTimeout(() => process.exit(0), 1000).unref();
  });
}
