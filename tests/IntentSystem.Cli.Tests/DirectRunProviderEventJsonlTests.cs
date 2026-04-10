using System.Text.Json;
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
                Provider = "Claude",
                EntryKind = "implement",
                SessionId = "pid:4321",
                Kind = "session-metadata",
                Payload = JsonSerializer.SerializeToElement(new
                {
                    model = "sonnet",
                    transport = "stdio",
                    command = "claude"
                })
            },
            new DirectRunProviderEvent
            {
                Timestamp = "2026-04-09T10:15:01.0000000+00:00",
                ExecutionUnit = "G19",
                Provider = "Claude",
                EntryKind = "implement",
                SessionId = "pid:4321",
                Kind = "provider-event",
                Payload = JsonSerializer.SerializeToElement(new
                {
                    type = "delta",
                    sequence = 2
                })
            }
        };

        var jsonl = string.Join(Environment.NewLine, events.Select(DirectRunProviderEventJsonl.SerializeLine)) + Environment.NewLine;

        var roundTripped = DirectRunProviderEventJsonl.DeserializeAll(jsonl);

        Assert.Equal(2, roundTripped.Count);
        Assert.Equal(events[0].Timestamp, roundTripped[0].Timestamp);
        Assert.Equal(events[0].ExecutionUnit, roundTripped[0].ExecutionUnit);
        Assert.Equal(events[0].Provider, roundTripped[0].Provider);
        Assert.Equal(events[0].EntryKind, roundTripped[0].EntryKind);
        Assert.Equal(events[0].SessionId, roundTripped[0].SessionId);
        Assert.Equal(events[0].Kind, roundTripped[0].Kind);
        Assert.Equal(events[0].Payload.GetProperty("model").GetString(), roundTripped[0].Payload.GetProperty("model").GetString());
        Assert.Equal(events[1].Timestamp, roundTripped[1].Timestamp);
        Assert.Equal(events[1].ExecutionUnit, roundTripped[1].ExecutionUnit);
        Assert.Equal(events[1].Provider, roundTripped[1].Provider);
        Assert.Equal(events[1].EntryKind, roundTripped[1].EntryKind);
        Assert.Equal(events[1].SessionId, roundTripped[1].SessionId);
        Assert.Equal(events[1].Kind, roundTripped[1].Kind);
        Assert.Equal(events[1].Payload.GetProperty("type").GetString(), roundTripped[1].Payload.GetProperty("type").GetString());
        Assert.Equal(events[1].Payload.GetProperty("sequence").GetInt32(), roundTripped[1].Payload.GetProperty("sequence").GetInt32());
    }
}
