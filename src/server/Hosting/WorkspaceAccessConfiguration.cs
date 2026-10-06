using Microsoft.Extensions.Configuration;

namespace Charac.Server.Hosting;

internal static class WorkspaceAccessConfiguration
{
    public const string FileName = "workspace-access.config";

    public static string PathToLoad(string? explicitPath = null)
    {
        if (explicitPath is not null)
        {
            if (!Path.IsPathRooted(explicitPath))
            {
                var repositoryRoot = FindRepositoryRoot();
                if (repositoryRoot is not null)
                {
                    var repositoryPath = Path.GetFullPath(explicitPath, repositoryRoot);
                    if (File.Exists(repositoryPath))
                        return repositoryPath;
                }
            }
            var directPath = Path.GetFullPath(explicitPath);
            if (File.Exists(directPath))
                return directPath;
            throw new FileNotFoundException("The specified configuration file does not exist.", explicitPath);
        }

        var root = FindRepositoryRoot();
        if (root is not null)
        {
            var repositoryConfig = Path.Combine(root, FileName);
            if (File.Exists(repositoryConfig))
                return repositoryConfig;
        }

        var workingDirectoryConfig = Path.Combine(Environment.CurrentDirectory, FileName);
        return File.Exists(workingDirectoryConfig)
            ? workingDirectoryConfig
            : Path.Combine(AppContext.BaseDirectory, FileName);
    }

    public static IConfigurationRoot Load(string? explicitPath = null) => new ConfigurationBuilder()
        .AddIniFile(PathToLoad(explicitPath), optional: false, reloadOnChange: false)
        .Build();

    private static string? FindRepositoryRoot()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Charac.slnx")))
                    return directory.FullName;
            }
        }
        return null;
    }
}