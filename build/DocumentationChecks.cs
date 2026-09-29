using System.Text.RegularExpressions;
using Serilog;

namespace WorkspaceAccess.Build;

internal static partial class DocumentationChecks
{
    public static void Validate(string root)
    {
        var documents = Directory.EnumerateFiles(Path.Combine(root, "docs"), "*.md", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(root, "*.md", SearchOption.TopDirectoryOnly))
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "deploy"), "*.md", SearchOption.AllDirectories))
            .ToArray();
        var links = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var document in documents)
        {
            var content = File.ReadAllText(document);
            if (!content.StartsWith("# ", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Document needs a title: {document}");
            }

            var targets = new List<string>();
            foreach (Match match in MarkdownLink().Matches(content))
            {
                var link = match.Groups[1].Value;
                if (link.StartsWith('#') || Uri.TryCreate(link, UriKind.Absolute, out _))
                {
                    continue;
                }

                var target = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(document)!,
                    Uri.UnescapeDataString(link.Split('#')[0])));
                if (!File.Exists(target) && !Directory.Exists(target))
                {
                    throw new InvalidOperationException($"Broken link in {document}: {link}");
                }

                targets.Add(target);
            }

            links[Path.GetFullPath(document)] = targets;
        }

        var visited = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<string>();
        pending.Enqueue(Path.Combine(root, "README.md"));
        while (pending.TryDequeue(out var current))
        {
            if (visited.Add(current) && links.TryGetValue(current, out var targets))
            {
                foreach (var target in targets)
                {
                    pending.Enqueue(target);
                }
            }
        }

        foreach (var document in Directory.EnumerateFiles(Path.Combine(root, "docs"), "*.md", SearchOption.AllDirectories))
        {
            if (!visited.Contains(Path.GetFullPath(document)))
            {
                throw new InvalidOperationException($"Document must be reachable from README: {document}");
            }
        }

        var overview = File.ReadAllText(Path.Combine(root, "docs", "overview.md"));
        foreach (var document in Directory.EnumerateFiles(Path.Combine(root, "docs", "platforms"), "*.md"))
        {
            var expectedLink = $"platforms/{Path.GetFileName(document)}";
            if (!overview.Contains($"]({expectedLink})", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Platform difference needs an overview link: {expectedLink}");
            }
        }

        Log.Information("Validated {Count} documents, local link targets, reachability and platform coverage.", documents.Length);
    }

    [GeneratedRegex(@"\[[^\]]+\]\(([^)\s]+)\)")]
    private static partial Regex MarkdownLink();
}
