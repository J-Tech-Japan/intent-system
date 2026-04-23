using IntentSystem.Supervisor.Models;

namespace IntentSystem.Cli;

internal interface IQueueDispatchPublisher
{
    LinkedIssue CreateIssue(string targetRepo, string title, string body);

    void AddLabel(string targetRepo, int issueNumber, string labelName)
    {
        throw new NotSupportedException("This publisher does not support issue label application.");
    }

    IReadOnlyList<string> GetIssueLabels(string targetRepo, int issueNumber)
    {
        throw new NotSupportedException("This publisher does not support issue label inspection.");
    }
}
