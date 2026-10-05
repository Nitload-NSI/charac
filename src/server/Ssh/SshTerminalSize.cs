namespace Charac.Server.Ssh;

/// <summary>Dimensions of the remote SSH pseudo-terminal, measured in character cells.</summary>
internal readonly record struct SshTerminalSize
{
    public SshTerminalSize(ushort columns, ushort rows)
    {
        if (columns == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(columns));
        }

        if (rows == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rows));
        }

        Columns = columns;
        Rows = rows;
    }

    public ushort Columns { get; }
    public ushort Rows { get; }
}
