namespace IntentSystem.Supervisor.Models;

public enum QueueItemState
{
    Queued,
    Active,
    Review,
    Fixing,
    ClarifyBlocked,
    Blocked,
    Completed,

    /// <summary>
    /// G525: the item was atomically retired (superseded/decomposed/obsolete)
    /// via <c>automation issue-retire</c> — it no longer counts toward WIP
    /// and is a terminal state distinct from <see cref="Completed"/>.
    /// </summary>
    Retired
}
