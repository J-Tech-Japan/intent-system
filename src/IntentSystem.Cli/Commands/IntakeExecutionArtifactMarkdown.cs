namespace IntentSystem.Cli.Commands;

internal static class IntakeExecutionArtifactMarkdown
{
    public static IntakeExecutionRequest Deserialize(string markdown)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(markdown);

        var lines = markdown
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.None);

        var sawTitle = false;
        var sawUnitsSection = false;
        var expectingDomain = false;
        string? domain = null;
        var candidates = new List<IntakeExecutionUnitCandidate>();
        ExecutionUnitBuilder? currentUnit = null;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (string.Equals(line, "# Intake Execution Draft", StringComparison.Ordinal))
            {
                sawTitle = true;
                continue;
            }

            if (string.Equals(line, "## Domain", StringComparison.Ordinal))
            {
                expectingDomain = true;
                continue;
            }

            if (expectingDomain)
            {
                if (line.StartsWith('`') && line.EndsWith('`') && line.Length >= 2)
                {
                    domain = line[1..^1];
                    expectingDomain = false;
                    continue;
                }

                throw new InvalidOperationException("Intake execution artifact must contain a backticked domain line.");
            }

            if (string.Equals(line, "## Proposed Execution Units", StringComparison.Ordinal))
            {
                sawUnitsSection = true;
                continue;
            }

            if (!sawUnitsSection)
            {
                continue;
            }

            if (line.StartsWith("### `", StringComparison.Ordinal) && line.EndsWith('`'))
            {
                if (currentUnit is not null)
                {
                    candidates.Add(currentUnit.Build());
                }

                currentUnit = new ExecutionUnitBuilder(line["### `".Length..^1]);
                continue;
            }

            if (currentUnit is null)
            {
                continue;
            }

            if (line.StartsWith("source_file_path: ", StringComparison.Ordinal))
            {
                currentUnit.SourceFilePath = line["source_file_path: ".Length..];
                continue;
            }

            if (line.StartsWith("target_part: ", StringComparison.Ordinal))
            {
                currentUnit.TargetPart = line["target_part: ".Length..];
                continue;
            }

            if (line.EndsWith(":", StringComparison.Ordinal))
            {
                currentUnit.CurrentList = line[..^1];
                continue;
            }

            if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                currentUnit.AddValue(line[2..]);
            }
        }

        if (currentUnit is not null)
        {
            candidates.Add(currentUnit.Build());
        }

        if (!sawTitle)
        {
            throw new InvalidOperationException("Intake execution artifact must start with '# Intake Execution Draft'.");
        }

        if (string.IsNullOrWhiteSpace(domain))
        {
            throw new InvalidOperationException("Intake execution artifact must contain a domain.");
        }

        if (!sawUnitsSection)
        {
            throw new InvalidOperationException("Intake execution artifact must contain '## Proposed Execution Units'.");
        }

        return new IntakeExecutionRequest
        {
            Domain = domain,
            ProposedExecutionUnits = candidates
        };
    }

    private sealed class ExecutionUnitBuilder
    {
        private readonly List<string> dependencies = [];
        private readonly List<string> readinessNotes = [];
        private readonly List<string> verificationHints = [];

        public ExecutionUnitBuilder(string executionUnitId)
        {
            ExecutionUnitId = executionUnitId;
        }

        public string ExecutionUnitId { get; }

        public string? SourceFilePath { get; set; }

        public string? TargetPart { get; set; }

        public string? CurrentList { get; set; }

        public void AddValue(string value)
        {
            if (string.Equals(value, "none", StringComparison.Ordinal))
            {
                return;
            }

            switch (CurrentList)
            {
                case "dependencies":
                    dependencies.Add(value);
                    break;
                case "readiness_notes":
                    readinessNotes.Add(value);
                    break;
                case "verification_hints":
                    verificationHints.Add(value);
                    break;
            }
        }

        public IntakeExecutionUnitCandidate Build()
        {
            if (string.IsNullOrWhiteSpace(SourceFilePath))
            {
                throw new InvalidOperationException(
                    $"Intake execution artifact unit '{ExecutionUnitId}' must contain source_file_path.");
            }

            if (string.IsNullOrWhiteSpace(TargetPart))
            {
                throw new InvalidOperationException(
                    $"Intake execution artifact unit '{ExecutionUnitId}' must contain target_part.");
            }

            return new IntakeExecutionUnitCandidate
            {
                ExecutionUnitId = ExecutionUnitId,
                SourceFilePath = SourceFilePath,
                TargetPart = TargetPart,
                Dependencies = dependencies.ToArray(),
                ReadinessNotes = readinessNotes.ToArray(),
                VerificationHints = verificationHints.ToArray()
            };
        }
    }
}
