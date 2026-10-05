namespace Charac.Server.Hosting;

internal sealed record ServerCommandLine(string[] Command, string? ConfigPath)
{
    public static ServerCommandLine Parse(string[] args)
    {
        var command = new List<string>();
        string? configPath = null;
        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            string? value = null;
            if (argument == "--config")
            {
                if (++index >= args.Length)
                    throw new ArgumentException("--config requires a file path.");
                value = args[index];
            }
            else if (argument.StartsWith("--config=", StringComparison.Ordinal))
            {
                value = argument["--config=".Length..];
            }
            else
            {
                command.Add(argument);
            }

            if (value is null)
                continue;
            if (string.IsNullOrWhiteSpace(value) || value.StartsWith("--", StringComparison.Ordinal) || configPath is not null)
                throw new ArgumentException("Specify --config exactly once with a file path.");
            configPath = value;
        }
        return new ServerCommandLine(command.ToArray(), configPath);
    }
}