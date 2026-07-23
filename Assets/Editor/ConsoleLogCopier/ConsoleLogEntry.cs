internal readonly struct ConsoleLogEntry
{
    public ConsoleLogEntry(string message, int mode)
    {
        Message = message;
        Mode = mode;
    }

    public string Message { get; }
    public int Mode { get; }
}
