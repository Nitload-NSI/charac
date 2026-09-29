using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;
using Serilog;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

namespace WorkspaceAccess.Build;

internal sealed class Build : NukeBuild
{
    public static int Main()
    {
        Environment.SetEnvironmentVariable("NUKE_TELEMETRY_OPTOUT", "1");
        return Execute<Build>(build => build.Verify);
    }

    [Parameter("Build configuration (Debug or Release).")]
    readonly string Configuration = "Release";

    [Parameter("Publish runtime: win-x64, win-arm64, linux-x64 or linux-arm64.")]
    readonly string? Runtime;

    [Parameter("Require dependency versions from packages.lock.json.")]
    readonly bool LockedRestore;

    AbsolutePath SolutionFile => RootDirectory / "WorkspaceAccessHost.slnx";
    AbsolutePath ProductProject => RootDirectory / "src/WorkspaceAccessHost.csproj";
    AbsolutePath TestProject => RootDirectory / "tests/WorkspaceAccessHost.Tests/WorkspaceAccessHost.Tests.csproj";
    AbsolutePath Artifacts => RootDirectory / "artifacts";

    Target Restore => _ => _
        .Executes(() => DotNetRestore(settings => settings
            .SetProjectFile(SolutionFile)
            .SetLockedMode(LockedRestore)));

    Target Compile => _ => _
        .DependsOn(Restore)
        .Executes(() => DotNetBuild(settings => settings
            .SetProjectFile(SolutionFile)
            .SetConfiguration(Configuration)
            .SetProcessAdditionalArguments("--disable-build-servers")
            .EnableNoRestore()));

    Target Test => _ => _
        .DependsOn(Compile)
        .Executes(() => DotNetTest(settings => settings
            .SetProjectFile(TestProject)
            .SetConfiguration(Configuration)
            .EnableNoBuild()
            .EnableNoRestore()
            .SetResultsDirectory(Artifacts / "test-results")
            .SetLoggers("trx;LogFileName=workspace-access.trx")));

    Target CheckDocs => _ => _
        .Executes(() => DocumentationChecks.Validate(RootDirectory));

    Target Verify => _ => _
        .DependsOn(CheckDocs, Test);

    Target Publish => _ => _
        .DependsOn(Verify)
        .Requires(() => Runtime)
        .Executes(() =>
        {
            string[] supported = ["win-x64", "win-arm64", "linux-x64", "linux-arm64"];
            if (!supported.Contains(Runtime, StringComparer.Ordinal))
            {
                throw new ArgumentException($"Runtime must be one of: {string.Join(", ", supported)}.");
            }

            var destination = Artifacts / "publish" / Runtime!;
            DotNetPublish(settings => settings
                .SetProject(ProductProject)
                .SetConfiguration(Configuration)
                .SetRuntime(Runtime)
                .SetProcessAdditionalArguments("--disable-build-servers")
                .SetSelfContained(true)
                .SetProperty("RestoreLockedMode", LockedRestore)
                .SetOutput(destination));

            var deployment = Runtime!.StartsWith("win-", StringComparison.Ordinal) ? "windows" : "linux";
            CopyDirectory(RootDirectory / "deploy" / deployment, destination / "deploy");
            Log.Information("Published WorkspaceAccessHost to {Directory}", destination);
        });

    Target RunHost => _ => _
        .DependsOn(Compile)
        .Executes(() => DotNetRun(settings => settings
            .SetProjectFile(ProductProject)
            .SetConfiguration(Configuration)
            .SetApplicationArguments("host")
            .EnableNoBuild()
            .EnableNoRestore()));

    Target RunAgent => _ => _
        .DependsOn(Compile)
        .Executes(() => DotNetRun(settings => settings
            .SetProjectFile(ProductProject)
            .SetConfiguration(Configuration)
            .SetApplicationArguments("agent")
            .EnableNoBuild()
            .EnableNoRestore()));

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }
}
