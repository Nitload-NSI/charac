using System.Net.Http.Json;

namespace Charac.Client;

internal sealed record ResourceTarget(Guid Id, string Name, string Account);
internal sealed record ResourceSession(Guid Id, Guid TargetId, string TargetName, string Account,
    DateTimeOffset CreatedAt, string State);
internal sealed record ResourceList(ResourceTarget[] Targets, ResourceSession[] Sessions);
internal sealed record ResourceSelection(Guid? TargetId = null, Guid? SessionId = null, bool Refresh = false);

internal static class ResourceMenu
{
    public static async Task<ResourceSelection?> SelectAsync(HttpClient http, Uri origin,
        TextReader input, TextWriter output, CancellationToken cancellationToken = default)
    {
        while (true)
        {
            var resources = await http.GetFromJsonAsync(new Uri(origin, "/resources"),
                ClientJsonContext.Default.ResourceList, cancellationToken);
            if (resources?.Targets is null || resources.Sessions is null)
                throw new InvalidOperationException("Server returned an invalid resource list.");
            var selection = ReadSelection(resources, input, output);
            if (selection is not { Refresh: true })
                return selection;
        }
    }

    internal static ResourceSelection? ReadSelection(ResourceList resources, TextReader input, TextWriter output)
    {
        var choices = new List<ResourceSelection>();
        output.WriteLine();
        output.WriteLine("已有会话");
        if (resources.Sessions.Length == 0)
            output.WriteLine("  暂无运行中的会话。");
        foreach (var session in resources.Sessions)
        {
            var caption = $"{Label(session.TargetName)} / {Label(session.Account)} · {session.CreatedAt.ToLocalTime():g}";
            if (session.State == "detached")
            {
                choices.Add(new(SessionId: session.Id));
                output.WriteLine($"  [{choices.Count}] 返回 {caption}");
            }
            else
                output.WriteLine($"  [占用] {caption} · 已有客户端控制，暂不可选");
            output.WriteLine($"         {session.Id}");
        }
        output.WriteLine();
        output.WriteLine("新建会话");
        if (resources.Targets.Length == 0)
            output.WriteLine("  暂无已授权的目标，请管理员登记目标访问权限。");
        foreach (var target in resources.Targets)
        {
            choices.Add(new(TargetId: target.Id));
            output.WriteLine($"  [{choices.Count}] 新建 {Label(target.Name)} / {Label(target.Account)}");
            output.WriteLine($"         {target.Id}");
        }
        output.WriteLine("  目标列表表示访问授权；实际连接时检查 SSH 是否可达。");
        output.WriteLine();
        while (true)
        {
            output.Write("输入编号连接，r 刷新，q 退出：");
            var value = input.ReadLine()?.Trim();
            if (value is null || string.Equals(value, "q", StringComparison.OrdinalIgnoreCase))
                return null;
            if (string.Equals(value, "r", StringComparison.OrdinalIgnoreCase))
                return new(Refresh: true);
            if (int.TryParse(value, out var number) && number >= 1 && number <= choices.Count)
                return choices[number - 1];
            output.WriteLine("请选择列表中的编号，或输入 r / q。");
        }
    }

    private static string Label(string value) => new(value.Where(character =>
        !char.IsControl(character) && character is not ('\u2028' or '\u2029')).Take(160).ToArray());
}
