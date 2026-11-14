using System.Xml.Linq;
using Xunit;

namespace Architecture.Tests;

/// <summary>
/// 模块边界靠项目引用兜不住（编译期只拦得住程序集，拦不住"绕道"），所以用测试把规则钉死：
/// 一个模块的实现项目只能引用自己的 Contracts 和别人的 Contracts，绝不能引用别人的实现。
/// </summary>
public class ModuleBoundaryTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MushroomSoup.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("找不到 backend 根目录（MushroomSoup.slnx）");
    }

    [Fact]
    public void Module_implementations_may_only_reference_other_modules_contracts()
    {
        var violations = new List<string>();

        foreach (var moduleDirectory in Directory.GetDirectories(Path.Combine(RepoRoot, "src", "Modules")))
        {
            var moduleName = Path.GetFileName(moduleDirectory);
            if (moduleName.EndsWith(".Contracts", StringComparison.Ordinal))
            {
                continue;
            }

            var projectPath = Path.Combine(moduleDirectory, $"{moduleName}.csproj");
            if (!File.Exists(projectPath))
            {
                continue;
            }

            foreach (var reference in ReadProjectReferences(projectPath))
            {
                var resolved = Path.GetFullPath(Path.Combine(moduleDirectory, reference)).Replace('\\', '/');
                var referenceName = Path.GetFileNameWithoutExtension(resolved);

                if (resolved.Contains("/src/BuildingBlocks/", StringComparison.Ordinal))
                {
                    continue;
                }

                if (resolved.Contains("/src/Modules/", StringComparison.Ordinal) &&
                    referenceName.EndsWith(".Contracts", StringComparison.Ordinal))
                {
                    continue;
                }

                violations.Add($"{moduleName} -> {referenceName}");
            }
        }

        Assert.True(violations.Count == 0, "跨模块实现引用：" + string.Join("; ", violations));
    }

    [Fact]
    public void Contracts_projects_only_reference_building_blocks()
    {
        var violations = new List<string>();

        foreach (var contractsDirectory in Directory.GetDirectories(Path.Combine(RepoRoot, "src", "Modules"), "*.Contracts"))
        {
            var name = Path.GetFileName(contractsDirectory);
            var projectPath = Path.Combine(contractsDirectory, $"{name}.csproj");
            if (!File.Exists(projectPath))
            {
                continue;
            }

            foreach (var reference in ReadProjectReferences(projectPath))
            {
                var resolved = Path.GetFullPath(Path.Combine(contractsDirectory, reference)).Replace('\\', '/');
                if (resolved.Contains("/src/BuildingBlocks/", StringComparison.Ordinal))
                {
                    continue;
                }

                // Contracts 之间允许互相引用（例如 Practice.Contracts 需要 CardDto），
                // 但绝不能引到任何模块的实现项目。
                if (resolved.Contains("/src/Modules/", StringComparison.Ordinal) &&
                    Path.GetFileNameWithoutExtension(resolved).EndsWith(".Contracts", StringComparison.Ordinal))
                {
                    continue;
                }

                violations.Add($"{name} -> {Path.GetFileNameWithoutExtension(resolved)}");
            }
        }

        Assert.True(violations.Count == 0, "Contracts 越界引用：" + string.Join("; ", violations));
    }

    [Fact]
    public void No_module_references_the_host()
    {
        var violations = Directory
            .GetFiles(Path.Combine(RepoRoot, "src", "Modules"), "*.csproj", SearchOption.AllDirectories)
            .Where(path => ReadProjectReferences(path).Any(reference =>
                reference.Contains("Host", StringComparison.OrdinalIgnoreCase)))
            .Select(Path.GetFileName)
            .ToArray();

        Assert.Empty(violations);
    }

    private static IEnumerable<string> ReadProjectReferences(string projectPath)
    {
        var document = XDocument.Load(projectPath);
        return document
            .Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Replace('/', Path.DirectorySeparatorChar));
    }
}
