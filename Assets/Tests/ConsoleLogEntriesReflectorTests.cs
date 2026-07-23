using NUnit.Framework;

public class ConsoleLogEntriesReflectorTests
{
    [Test]
    public void CompatibilityState_IsAvailableOrHasReadableReason()
    {
        Assert.That(
            ConsoleLogEntriesReflector.IsAvailable ||
            !string.IsNullOrWhiteSpace(ConsoleLogEntriesReflector.UnavailableReason),
            Is.True);
    }

    [Test]
    public void TryGetVisibleEntries_DoesNotThrow()
    {
        bool success = ConsoleLogEntriesReflector.TryGetVisibleEntries(out var entries, out string error);

        if (success)
        {
            Assert.That(entries, Is.Not.Null);
        }
        else
        {
            Assert.That(error, Is.Not.Empty);
        }
    }
}
