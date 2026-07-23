using System.Collections.Generic;
using System.Text;

internal static class ConsoleLogFormatter
{
    private const string EntrySeparator = "\n\n------------------------------------------------------------\n\n";

    public static string Format(IReadOnlyList<ConsoleLogEntry> entries)
    {
        if (entries == null || entries.Count == 0)
        {
            return string.Empty;
        }

        var output = new StringBuilder();
        for (int i = 0; i < entries.Count; i++)
        {
            if (i > 0)
            {
                output.Append(EntrySeparator);
            }

            output.Append((entries[i].Message ?? string.Empty).TrimEnd());
        }

        return output.ToString();
    }
}
