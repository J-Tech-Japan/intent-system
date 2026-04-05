using Xunit;

namespace IntentSystem.Cli.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class RunSubmitCommandCollection
{
    public const string Name = "RunSubmitCommand";
}
