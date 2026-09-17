namespace IntentSystem.Cli.Commands;

/// <summary>
/// G841: structured parse failure from <see cref="PacketYamlDocument.TryParseWithLocation"/>.
/// <see cref="Message"/> is the raw parser text without the
/// <c>packet.yaml is not valid YAML: </c> prefix.
/// </summary>
internal sealed record PacketYamlParseError(string Message, int? Line, int? Column);
