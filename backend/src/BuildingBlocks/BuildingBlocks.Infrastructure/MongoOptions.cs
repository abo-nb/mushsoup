namespace BuildingBlocks.Infrastructure;

public sealed class MongoOptions
{
    public string ConnectionString { get; set; } = "mongodb://127.0.0.1:27027";

    public string Database { get; set; } = "mushroomsoup";

    /// <summary>开发环境下自动拉起内嵌 mongod，免去本机安装 MongoDB。</summary>
    public bool UseEmbedded { get; set; } = true;

    public string EmbeddedDataDirectory { get; set; } = "data/mongo";

    public int EmbeddedPort { get; set; } = 27027;

    /// <summary>
    /// 某些托管 MongoDB（例如 Firestore 兼容层）不允许客户端建索引。
    /// 打开后索引创建失败只记日志，不阻止启动。
    /// </summary>
    public bool IgnoreIndexErrors { get; set; }

    /// <summary>可指向已解压的 mongod 所在目录，跳过运行时下载（离线环境用）。</summary>
    public string? BinaryDirectory { get; set; }

    /// <summary>应用层每日写入额度，按受影响/返回的文档数计算。</summary>
    public MongoUsageLimits UsageLimits { get; set; } = new();
}

public sealed class MongoUsageLimits
{
    public bool Enabled { get; set; } = true;

    public int DailyWrites { get; set; } = 20_000;

    public string StateFile { get; set; } = "data/mongo-usage.json";
}
