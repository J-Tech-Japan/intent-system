using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class DirectRunProviderEventJsonlTests
{
    [Fact]
    public void SerializeLineAndDeserializeAll_GivenEvents_RoundTripsJsonl()
    {
        var events = new[]
        {
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-09T10:15:00.0000000+00:00",
                ExecutionUnit = "G19",
                EntryKind = "implement",
                Provider = "Claude",
                ProviderSessionId = "pid:4321",
                EventKind = "session-started",
                Model = "sonnet",
                Transport = "stdio",
                Command = "claude"
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-09T10:15:01.0000000+00:00",
                ExecutionUnit = "G19",
                EntryKind = "implement",
                Provider = "Claude",
                ProviderSessionId = "pid:4321",
                EventKind = "stdout",
                Raw = "{\"type\":\"delta\"}"
            }
        };

        var jsonl = string.Join(Environment.NewLine, events.Select(DirectRunProviderEventJsonl.SerializeLine)) + Environment.NewLine;

        var roundTripped = DirectRunProviderEventJsonl.DeserializeAll(jsonl);

        Assert.Equal(events, roundTripped);
    }
}
