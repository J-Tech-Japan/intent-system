using IntentSystem.Supervisor.Models;

namespace IntentSystem.Cli;

internal interface IQueueDispatchPublisher
{
    LinkedIssue CreateIssue(string targetRepo, string title, string body);
}
