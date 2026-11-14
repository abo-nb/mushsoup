# AGENTS.md

## 项目

班级共享题库 + 记忆算法刷题网站。前端 React 19 + Vite + TypeScript；后端 .NET 10 + MongoDB。

## 命令

```bash
node scripts/verify-stub.mjs                               # 本地 Discord 验证 API stub，端口 5099
cd backend && dotnet run --project src/Host/Host.csproj   # http://localhost:5080，自动拉起内嵌 mongod
cd frontend && pnpm dev                                    # http://localhost:5173，/api 代理到 5080
dotnet build backend/MushroomSoup.slnx
dotnet test backend/MushroomSoup.slnx
pnpm --dir frontend build
```

## 架构约束

- 模块化单体：`backend/src/Modules/<X>` + `<X>.Contracts`
- 跨模块只能引用 `*.Contracts`，由 `backend/tests/Architecture.Tests` 强制
- 每个模块只读写自己的集合
- 前端不要手写后端 DTO 的猜测版本，类型都在 `frontend/src/api.ts`

## 写界面必须使用 `ui-ux-pro-max` 技能

任何前端 UI 工作（新页面、新组件、样式调整、无障碍修复、响应式问题）都要：

1. 调用 `ui-ux-pro-max` 技能，按它的检索流程取用对应的 domain/stack 建议；
2. 先读 `design-system/mushroomsoup/MASTER.md`，尤其是末尾的 **Project Overrides** 段；
3. 交付前按技能的 Pre-Delivery Checklist 自检：可见焦点、4.5:1 对比度、`prefers-reduced-motion`、375px 无横向溢出、可点击元素有 hover/active 反馈；
4. 不新增 UI 组件库或图标依赖，图标用 `src/components/Icon.tsx` 的内联 SVG。

## 前端约定

- 全局 tokens、基础样式、通用组件样式在 `frontend/src/styles.css`。
- 页面级微调写在 `frontend/src/styles/{editorial,manage,practice,chapters,notes,leaderboard}.css`，一组页面一个文件，避免并行改动冲突。
- **不要重命名现有 CSS 类名**，已验证的流程依赖它们；要调整就改样式值或在页面文件里叠加。
- 中文界面，语气克制；不写感叹号堆叠的文案。
- 颜色、间距、圆角、阴影一律用 CSS 变量，不在组件里写死色值。

## 后端约定

- 模块入口 `IModule`，在 `Host/Program.cs` 的模块数组里注册。
- 错误统一抛 `AppException` 系列，由 Host 转 ProblemDetails。
- 时间统一 `DateTime.UtcNow`；软删除用 `DeletedAt`。
- 身份来自外部 Discord 验证 API，API Key 只配置在服务端，前端永远拿不到。

## 验证

- 改前端：`pnpm --dir frontend build`，并在浏览器里走一遍改动涉及的主流程。
- 改后端：`dotnet build` + `dotnet test`，必要时对真实接口做一次 curl/node 冒烟。
- 测试产生的临时文件和日志不要留在仓库里。
