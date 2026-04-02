using System.Text.Json;
using IntentSystem.Supervisor.Models;

namespace IntentSystem.Supervisor.Serialization;

public static class QueueStateSerializer
{
    public static string Serialize(QueueState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        return JsonSerializer.Serialize(state, SupervisorJsonOptions.Indented);
    }

    public static QueueState Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        return JsonSerializer.Deserialize<QueueState>(json, SupervisorJsonOptions.Indented)
            ?? throw new InvalidOperationException("Queue state payload deserialized to null.");
    }
}
