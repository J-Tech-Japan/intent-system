namespace IntentSystem.Projection.Models;

internal static class ReadOnlyListHashCodeExtensions
{
    internal static void AddValues(ref HashCode hash, IReadOnlyList<string> values)
    {
        foreach (var value in values)
        {
            hash.Add(value);
        }
    }
}
