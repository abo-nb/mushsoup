using EphemeralMongo;

namespace BuildingBlocks.Infrastructure;

/// <summary>
/// 开发用内嵌 MongoDB。数据落在 EmbeddedDataDirectory，重启不丢。
/// 注意：默认以 standalone 启动，不支持多文档事务；接真实副本集时把 UseEmbedded 关掉即可。
/// </summary>
public static class EmbeddedMongo
{
    public static IDisposable Start(MongoOptions options)
    {
        var dataDirectory = Path.GetFullPath(options.EmbeddedDataDirectory);
        Directory.CreateDirectory(dataDirectory);

        var runner = MongoRunner.Run(new MongoRunnerOptions
        {
            UseSingleNodeReplicaSet = false,
            MongoPort = options.EmbeddedPort,
            DataDirectory = dataDirectory,
            DataDirectoryLifetime = TimeSpan.FromDays(3650),
            ConnectionTimeout = TimeSpan.FromSeconds(60),
            // 默认每天检查一次新版本，一旦上游发了 patch 就会重新下载整个 MongoDB。
            // 开发环境不需要这个，改成一年的节奏，避免动不动等几分钟。
            NewVersionCheckTimeout = TimeSpan.FromDays(365),
            BinaryDirectory = string.IsNullOrWhiteSpace(options.BinaryDirectory)
                ? null
                : Path.GetFullPath(options.BinaryDirectory),
        });

        options.ConnectionString = runner.ConnectionString;
        return runner;
    }
}
