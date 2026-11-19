# 蘑菇汤题库 · MushroomSoup

班级内共享题库 + 记忆算法驱动的刷题网站。

已实现：**P1a**（登录、题库、单选/多选、练习判分）、**P1b**（公用选项池）、**P2**（记忆调度、每日复习、无限刷题）、**P1c**（批量导入、校验预览、LLM 提示词）、**P3-lite**（手动公开、公开内容页、题库设置、成员共享、学科标签、题目图片上传）、**讲义**（结构化 JSON 块、深层目录、阅读/设置/导入页）、**章节练习**、**深色模式**、**排行榜**（答对率 / 资料贡献 / 刷题量）、**最近**（最近常用 + 最新公开）、**分享短链接**（游客可读可练、不保存）、**继续练习**、**简体/繁体切换**、**收藏**。

## 技术栈

| 层 | 选择 |
|---|---|
| 后端 | .NET 10 · ASP.NET Core Minimal API · MongoDB.Driver |
| 数据库 | MongoDB（默认连接 Firestore 兼容的托管实例；也可切回内嵌 mongod） |
| 前端 | React 19 · TypeScript · Vite · TanStack Query · react-markdown + KaTeX |
| 认证 | 外部 Discord 验证码登录（4 位码）+ Cookie 会话（同源部署，SameSite=Lax） |

## 快速开始

需要 .NET 10 SDK、Node 20+、pnpm。

### 1. 启动本地验证 stub

```bash
node scripts/verify-stub.mjs
```

监听 `http://localhost:5099`，模拟外部的 Discord 验证 API：成员列表、发码、校验、token。验证码会打印在它的终端里。

> 这个 stub 只用于本地开发，不能当线上服务用：没有真实 Discord 服务器，验证码直接打在控制台，任意非空 `X-Api-Key` 都会被接受。真实部署见下面的「认证」。

### 2. 启动后端

`appsettings.Development.json` 已经让后端指向 stub（这是服务端配置，前端拿不到），直接启动即可：

```bash
cd backend
dotnet run --project src/Host/Host.csproj
```

需要换地址时用环境变量覆盖，例如接真实服务：`Verification__BaseUrl=https://ialine2dc.onrender.com Verification__ApiKey=<key> dotnet run --project src/Host/Host.csproj`。

监听 `http://localhost:5080`。仓库里的默认配置使用内嵌 mongod（首次启动会下载约 250MB，数据落在 `backend/src/Host/data/mongo`），不需要本机安装 MongoDB；首次登录会自动创建默认班级。

本地要连远程/托管 MongoDB 时，把连接串和验证 Key 写进 `backend/src/Host/appsettings.Local.json`（已被 gitignore，不会进仓库）：

```json
{
  "Mongo": {
    "UseEmbedded": false,
    "ConnectionString": "mongodb://...",
    "Database": "soupdb",
    "IgnoreIndexErrors": true
  },
  "Verification": {
    "BaseUrl": "https://ialine2dc.onrender.com",
    "ApiKey": "<key>"
  }
}
```

也可以改用环境变量覆盖（优先级高于本地 JSON）：`Mongo__ConnectionString=... Verification__ApiKey=... dotnet run --project src/Host/Host.csproj`。

> 远程实例是 Firestore 的 MongoDB 兼容层，不允许客户端建索引。`Mongo:IgnoreIndexErrors` 已设为 `true`：启动时会打几条 index setup skipped 的 warning，然后继续运行，这是预期行为。
> 连接串与 API Key 只放在服务端配置或环境变量里，不要提交进仓库。
> 想清空本地内嵌库：先正常停止后端（Ctrl+C，让内嵌 mongod 一起退出），再删除 `backend/src/Host/data`。直接强杀进程会留下 mongod，数据目录删不干净。

### 3. 启动前端

```bash
cd frontend
pnpm install
pnpm dev
```

打开 `http://localhost:5173`。开发服务器把 `/api` 代理到后端，不存在跨域和 Cookie 问题。

### 4. 生成演示数据（可选）

```bash
node scripts/seed.mjs
```

脚本不读密码：它先让后端向验证 stub 申请验证码，再从 stub 的开发接口取出该验证码完成登录（身份 `abohp` / 阿博），然后创建一个含 5 道题的「演示 · 细胞生物学」题库。脚本幂等。

> stub 默认对同一用户 15 秒只发一次码（`STUB_REQUEST_INTERVAL_SECONDS` 可改）。刚跑过就立刻重跑会被拒绝，稍等即可。线上真 API 的间隔是 60 秒。
> 看不到验证码时，stub 的终端里每次发码都会打印一行。

## 部署到 Render

仓库根目录的 `render.yaml` 是 Render Blueprint，前后端一起部署成一个 Docker Web Service：构建阶段先 `pnpm build` 前端，把 `dist` 放进后端 `wwwroot`，运行时由 ASP.NET Core 同源提供页面、静态资源和 `/api`。

1. 在 Render 新建 Blueprint，选择 `abo-nb/mushsoup`，它会读取根目录的 `render.yaml`；
2. 按提示填写两个 `sync: false` 的环境变量：
   - `Mongo__ConnectionString`：MongoDB 连接串（含凭据）
   - `Verification__ApiKey`：外部验证服务的 Key
3. `Mongo__Database`、`Verification__BaseUrl` 已写在 `render.yaml` 里，可按需修改。

不用 Blueprint、手动 New → Web Service 建服务也可以：Dockerfile 已经把 `ASPNETCORE_ENVIRONMENT=Production`、`Mongo__UseEmbedded=false`、`/tmp` 下的可写路径都定好了，`appsettings.Production.json` 补了生产默认值，所以同样只需要填 `Mongo__ConnectionString` 和 `Verification__ApiKey` 两个环境变量。

`Mongo__UseEmbedded` 必须是 `false`：运行镜像以非 root 用户跑、`/app` 不可写，也没有 mongod 可执行文件。漏配这一项，启动时会抛 `Access to the path '/app/data' is denied`，容器反复重启。

注意事项：

- 免费层没有持久化磁盘：上传的图片（`/tmp/assets`）和每日用量计数（`/tmp/mongo-usage.json`）在重新部署后会清空，业务数据都在 MongoDB 里。
- 应用层有每日写入额度保护（默认 2 万写，按受影响文档数，见 `Mongo:UsageLimits`），超限会返回 429，避免把上游免费额度用穿。读取不再设额度，只靠内存缓存和 Mongo 自身的配额。
- 免费 Web Service 闲置约 15 分钟会休眠，首次访问需要等冷启动。保活有两层：应用内自维持 + GitHub Actions 定时任务，见下一节。
- 容器以非 root 运行，且 Data Protection 密钥不持久化；重新部署后现有登录会话会失效，需要重新登录。

### 保活

保活分两层。

**应用内自维持**：`Host/KeepAliveService.cs` 在进程启动 10 秒后开始，每隔 `KeepAlive:IntervalMinutes`（默认 10 分钟）请求一次本机 `/api/health`，让免费层不会因为空闲而休眠。它只能让已经醒着的实例保持唤醒，进程被杀掉时没法自我唤醒，所以冷启动仍靠外部访问或下面的 Actions。可用环境变量微调：

- `KeepAlive__Enabled`（默认 `true`）：设为 `false` 关掉自维持
- `KeepAlive__IntervalMinutes`（默认 `10`，取值 1–120）
- `KeepAlive__Url`：不填时自动取 Kestrel 实际监听端口的回环地址

**GitHub Actions**：`.github/workflows/keep-alive.yml` 每 10 分钟打一次 `/api/health`（不碰数据库，不占 Mongo 写入额度）。默认地址是 `https://mushsoup.onrender.com`，服务名不同就在仓库 Settings → Secrets and variables → Actions → Variables 里加 `KEEP_ALIVE_URL` 覆盖。

几点要知道：

- 公开仓库的 Actions 不计费；计划任务在 GitHub 负载高峰时可能延迟几分钟，10 分钟的间隔对 15 分钟休眠阈值留了余量。
- 长时间没有任何提交时，GitHub 会停用计划任务（公开仓库 60 天），收到停用邮件后点一次启用即可。
- 免费层每月 750 实例小时。全天保持在唤醒约 744 小时，几乎用满；想留余量就把 cron 改成本地时间白天才跑，例如 `*/10 23-15 * * *`（UTC，对应北京时间 07:00–23:59，约 520 小时/月）。
- 超过 750 小时后免费服务会被暂停到次月，所以别在多处重复保活。
- 不想让它一直醒着，直接在仓库 Actions 页面禁用这个 workflow 即可，回到按需冷启动。

## 认证

登录走外部 Discord 验证 API，本项目不存密码。

1. 登录页从验证频道可见成员里选自己（也可手填 Discord 用户 ID），后端向验证 API 申请验证码
2. 验证码是 4 位（2 字母 + 2 数字，全大写），5 分钟内有效；同一用户 60 秒只能申请一次
3. 验证码通过 Discord 私信或频道线程投递，连续输错 5 次作废，成功一次后失效
4. 校验通过后写 Cookie 会话；首次登录自动用 Discord 身份建号，并加入默认班级

成员列表在服务端缓存 5 分钟，避免每次打开登录页都让上游分页拉人。名单有变动时点登录页的「刷新成员」强制重新抓取；上游出错不会清掉已有缓存。

本地开发连 `scripts/verify-stub.mjs`。真实部署时用环境变量覆盖（也可以在服务器上写进配置）：

```bash
Verification__BaseUrl=https://ialine2dc.onrender.com
Verification__ApiKey=<在服务器里执行 /verify-key 拿到的 key>
```

API Key 只配置在服务端，浏览器永远拿不到，也不要提交进仓库。

## 功能

### 题库与题目

- Discord 验证码登录 / 退出，首次登录自动加入「我的班级」
- 建题库（默认私有，可在题库设置里手动公开）
- 单选 / 多选，选项 A–D 可增删，标记正确答案，标签、难度、解析、来源
- 题干与解析支持 Markdown、`$LaTeX$`、代码块；编辑器里可直接上传图片，插入为 `asset://` 引用
- 默认直接发布，不走审核（练习工具，不是考试系统）
- 搜索、按标签筛选、编辑与删除题目在题库详情页的「题目设置」（`/banks/:id/questions`）；详情页只保留练习入口和章节选择

### 题库共享

- 题库设置页（只有创建者能改）管理名称、简介、可见性、默认选项乱序、学科标签、学年与学期
- 可见性两档：**私有**（只有自己和成员） / **公开**（所有人能在「公开」页看到并练习）
- 公开不需要审核，保存即生效；**公开题库必须有至少一个标签**
- 成员：从验证频道成员里选人加入，成员可以查看、练习并编辑题库；随时可移除
- 导航里的「公开」页面集中展示所有人的公开题库和公开讲义，可按**年 / 学期 / 学科 / 包含章**筛选，并搜索标题、简介、标签、学年和章节
- 学科标签做了简繁与常见别名归一：`数学` / `數學`、`英文` / `英语`、`语文` / `國文` 等视为同一科
- 学年是自由文本（例如 `高一` 或 `2025-2026`），学期是 `上` / `下`；没填的内容只会出现在「全部」筛选下

默认班级的管理员固定为 `568768836153769996`（首次启动或重启时补齐）。这个 ID 目前只作为班级数据里的管理员记录，尚未接到越权编辑题库的判断上。

### 分享与游客访问

公开题库和公开讲义的详情页上有「分享链接」按钮，生成形如 `/s/xtz6fn3z` 的八位短码；同一份内容重复点会复用同一个码（`short_links` 集合，`backend/src/Modules/ShortLinks`）。

- 已登录时打开短链接：直接跳到真实页面，和平时一样保存进度
- 未登录时：进入游客视图，公开讲义可整份阅读，公开题库可逐题练习；判分在服务端完成，但**不写任何记录**
- 只有公开内容能生成链接；内容改回私有后，旧链接打开会提示不可用
- 游客视图在 `/share/bank/:id` 和 `/share/note/:id`，路由不带登录守卫；前端的 401 自动跳登录会跳过 `/s/`、`/share/` 和 `/api/public/`

### 收藏

公开题库和公开讲义的详情页上有「收藏」按钮，导航里的「收藏」页按题库 / 讲义分组列出。

- 只能收藏当前公开的内容；重复点收藏是幂等的，取消收藏立即生效
- 收藏只记录 `kind + itemId`（`favorite_items` 集合，`backend/src/Modules/Favorites`），打开「收藏」页时再和题库、讲义列表合并
- 内容改成私有或删除后，收藏页里自然不再显示；重新公开又会回来
- 收藏跟着账号走，换设备也在

### 最近

首页就是「最近」这个动态页，分上下两块：

- **最近常用**：固定 6 格，按账号记录的打开顺序排列（`recent_items` 集合，按用户去重并累计打开次数），换设备也能接着用；不足 6 个时用最新公开内容补齐
- **最新公开**：公开题库和公开讲义按更新时间混排，显示前 12 项，其余跳「公开」页
- 打开题库详情、练习页、讲义阅读页都会记一次，1 分钟内的重复上报会合并，失败不影响页面
- 完全没有内容时只显示空状态：一个「去公开」、一个「去我的题库」
- 题库列表移到了 `/banks`（导航第二项），首页 `/` 现在是「最近」

### 讲义

讲义是题库之外的另一块内容：一整份结构化文档，按章节阅读，不参与判分和记忆调度。

- 导航里的「讲义」列出可见讲义；点进去是阅读页
- 目录分两层：大章（CH 01 / 附錄 A）下挂每一张内容卡的标题（例如「企業的定義」），点击直接定位；桌面端右侧粘性侧栏，窄屏折叠成顶部「目录」
- 内容以 JSON 块存储（`notes` 集合），前端按 `type` 递归渲染：`card / heading / paragraph / list / table(普通 + matrix) / flow / callout(tip/warn/note/example) / quote / grid / quickGroup / figure(SVG) / summary / chips`
- 文本字段一律 Markdown，支持 GFM 表格、`$LaTeX$`、代码块；SVG 以 data URI 渲染，不执行脚本
- 讲义里的图点击（或点按）可以全屏放大，窄屏下用更大的画布显示、可平移查看细节；Esc 或点背景关闭
- 「导入讲义」页面提供一份纯格式提示词，可以复制给模型生成整份讲义；粘贴整份 JSON → 本地校验并预览章节与块数 → 选择可见性 / 标签 / 学年 / 学期 → 发布。文件整体 ≤ 4 MB，`formatVersion` 必须是 1
- 创建者可以在「讲义设置」里改可见性、学科标签、学年与学期；公开讲义必须有至少一个标签
- 讲义章节名（`CH 01 認識融合生活的企業`）由各节 `label + title` 自动派生，公开页的「包含章」筛选直接用它
- 完整示例：`scripts/content/enterprise-management-101.json`（企管概論 CH1–CH6 + 附录 A–E，11 节 / 451 块 / 7 张 SVG）
- `node scripts/seed.mjs` 会顺带把这份讲义写进当前环境；已存在同名讲义时走更新而不是重复创建

讲义默认公开；私有讲义只有创建者能看。讲义正文的在线编辑暂时没有界面；删除入口在「讲义设置」页底部。

### 三种练习模式

| 模式 | 行为 |
|---|---|
| **开始练习**（daily） | 按记忆到期时间排队：先到期的，再新题，末尾带少量维护抽样 |
| **无限刷题**（endless） | 无结束按钮，答错的题隔两张卡再回来，连续答对两次才移出本轮；全部练熟才结束 |
| 顺序练习（sequence） | 按更新时间依次出题。API 默认模式 |

无限刷题没有「再练一次」，因为它本身就是循环：只有全部题目都达到「连续答对两次」才会停下来。

题库详情页顶部可以选题数：10 / 20 / 50 / 100，默认 20。选择记在这台设备的 localStorage（`ms_practice_limit`），只对「开始练习」（daily）和顺序练习生效；无限刷题按自己的队列循环，不用这个值。

题库详情页会显示「继续练习」：上一轮没做完的会话留在服务端（`practice_sessions` 里 `FinishedAt = null` 的最近一条），点继续会回到当前那道题，已答、已掌握和队列都还在。

### 按章节练习

- 题目可以有 `chapterPath`：最多 3 层，常见是「大章 / 小章」，例如 `["企業管理概論","認識融合生活的企業","企業的定義"]`
- 题库详情页会把所有题目按大章、小章聚合成树，显示每章题数；可以勾单个小章，也可以勾大章全选（半选状态会标出来）
- 章节按自然顺序排列：`1-1 < 1-2 < 1-10`、`1.1 < 1.2 < 1.10`，也认 `CH 02`、`第一章`、`第二十三章`；分隔符不限，公开页的「包含章」下拉用的是同一套比较（`frontend/src/natural-sort.ts`）
- 「练习所选章节」和「无限刷题」都只练选中的章节；不选章节时等价于整库练习
- 大章按前缀匹配：只选大章会覆盖它下面所有小章；没有章节的题只在整库练习里出现
- 章节面板顶部有一排**大章小按钮**：点一下选中整章，再点取消；半选状态用虚线框标出，长列表里不用往下滚
- 「范围」可以直接写 `1-1 ~ 4-5`：按面板里的章节顺序选中这段区间（含两端），`1 ~ 4` 表示整章；`1.1`、`第一章 / 第一節`、`~ ～ 至 到` 都认，两端写反也能选中；点「选这段」会替换当前勾选（`frontend/src/chapter-range.ts`）
- 题目编辑器和批量导入都支持章节路径；导入提示词里也带了这个可选字段

### 外观

- 设置页可以选**浅色 / 深色 / 跟随系统**，立即生效，记在这台设备的 localStorage 里
- 深色模式有一套完整的语义 token（背景、表面、文字、边框、主色、状态色、代码高亮），各页面共用
- 讲义里的 SVG 是深色画布设计，所以图版固定用深色背景，保证浅色和深色主题下文字都清晰；图注在画布外，跟随正文颜色

### 语言

- 设置页可以选**简体中文 / 繁體中文**，立即生效，记在这台设备的 localStorage 里（`ms_lang`），不跟账号同步
- 只做字形转换：整个界面、题库与讲义正文都按 OpenCC 字表实时换成繁体（`frontend/src/i18n-chars.ts`），不改写数据，也不做词汇层面的润色；`代码块`、输入框里的内容保持原样
- 词级修正（`PHRASES` / `CORRECTIONS`）处理单字表容易弄错的常见词，例如 复制→複製、公里→公里、关系→關係

### 排行榜

导航里的「排行」按班级统计每个人，默认综合排序，也可以切到单项排序。

| 指标 | 来源 |
|---|---|
| 答对率 | `review_events` 的 `correct / answered` |
| 资料贡献 | 创建的题目数 + 创建的题库数 + 创建的讲义数 |
| 刷题量 | `review_events` 的作答次数 |

- 综合分 = 答对率 50% + 资料贡献 30% + 刷题量 20%，后两项按班级最高值归一
- **答满 50 题才能上榜**；不满 50 题的成员不会出现在排行榜里
- 答对率做了平滑：`(correct + 5) / (answered + 10)`，避免只答对一两题就冲到榜首
- 只统计当前班级成员
- 自己那一行会标「你」

### 记忆调度

- 每次作答写入 `review_events`（评分、耗时、seed、当时的卡片快照），并更新 `review_states`
- 算法是 **SM-2**：ease 因子 + 间隔 1 → 6 → `interval × ease`，答错重置为学习步并降低 ease
- 评分自动推导：错 → `again`，对但慢 → `hard`，正常 → `good`，又快又稳 → `easy`
- 间隔 ≥ 21 天且复习 ≥ 3 次记为 `mastered`
- 每日队列 = 到期题 + 新题 + 10% 的「还没到期」维护抽样

> 为什么不是 FSRS：FSRS 的默认权重随版本变化，猜一组数字上线等于埋一个无法验证的错误。SM-2 的常数是明确定义的、可以逐条单测。调度器是 `IReviewScheduler` 接口，将来补 FSRS 不需要动练习流程。

### 选项池

- 题库内可建多个选项池，每个池里放一组同类干扰项
- 出题时选择「干扰项来源 = 某个池」，只需写正确答案，组卡时按 seed 随机抽 N 个干扰项
- **容量硬校验**：任意引用该池的题，池内可用条目必须 ≥ 干扰项数 + 1，不达标时保存题目直接拒绝
- **停用/删除也有校验**：停用到不够用时返回 422，并说明还差几条
- 组卡确定性：同一 (用户, 题, 第几次) 抽到同一组选项，出问题能按 seed 复现
- 池健康面板：可用数、被引用题数、所需条数、重复文本、池子过小的提示

### 批量导入

题库页的「批量导入」把出题从逐题手写变成批量生成：

1. **复制提示词** —— 系统生成一份纯格式说明，带上本题库已有的标签表；内容和题量由你和自己的智能体商量
2. **粘回 JSON** —— 支持 `{ formatVersion: 2, bank?, pools?, questions? }`，也兼容 `formatVersion: 1` 的纯题目对象和裸数组
3. **预览与校验** —— 逐题报告问题，并渲染前 3 道题的示例卡片（含乱序效果）；同时列出这次会修改的题库设置和选项池
4. **确认导入** —— 先应用 `bank` / `pools`，再写入题目；重复题默认跳过（可勾选强制导入），错误题跳过并列出原因

`formatVersion: 2` 让一次导入就能调整整个题库，方便让模型直接「改题 + 改设置 + 调池子」：

- `bank`：名称、简介、可见性、标签、学年、学期、默认乱序、是否需要审核，只改写了字段（标签是整组替换）；需要题库创建者权限
- `pools`：按名称匹配，`mode: merge` 补充条目并重新启用列出的条目，`mode: replace` 把可用条目替换成这一份（不在列表里的停用而不是删除），`delete: true` 删除未被引用的池；调整后仍要满足「最大干扰项数 + 1」的容量校验
- `questions`：可以引用同一次导入里新建的池（按名称），不需要分两次导入
- 只写 `bank` 或只写 `pools` 都可以，不写 `questions` 不会影响已有题目

容错解析：自动剥掉 ```` ```json ```` 围栏、截取中间的 JSON 块、允许尾逗号和注释。模型常见的「前面加一句解释」不会导致失败。

校验分两级：

- **硬错误**（阻止导入）：题型未知、题干为空、选项 key 重复、答案 key 不存在、单选答案不是 1 个、多选答案数量不对
- **软提示**（照常导入）：标签不在题库里、正确选项明显偏长、一批题答案集中在一个位置、`$` 或代码围栏未闭合、缺解析、与已有题目重复

每次导入生成一个 `import_batches` 记录。题库默认不需要审核，题目导入即发布；后端仍保留 `settings.requireReview`，开启时导入的题会进入待审，导入页可以按批次一键通过（设置界面目前不提供这个开关）。

### 判分

- 多选严格：全对才算对；`score` 仍写日志供统计
- 提交后逐项标记 `hit` / `missed` / `wrong` / `neutral`
- 反馈基于**发卡时的快照**，即使题目或选项池在答题过程中被改动，也不会出现选项对不上
- 正确答案和解析随卡片一起下发（`NextCardResponse.CorrectKeys`），前端本地即时判分，不等服务端往返；提交在后台完成，失败才撤回这次反馈
- 提交答案的响应里直接带下一题（`AnswerFeedbackDto.Next`），按空格切题不再多发一次请求

## 目录结构

```
backend/
  src/
    Host/                       组合根：DI、认证、异常处理、模块装配
    BuildingBlocks/
      BuildingBlocks.Contracts/ IModule、异常、ICurrentUser
      BuildingBlocks.Infrastructure/ Mongo 连接、内嵌 mongod
    Modules/
      Identity/    账号与会话        Identity.Contracts/
      Classes/     班级与邀请码      Classes.Contracts/
      Banks/       题库与权限        Banks.Contracts/
      Questions/   题目、选项池、导入、判分、组卡   Questions.Contracts/
      Practice/    练习会话、作答    Practice.Contracts/
      Scheduling/  记忆调度          Scheduling.Contracts/
      Assets/      图片存储          Assets.Contracts/
      Notes/       讲义              Notes.Contracts/
      Leaderboard/ 排行榜            Leaderboard.Contracts/
  tests/
    Architecture.Tests/  模块边界测试
    Questions.Tests/     判分器与组卡器测试
frontend/
  src/pages/     登录、题库、公开内容、题库设置、题目编辑、选项池、批量导入、讲义阅读/设置/导入、练习、排行、设置
  src/components/ Markdown 渲染、讲义块渲染、标签输入、布局、图片上传、通用组件
  src/subjects.ts 学科标签与简繁/别名归一
  src/theme.ts    浅色 / 深色 / 跟随系统
scripts/
  verify-stub.mjs  本地 Discord 验证 API stub（仅开发）
  seed.mjs         演示数据（题库 + 讲义）
  content/         讲义 JSON（enterprise-management-101.json）
```

每个模块只对外暴露 `*.Contracts`。其他模块只能引用 Contracts，不能引用实现 —— 这条规则由 `Architecture.Tests` 强制。

## 几个设计要点

**模块化单体**：所有板块在一个 ASP.NET Core 进程里，但各自持有集合、端点和索引。加一个板块 = 新增两个项目 + 在 `Program.cs` 的模块数组里加一行。

**调度只认 ID**：`Scheduling` 只持有 `(userId, questionId, reviewState)`，不认识题目内容。将来做背单词板块可以整套复用。

**选项乱序是确定性的**：`seed = SHA256(userId | questionId | attemptNo)`。同一次尝试内顺序不变，跨尝试必然变化，出问题能按 seed 复现当时那几张选项。

**卡片不携带答案**：`CardDto` 只有 key / label / text，正确性只在提交后的反馈里返回。

**导入不信任模型输出**：先解析、再逐题校验、再预览，最后才落库。宁可少导入几道，也不让坏数据进题库。

## 尚未实现

| 阶段 | 内容 |
|---|---|
| P3 | 题库 fork / 复制他人题库；管理员对任意题库的越权管理 |
| P4 | 资源共享区（文件 / 链接 / 笔记） |
| P5 | 搜索、通知、统计报表 |
| 讲义 | 正文在线编辑与删除、讲义搜索、从讲义生成题目、LLM 生成提示词 |
| 后续 | FSRS 调度器、算法对比实验页、按用户历史标定参数、题目举报、选项内图片上传、题库级章节模板 |

## 测试

```bash
cd backend
dotnet test
```

覆盖模块边界规则、单选/多选题判分的各种组合、组卡器的确定性、池选项抽样的确定性与去重。

端到端行为在开发时用脚本对真实 HTTP 接口和真实浏览器验证过：验证码登录、建库、建题、三种练习模式、选项池容量校验、批量导入的容错解析与质量警告、图片上传、权限。

本轮新增的公开题库页、题库设置页、成员共享与编辑器图片上传，构建通过但未做浏览器端回归。

讲义链路已做接口级验证：起隔离后端（5081 + 临时 mongod）→ `seed.mjs` 登录并写库 → `GET /api/notes` 返回 11 节 / 451 块，13 种块全部落库。

本轮新增的公开页/标签、讲义深层目录与设置、章节练习、深色模式都做了接口级与构建验证：标签去重与公开校验（422）、章节路径落库、按大章/小章筛选、讲义元信息 PATCH 与 content 同步，全部通过；深色模式核对过文字对比度，但所有新界面都还没做浏览器端视觉回归。

公开页的四个筛选（年 / 学期 / 学科 / 包含章）也做了接口级验证：题库和讲义的 `year` / `semester` 能写入并读回，讲义 `chapters` 由 11 个 section 正确派生，`GET /api/questions/chapter-index` 能按题库返回大章并跳过不可见题库。

排行榜做了真实接口验证：登录后作答一次，`GET /api/leaderboard` 返回权重 0.5/0.3/0.2、该用户 answered=1、贡献 7（题 5 + 库 1 + 讲义 1），综合分按公式算出 72.7。前端排行榜页的桌面表格 / 移动卡片和深色模式在 375–1000px 下做过静态布局检查，未做浏览器端点击回归。
