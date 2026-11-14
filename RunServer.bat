cd backend
rem 本地密钥（Mongo 连接串、Verification API Key）放在 src/Host/appsettings.Local.json，该文件已被 gitignore。
rem 也可以改用环境变量覆盖，例如 set Mongo__ConnectionString=... 与 set Verification__ApiKey=...
dotnet run --project src/Host/Host.csproj
