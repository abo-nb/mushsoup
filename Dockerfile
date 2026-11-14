# ─────────────────────────────────────────────────────────────
#  MushroomSoup — 前后端一体的 Docker 映像（Render 部署用）
#
#  构建顺序：
#    1. Node 阶段构建前端，产物在 /web/dist
#    2. .NET SDK 阶段发布 Host
#    3. 运行阶段把前端产物放进 wwwroot，由 ASP.NET Core 同源提供
#       页面、静态资源和 /api
#
#  本机测试：
#    docker build -t mushsoup .
#    docker run --rm -p 8080:8080 \
#      -e Mongo__ConnectionString="mongodb://..." \
#      -e Verification__ApiKey="..." mushsoup
#    curl http://localhost:8080/api/health
# ─────────────────────────────────────────────────────────────

# ── 前端 ────────────────────────────────────────────────────
FROM node:24-alpine AS frontend
WORKDIR /web
RUN npm install -g pnpm@12.3.4
COPY frontend/package.json frontend/pnpm-lock.yaml ./
RUN pnpm install --frozen-lockfile
COPY frontend/ ./
RUN pnpm build

# ── 后端 ────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY backend/ ./backend/
RUN dotnet publish backend/src/Host/Host.csproj \
        -c Release \
        -o /app/publish \
        -p:UseAppHost=false

# ── 运行 ────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Render 会注入 PORT（render.yaml 里固定为 8080）；ASP.NET Core 读的是 ASPNETCORE_URLS。
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    Mongo__UsageLimits__StateFile=/tmp/mongo-usage.json \
    Assets__Directory=/tmp/assets

COPY --from=build /app/publish ./
COPY --from=frontend /web/dist ./wwwroot

# 非 root 运行；/app 只读即可，需要写入的路径都指到 /tmp。
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Host.dll"]
