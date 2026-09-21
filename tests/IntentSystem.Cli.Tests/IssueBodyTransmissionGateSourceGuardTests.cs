using System.Text.RegularExpressions;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G845 source guard. The end-to-end routes can be stopped by earlier
/// validators, so this guard is the binding proof that every file transmission
/// really uses the complete gate and the staged path.
/// </summary>
public sealed class IssueBodyTransmissionGateSourceGuardTests
{
    [Fact]
    public void EveryBodyFileTransmissionRoute_EvaluatesStagesAndTransmitsTheStagedPath()
    {
        var root = RepoVersionPolicySource.RepoRoot();
        var cases = new[]
        {
            new RouteCase(
                "IssuePublishFlowCommand.cs",
                "IssuePublishFlowCommand.Execute",
                "    public static int Execute(CliContext context, string[] args, TextWriter writer)",
                "    private static int ExecuteDeclaredTeamCreate(",
                "creator.CreateIssue",
                Point7: false),
            new RouteCase(
                "IssuePublishFlowCommand.cs",
                "IssuePublishFlowCommand.ExecuteDeclaredTeamCreate",
                "    private static int ExecuteDeclaredTeamCreate(",
                null,
                "creator.CreateIssue",
                Point7: false),
            new RouteCase(
                "IssuePublishReviewedCommand.cs",
                "IssuePublishReviewedCommand.Execute",
                "    public static int Execute(CliContext context, string[] args, TextWriter writer)",
                "    private static bool TryParseArguments(",
                "creator.CreateIssue",
                Point7: false),
            new RouteCase(
                "IssueSyncBodyCommand.cs",
                "IssueSyncBodyCommand.Execute",
                "    public static int Execute(CliContext context, string[] args, TextWriter writer)",
                "    private static bool IsAdapterFailure(",
                "client.UpdateBody",
                Point7: true),
        };

        foreach (var route in cases)
        {
            var path = Path.Combine(root, "src", "IntentSystem.Cli", "Commands", route.FileName);
            var source = File.ReadAllText(path);
            var member = SliceMember(source, route.StartMarker, route.EndMarker);
            var evaluate = member.IndexOf("IssueBodyTransmissionGate.Evaluate", StringComparison.Ordinal);
            var transmission = member.IndexOf(route.TransmissionCall, StringComparison.Ordinal);
            var stage = member.IndexOf("using var staged = acceptedBody.Stage();", StringComparison.Ordinal);

            Require(evaluate >= 0, path, route.MemberName, source, route.StartMarker,
                "the complete IssueBodyTransmissionGate.Evaluate call is present");
            // AC3: "no route calls a decode-only variant". A member-substring
            // check alone passes for a helper whose name merely starts with
            // Evaluate, so the call must be Evaluate( exactly and no
            // longer-named gate entry point may appear in the member.
            Require(
                Regex.IsMatch(member, @"IssueBodyTransmissionGate\.Evaluate\("),
                path, route.MemberName, source, route.StartMarker,
                "the gate entry point called is IssueBodyTransmissionGate.Evaluate itself");
            Require(
                !Regex.IsMatch(member, @"IssueBodyTransmissionGate\.Evaluate\w"),
                path, route.MemberName, source, route.StartMarker,
                "the route calls no decode-only gate variant");
            Require(transmission >= 0, path, route.MemberName, source, route.TransmissionCall,
                "the measured transmission call is still anchored in this member");
            Require(evaluate < transmission, path, route.MemberName, source, route.TransmissionCall,
                "Evaluate precedes the transmission call");
            Require(stage >= 0, path, route.MemberName, source, route.TransmissionCall,
                "the accepted result is staged in a using scope");
            Require(
                Regex.IsMatch(member, $@"{Regex.Escape(route.TransmissionCall)}\([^\r\n]*staged\.Path"),
                path,
                route.MemberName,
                source,
                route.TransmissionCall,
                "the transmission receives staged.Path");
            Require(
                member.Contains("if (transmissionResult is IssueBodyTransmissionRefusal transmissionRefusal)", StringComparison.Ordinal),
                path,
                route.MemberName,
                source,
                route.StartMarker,
                "both neutral refusal kinds reach the route refusal branch");
            Require(
                member.Contains("BuildTransmissionRefusal", StringComparison.Ordinal),
                path,
                route.MemberName,
                source,
                route.StartMarker,
                "the route uses its own refusal builder");
            Require(
                !member.Contains("IssueBodyFileStager.Transmit", StringComparison.Ordinal),
                path,
                route.MemberName,
                source,
                route.StartMarker,
                "the route does not bypass the accepted-result Stage boundary");

            if (route.Point7)
            {
                var dryRun = member.IndexOf("if (!write)", StringComparison.Ordinal);
                var equality = member.IndexOf("if (equalModuloTrailingNewline)", StringComparison.Ordinal);
                var remoteRead = member.IndexOf("client.ReadBody", StringComparison.Ordinal);
                Require(remoteRead >= 0 && remoteRead < evaluate, path, route.MemberName, source, "client.ReadBody",
                    "ReadBody precedes the write-only gate");
                Require(dryRun >= 0 && dryRun < evaluate, path, route.MemberName, source, "if (!write)",
                    "the gate remains out of the dry-run return");
                Require(equality >= 0 && equality < stage, path, route.MemberName, source,
                    "if (equalModuloTrailingNewline)",
                    "Stage occurs only after the write-mode equality/no-op decision");
                Require(evaluate < equality, path, route.MemberName, source, "if (equalModuloTrailingNewline)",
                    "Evaluate precedes the write-mode equality/no-op decision");
            }
        }
    }

    [Fact]
    public void Gate_UsesTheSharedStrictDecodeBeforeTheRawSizeCheck_AndDoesNoIo()
    {
        var path = Path.Combine(
            RepoVersionPolicySource.RepoRoot(),
            "src",
            "IntentSystem.Cli",
            "Commands",
            "IssueBodyTransmissionGate.cs");
        var source = File.ReadAllText(path);
        var decode = source.IndexOf("StrictUtf8FileReader.Decode", StringComparison.Ordinal);
        var size = source.IndexOf("body.Length > IssueBodySizeLimits.HardLimitBytes", StringComparison.Ordinal);

        Require(decode >= 0, path, "IssueBodyTransmissionGate.Evaluate", source, "StrictUtf8FileReader.Decode",
            "the gate calls the shared strict decoder");
        Require(size >= 0, path, "IssueBodyTransmissionGate.Evaluate", source, "HardLimitBytes",
            "the gate performs its raw-byte size check");
        Require(decode < size, path, "IssueBodyTransmissionGate.Evaluate", source, "HardLimitBytes",
            "strict decoding precedes the size check");
        Assert.DoesNotContain("File.", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Directory.", source, StringComparison.Ordinal);
    }

    private static string SliceMember(string source, string startMarker, string? endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"missing member anchor {startMarker}");
        var end = endMarker is null
            ? source.Length
            : source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(end >= 0, $"missing member end anchor {endMarker}");
        return source[start..end];
    }

    private static void Require(
        bool condition,
        string path,
        string member,
        string source,
        string anchor,
        string requirement)
    {
        var anchorIndex = Math.Max(0, source.IndexOf(anchor, StringComparison.Ordinal));
        var line = source[..anchorIndex].Count(character => character == '\n') + 1;
        Assert.True(condition, $"{Path.GetFileName(path)}:{line} {member}: {requirement}.");
    }

    private sealed record RouteCase(
        string FileName,
        string MemberName,
        string StartMarker,
        string? EndMarker,
        string TransmissionCall,
        bool Point7);
}
