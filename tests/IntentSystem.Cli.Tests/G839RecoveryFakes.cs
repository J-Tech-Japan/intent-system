using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor;

namespace IntentSystem.Cli.Tests;

internal static class G839RecoveryFakes
{
    internal sealed class ThrowingIssueLookup : IGitHubIssueLookup
    {
        public GitHubIssueLookupResult Lookup(string repo, int issueNumber) =>
            throw new InvalidOperationException("IssueLookupFactory was not overridden in test.");
    }

    internal sealed class ThrowingPrLookup : IGitHubPrLookup
    {
        public GitHubPrLookupResult Lookup(string repo, int prNumber) =>
            throw new InvalidOperationException("PrLookupFactory was not overridden in test.");
    }

    internal sealed class ThrowingLister : IGitHubAutomationCandidateLister
    {
        public IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(string repo, IReadOnlyCollection<string> requiredLabels) =>
            throw new InvalidOperationException("CandidateListerFactory was not overridden in test.");

        public IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(string repo, IReadOnlyCollection<string> requiredLabels) =>
            throw new InvalidOperationException("CandidateListerFactory was not overridden in test.");
    }

    internal sealed class ThrowingLabelMutator : IGitHubLabelMutator
    {
        public IReadOnlyList<GitHubAutomationLabel> ReadLabels(string repo, string kind, int number) =>
            throw new InvalidOperationException("LabelMutatorFactory was not overridden in test.");

        public void ApplyLabelTransitions(string repo, string kind, int number,
            IReadOnlyCollection<string> addLabels, IReadOnlyCollection<string> removeLabels) =>
            throw new InvalidOperationException("LabelMutatorFactory was not overridden in test.");

        public void ApplyReconcileTransitions(string repo, string kind, int number,
            IReadOnlyCollection<string> addLabels, IReadOnlyCollection<string> removeLabels) =>
            throw new NotSupportedException();
    }

    internal sealed class FakeIssueLookup : IGitHubIssueLookup
    {
        private readonly GitHubIssueLookupResult? snapshot;
        private readonly Exception? failure;

        public FakeIssueLookup(GitHubIssueLookupResult snapshot) => this.snapshot = snapshot;

        public FakeIssueLookup(Exception failure) => this.failure = failure;

        public GitHubIssueLookupResult Lookup(string repo, int issueNumber)
        {
            if (failure is not null)
            {
                throw failure;
            }

            return snapshot!;
        }
    }

    internal sealed class FakePrLookup : IGitHubPrLookup
    {
        private readonly Dictionary<int, GitHubPrLookupResult> map = new();
        private readonly Exception? failure;

        public FakePrLookup(GitHubPrLookupResult result) => map[result.Number] = result;

        public FakePrLookup(Exception failure) => this.failure = failure;

        public GitHubPrLookupResult Lookup(string repo, int prNumber)
        {
            if (failure is not null)
            {
                throw failure;
            }

            if (map.TryGetValue(prNumber, out var result))
            {
                return result;
            }

            throw new InvalidOperationException($"no fake PR for #{prNumber}");
        }
    }

    internal sealed class FakeLister : IGitHubAutomationCandidateLister
    {
        private readonly IReadOnlyList<GitHubAutomationPrCandidate> prs;
        private readonly Exception? listFailure;

        public FakeLister(IReadOnlyList<GitHubAutomationPrCandidate>? prs = null, Exception? listFailure = null)
        {
            this.prs = prs ?? Array.Empty<GitHubAutomationPrCandidate>();
            this.listFailure = listFailure;
        }

        public IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(string repo, IReadOnlyCollection<string> requiredLabels)
        {
            if (listFailure is not null)
            {
                throw listFailure;
            }

            return prs;
        }

        public IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(string repo, IReadOnlyCollection<string> requiredLabels) =>
            Array.Empty<GitHubAutomationIssueCandidate>();
    }

    internal sealed class SequencedIssueLookup : IGitHubIssueLookup
    {
        private readonly Queue<object> sequence = new();

        public SequencedIssueLookup(params object[] steps)
        {
            foreach (var step in steps)
            {
                sequence.Enqueue(step);
            }
        }

        public GitHubIssueLookupResult Lookup(string repo, int issueNumber)
        {
            if (sequence.Count == 0)
            {
                throw new InvalidOperationException("no more sequenced issue lookup results");
            }

            var next = sequence.Dequeue();
            if (next is Exception exception)
            {
                throw exception;
            }

            return (GitHubIssueLookupResult)next;
        }
    }

    internal sealed class SequencedPrLookup : IGitHubPrLookup
    {
        private readonly Queue<object> sequence = new();

        public SequencedPrLookup(params object[] steps)
        {
            foreach (var step in steps)
            {
                sequence.Enqueue(step);
            }
        }

        public GitHubPrLookupResult Lookup(string repo, int prNumber)
        {
            if (sequence.Count == 0)
            {
                throw new InvalidOperationException("no more sequenced PR lookup results");
            }

            var next = sequence.Dequeue();
            if (next is Exception exception)
            {
                throw exception;
            }

            return (GitHubPrLookupResult)next;
        }
    }

    internal sealed class SequencedClaimVerifier
    {
        private readonly Queue<ClaimOwnershipVerification> sequence = new();

        public SequencedClaimVerifier(params ClaimOwnershipVerification[] steps)
        {
            foreach (var step in steps)
            {
                sequence.Enqueue(step);
            }
        }

        public ClaimOwnershipVerification Verify(string repoRoot, string scope, string? team, bool allowUnheld)
        {
            if (sequence.Count == 0)
            {
                throw new InvalidOperationException("no more sequenced claim verification results");
            }

            return sequence.Dequeue();
        }
    }

    internal sealed class RecordingLabelMutator : IGitHubLabelMutator
    {
        public RecordingLabelMutator(params string[] labels) => Labels = labels.ToList();

        public List<string> Labels { get; }

        public List<LabelTransition> Transitions { get; } = [];

        public IReadOnlyList<GitHubAutomationLabel> ReadLabels(string repo, string kind, int number) =>
            Labels.Select(name => new GitHubAutomationLabel { Name = name }).ToArray();

        public void ApplyLabelTransitions(string repo, string kind, int number,
            IReadOnlyCollection<string> addLabels, IReadOnlyCollection<string> removeLabels)
        {
            foreach (var label in removeLabels)
            {
                Labels.Remove(label);
            }

            foreach (var label in addLabels)
            {
                if (!Labels.Contains(label))
                {
                    Labels.Add(label);
                }
            }

            Transitions.Add(new LabelTransition(kind, number, addLabels.ToArray(), removeLabels.ToArray()));
        }

        public void ApplyReconcileTransitions(string repo, string kind, int number,
            IReadOnlyCollection<string> addLabels, IReadOnlyCollection<string> removeLabels) =>
            throw new NotSupportedException();
    }

    internal sealed record LabelTransition(
        string Kind,
        int Number,
        IReadOnlyList<string> AddLabels,
        IReadOnlyList<string> RemoveLabels);
}
