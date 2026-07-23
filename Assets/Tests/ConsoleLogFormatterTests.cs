using System.Collections.Generic;
using NUnit.Framework;

public class ConsoleLogFormatterTests
{
    [Test]
    public void Format_EmptyEntries_ReturnsEmptyString()
    {
        Assert.That(ConsoleLogFormatter.Format(new List<ConsoleLogEntry>()), Is.Empty);
    }

    [Test]
    public void Format_MultipleEntries_PreservesOrderAndStackTrace()
    {
        var entries = new List<ConsoleLogEntry>
        {
            new ConsoleLogEntry("first message\nFirstMethod() (at Assets/First.cs:10)\n", 0),
            new ConsoleLogEntry("second message\nSecondMethod() (at Assets/Second.cs:20)", 0)
        };

        string result = ConsoleLogFormatter.Format(entries);

        Assert.That(result, Does.StartWith("first message\nFirstMethod() (at Assets/First.cs:10)"));
        Assert.That(result, Does.EndWith("second message\nSecondMethod() (at Assets/Second.cs:20)"));
        Assert.That(result.IndexOf("first message"), Is.LessThan(result.IndexOf("second message")));
        Assert.That(result, Does.Contain("------------------------------------------------------------"));
    }

    [Test]
    public void Format_TrailingWhitespace_RemovesOnlyEntryEndWhitespace()
    {
        var entries = new List<ConsoleLogEntry>
        {
            new ConsoleLogEntry("message  \n\t", 0)
        };

        Assert.That(ConsoleLogFormatter.Format(entries), Is.EqualTo("message"));
    }
}
