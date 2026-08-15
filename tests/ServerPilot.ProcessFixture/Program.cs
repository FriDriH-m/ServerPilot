if (args.Contains("--stdin-quit", StringComparer.Ordinal))
{
    while (await Console.In.ReadLineAsync() is string command)
    {
        if (string.Equals(command.Trim(), "quit", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
    }

    return;
}

await Task.Delay(TimeSpan.FromMinutes(5));
