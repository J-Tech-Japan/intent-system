namespace IntentSystem.Projection.Models;

public sealed record ProjectionSourceBundle
{
    public required SubSliceRow Row { get; init; }

    public required ProjectionContext Context { get; init; }
}
