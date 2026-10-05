namespace Charac.Client;

/// <summary>Places local prompts and status messages below the current terminal text.</summary>
internal static class TerminalLine
{
    public static void Align()
    {
        if (Console.IsOutputRedirected)
            return;
        try
        {
            if (Console.GetCursorPosition().Left != 0)
                Console.WriteLine();
        }
        catch (IOException)
        {
            Console.WriteLine();
        }
        catch (InvalidOperationException)
        {
            Console.WriteLine();
        }
    }

    public static void WriteStatus(string status)
    {
        Align();
        Console.WriteLine(status);
    }
}
