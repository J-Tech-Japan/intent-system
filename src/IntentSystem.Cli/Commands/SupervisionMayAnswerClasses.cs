namespace IntentSystem.Cli.Commands;

/// <summary>
/// G563: the four dialog classes the design thread MAY answer, named once.
///
/// The supervision section grants these four; the provisioning section's
/// Authority-boundary sentence used to narrow operator authorization to
/// "read-pane TRUST/ALLOWLIST cases" alone, which silently forbade two of
/// them. Two prose statements of the same rule drifted apart. Naming each
/// class once and composing both surfaces from these constants makes the
/// enumerations agree verbatim rather than by review vigilance.
///
/// This does NOT widen what may be answered: every class still requires the
/// verified read first, and credential / security / permission prompts remain
/// unanswerable with or without authorization.
/// </summary>
internal static class SupervisionMayAnswerClasses
{
    public const string RequestedConfirmations =
        "confirmations of work the design thread itself requested";

    public const string VerifiedReadOnlyCommandApprovals =
        "command approvals verified read-only";

    public const string OwnHookTrustScreens =
        "trust screens for hooks the design thread itself installed";

    public const string PreauthorizedModeChanges =
        "operator-preauthorized mode changes";

    /// <summary>
    /// The same four classes rendered as one inline list, for prose surfaces
    /// that state the boundary in a sentence rather than as a bullet list.
    /// </summary>
    public const string InlineList =
        RequestedConfirmations
        + "; " + VerifiedReadOnlyCommandApprovals
        + "; " + OwnHookTrustScreens
        + "; and " + PreauthorizedModeChanges;
}
