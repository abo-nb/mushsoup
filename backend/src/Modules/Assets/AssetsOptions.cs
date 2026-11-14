namespace Assets;

internal sealed class AssetsOptions
{
    public string Directory { get; init; } = "data/assets";

    public long MaxFileBytes { get; init; } = 5 * 1024 * 1024;
}
