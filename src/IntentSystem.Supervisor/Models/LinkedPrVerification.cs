namespace IntentSystem.Supervisor.Models;

/// <summary>
/// G534 review repair: authoritative verification of a queue item's linked
/// PR state, resolved by a CLI boundary that has GitHub access (e.g. via
/// `gh pr view`) and passed into the domain-pure <see cref="QueueManager.Retire"/>
/// so retirement can refuse a merged/closed linked PR even when queue-state
/// itself is stale and still reports a non-<see cref="QueueItemState.Completed"/>
/// state (<c>Queued</c>/<c>Review</c>/<c>Fixing</c>/etc.). Queue-state alone is
/// not authoritative for "is this work actually done" — the linked PR is.
/// <see cref="QueueManager"/> never performs the lookup itself (no network
/// logic in the domain core); the caller resolves this value first.
/// </summary>
public enum LinkedPrVerification
{
    /// <summary>The item has no linked PR at all — nothing to verify.</summary>
    NotLinked,

    /// <summary>The linked PR was confirmed OPEN — retirement may proceed.</summary>
    ConfirmedOpen,

    /// <summary>The linked PR was confirmed MERGED or CLOSED — retirement refuses;
    /// completed/finished work can never be reclassified as retired.</summary>
    ConfirmedMergedOrClosed,

    /// <summary>The linked PR's state could not be resolved (lookup failure,
    /// malformed/unparseable URL, wrong repo, ambiguous response, etc.).
    /// Retirement refuses — ambiguous evidence must never be presumed open.</summary>
    Unverifiable
}
