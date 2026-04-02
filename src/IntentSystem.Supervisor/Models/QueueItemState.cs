namespace IntentSystem.Supervisor.Models;

public enum QueueItemState
{
    Queued,
    Active,
    Review,
    Fixing,
    ClarifyBlocked,
    Blocked,
    Completed
}
