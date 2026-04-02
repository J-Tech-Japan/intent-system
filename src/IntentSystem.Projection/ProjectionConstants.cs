namespace IntentSystem.Projection;

public static class ProjectionConstants
{
    public const string RulesPathSegment = "rules/";
    public const string SpecsPathSegment = "specs/";
    public const string DesignPathSegment = "design/";

    public static readonly string[] RulesAndSpecsPathSegments =
    [
        RulesPathSegment,
        SpecsPathSegment,
        DesignPathSegment
    ];

    public static readonly string[] RequiredVerificationEvidence =
    [
        "contract-reviewed",
        "tests-passing",
        "acceptance-criteria-checked"
    ];
}
