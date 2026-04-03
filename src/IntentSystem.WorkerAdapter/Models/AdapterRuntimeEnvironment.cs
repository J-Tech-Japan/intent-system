namespace IntentSystem.WorkerAdapter.Models;

/// <summary>
/// Captures adapter-local runtime details without leaking queue policy into the contract.
/// </summary>
public sealed record AdapterRuntimeEnvironment
{
    public required string Engine { get; init; }

    public required IReadOnlyList<string> Arguments { get; init; }

    public IReadOnlyDictionary<string, string> Variables { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
