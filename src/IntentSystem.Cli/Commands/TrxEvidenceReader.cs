using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>Reads a bounded subset of VSTest TRX without treating it as execution authority.</summary>
internal static class TrxEvidenceReader
{
    internal const int MaximumBytes = 16 * 1024 * 1024;
    internal const int MaximumRows = 20_000;
    internal const int MaximumDepth = 32;
    internal const int MaximumNameLength = 16_384;

    private static readonly XNamespace Trx = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly string[] UnsupportedCounters =
    [
        "error", "timeout", "aborted", "inconclusive", "passedButRunAborted",
        "notRunnable", "disconnected", "warning", "completed", "inProgress", "pending"
    ];

    internal sealed record Counters(
        [property: JsonPropertyName("total")] int Total,
        [property: JsonPropertyName("executed")] int Executed,
        [property: JsonPropertyName("passed")] int Passed,
        [property: JsonPropertyName("failed")] int Failed,
        [property: JsonPropertyName("not_executed")] int NotExecuted);

    internal sealed record Discrepancy(
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("declared"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Declared = null,
        [property: JsonPropertyName("derived"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Derived = null,
        [property: JsonPropertyName("count"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Count = null);

    internal sealed record Summary(
        [property: JsonPropertyName("reported_outcome")] string ReportedOutcome,
        [property: JsonPropertyName("total")] int Total,
        [property: JsonPropertyName("passed")] int Passed,
        [property: JsonPropertyName("failed")] int Failed,
        [property: JsonPropertyName("skipped")] int Skipped,
        [property: JsonPropertyName("executed")] int Executed,
        [property: JsonPropertyName("raw_counters")] Counters RawCounters,
        [property: JsonPropertyName("discrepancies")] IReadOnlyList<Discrepancy> Discrepancies);

    internal sealed record ReadResult(bool Success, string Cause, string Detail, Summary? Report)
    {
        internal static ReadResult Invalid(string cause, string detail) => new(false, cause, detail, null);
        internal static ReadResult Complete(Summary report) => new(true, string.Empty, string.Empty, report);
    }

    internal static ReadResult Read(ReadOnlySpan<byte> rawBytes)
    {
        if (rawBytes.Length > MaximumBytes)
            return ReadResult.Invalid("verification-evidence-resource-limit", $"TRX exceeds the {MaximumBytes}-byte limit");

        byte[] bytes = rawBytes.ToArray();
        if (bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) bytes = bytes[3..];

        string xml;
        try { xml = StrictUtf8.GetString(bytes); }
        catch (DecoderFallbackException) { return ReadResult.Invalid("verification-evidence-trx-invalid", "TRX is not strict UTF-8"); }

        if (xml.StartsWith('\uFEFF'))
            return ReadResult.Invalid("verification-evidence-trx-invalid", "TRX contains more than one leading UTF-8 BOM");

        try
        {
            using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MaximumBytes,
                MaxCharactersFromEntities = 0,
                IgnoreComments = false,
                IgnoreWhitespace = false,
                ConformanceLevel = ConformanceLevel.Document,
                CloseInput = true,
            });
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.XmlDeclaration)
                {
                    var encoding = reader.GetAttribute("encoding");
                    if (encoding is not null && !string.Equals(encoding, "utf-8", StringComparison.OrdinalIgnoreCase))
                        return ReadResult.Invalid("verification-evidence-unsupported-dialect", "TRX XML declaration must be absent or UTF-8");
                }
                if (reader.NodeType == XmlNodeType.Element && reader.Depth + 1 > MaximumDepth)
                    return ReadResult.Invalid("verification-evidence-resource-limit", $"TRX XML exceeds depth {MaximumDepth}");
            }
        }
        catch (XmlException exception)
        {
            return ReadResult.Invalid("verification-evidence-trx-invalid", SafeXmlDetail(exception));
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or InvalidOperationException)
        {
            return ReadResult.Invalid("verification-evidence-trx-invalid", SafeXmlDetail(exception));
        }

        XDocument document;
        try
        {
            using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MaximumBytes,
                MaxCharactersFromEntities = 0,
                ConformanceLevel = ConformanceLevel.Document,
            });
            document = XDocument.Load(reader, LoadOptions.None);
        }
        catch (XmlException exception)
        {
            return ReadResult.Invalid("verification-evidence-trx-invalid", SafeXmlDetail(exception));
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or InvalidOperationException)
        {
            return ReadResult.Invalid("verification-evidence-trx-invalid", SafeXmlDetail(exception));
        }

        var root = document.Root;
        if (root is null || root.Name != Trx + "TestRun")
            return ReadResult.Invalid("verification-evidence-unsupported-dialect", "TRX root must be TestRun in the VSTest 2010 namespace");
        if (!TryGuid((string?)root.Attribute("id"), out _))
            return ReadResult.Invalid("verification-evidence-unsupported-dialect", "TestRun id must be a GUID");

        var sections = new Dictionary<string, XElement>(StringComparer.Ordinal);
        foreach (var name in new[] { "Results", "TestDefinitions", "TestEntries", "ResultSummary" })
        {
            var direct = root.Elements(Trx + name).ToArray();
            var anywhere = root.Descendants().Where(element => element.Name.LocalName == name).ToArray();
            if (direct.Length != 1 || anywhere.Length != 1 || anywhere[0].Name != Trx + name)
                return ReadResult.Invalid("verification-evidence-unsupported-dialect", $"TRX requires exactly one direct {name} section");
            sections.Add(name, direct[0]);
        }

        var resultElements = sections["Results"].Elements(Trx + "UnitTestResult").ToArray();
        if (sections["Results"].DescendantsAndSelf().Skip(1)
            .Any(element => string.Equals(element.Name.LocalName, "TestResultAggregation", StringComparison.Ordinal)))
            return ReadResult.Invalid("verification-evidence-unsupported-dialect", "TestResultAggregation rows are not supported by this bounded TRX reader");
        var entryElements = sections["TestEntries"].Elements(Trx + "TestEntry").ToArray();
        var definitionElements = sections["TestDefinitions"].Elements(Trx + "UnitTest").ToArray();
        if (resultElements.Length > MaximumRows || entryElements.Length > MaximumRows || definitionElements.Length > MaximumRows)
            return ReadResult.Invalid("verification-evidence-resource-limit", $"TRX result, entry, or definition count exceeds {MaximumRows}");

        if (root.Descendants().Count(element => element.Name.LocalName == "UnitTestResult") != resultElements.Length
            || root.Descendants().Count(element => element.Name.LocalName == "TestEntry") != entryElements.Length
            || root.Descendants().Count(element => element.Name.LocalName == "UnitTest") != definitionElements.Length)
            return ReadResult.Invalid("verification-evidence-unsupported-dialect", "nested result aggregation or misplaced TRX rows are unsupported");

        var results = new List<ResultRow>(resultElements.Length);
        var executionIds = new HashSet<Guid>();
        foreach (var element in resultElements)
        {
            if (!TryGuid((string?)element.Attribute("testId"), out var testId)
                || !TryGuid((string?)element.Attribute("executionId"), out var executionId))
                return ReadResult.Invalid("verification-evidence-unsupported-dialect", "UnitTestResult testId and executionId must be GUIDs");
            var testName = (string?)element.Attribute("testName");
            if (!ValidName(testName))
                return ReadResult.Invalid("verification-evidence-unsupported-dialect", "UnitTestResult testName must be nonblank text up to 16384 UTF-16 code units");
            var outcome = (string?)element.Attribute("outcome");
            if (outcome is not ("Passed" or "Failed" or "NotExecuted"))
                return ReadResult.Invalid("verification-evidence-unsupported-dialect", "UnitTestResult outcome is not one of Passed, Failed, or NotExecuted");
            if (!executionIds.Add(executionId))
                return ReadResult.Invalid("verification-evidence-unsupported-dialect", "TRX executionId values must be unique");
            results.Add(new(testId, executionId, testName!, outcome));
        }

        var entries = new HashSet<(Guid TestId, Guid ExecutionId)>();
        foreach (var element in entryElements)
        {
            if (!TryGuid((string?)element.Attribute("testId"), out var testId)
                || !TryGuid((string?)element.Attribute("executionId"), out var executionId)
                || !entries.Add((testId, executionId)))
                return ReadResult.Invalid("verification-evidence-unsupported-dialect", "TestEntry identifiers are invalid or duplicated");
        }
        var resultPairs = results.Select(item => (item.TestId, item.ExecutionId)).ToHashSet();
        if (!entries.SetEquals(resultPairs))
            return ReadResult.Invalid("verification-evidence-unsupported-dialect", "TestEntry rows must match every result exactly once and contain no extras");

        var definitions = new Dictionary<Guid, (Guid ExecutionId, string Name)>();
        foreach (var element in definitionElements)
        {
            if (!TryGuid((string?)element.Attribute("id"), out var id))
                return ReadResult.Invalid("verification-evidence-unsupported-dialect", "UnitTest id must be a GUID");
            var name = (string?)element.Attribute("name");
            if (!ValidName(name))
                return ReadResult.Invalid("verification-evidence-unsupported-dialect", "UnitTest name must be nonblank text up to 16384 UTF-16 code units");
            var executions = element.Elements(Trx + "Execution").ToArray();
            if (executions.Length != 1 || !TryGuid((string?)executions[0].Attribute("id"), out var executionId))
                return ReadResult.Invalid("verification-evidence-unsupported-dialect", "UnitTest must have exactly one direct Execution with a GUID id");
            if (!definitions.TryAdd(id, (executionId, name!)))
                return ReadResult.Invalid("verification-evidence-unsupported-dialect", "UnitTest definition IDs must be unique");
        }

        var expectedExecutionElements = definitionElements
            .SelectMany(element => element.Elements(Trx + "Execution"))
            .ToHashSet();
        var allExecutionElements = root.Descendants()
            .Where(element => element.Name.LocalName == "Execution")
            .ToArray();
        if (allExecutionElements.Length != expectedExecutionElements.Count
            || allExecutionElements.Any(element => element.Name != Trx + "Execution")
            || !allExecutionElements.ToHashSet().SetEquals(expectedExecutionElements))
            return ReadResult.Invalid("verification-evidence-unsupported-dialect", "Execution elements must be direct children of UnitTest definitions in the VSTest namespace");

        var usedDefinitions = new HashSet<Guid>();
        var nameDifferences = 0;
        foreach (var result in results)
        {
            if (!definitions.TryGetValue(result.TestId, out var definition)
                || definition.ExecutionId != result.ExecutionId)
                return ReadResult.Invalid("verification-evidence-unsupported-dialect", "each result must match one UnitTest definition id and its Execution id");
            usedDefinitions.Add(result.TestId);
            if (!string.Equals(definition.Name, result.TestName, StringComparison.Ordinal)) nameDifferences++;
        }
        if (usedDefinitions.Count != definitions.Count)
            return ReadResult.Invalid("verification-evidence-unsupported-dialect", "TRX contains an unused UnitTest definition");

        var countersElements = sections["ResultSummary"].Elements(Trx + "Counters").ToArray();
        if (countersElements.Length != 1)
            return ReadResult.Invalid("verification-evidence-unsupported-dialect", "ResultSummary requires exactly one direct Counters element");
        var allCountersElements = root.Descendants()
            .Where(element => element.Name.LocalName == "Counters")
            .ToArray();
        if (allCountersElements.Length != 1
            || allCountersElements[0] != countersElements[0]
            || allCountersElements[0].Name != Trx + "Counters")
            return ReadResult.Invalid("verification-evidence-unsupported-dialect", "Counters must be the one direct ResultSummary child in the VSTest namespace");
        var countersElement = countersElements[0];
        if (!TryCounter(countersElement, "total", out var declaredTotal)
            || !TryCounter(countersElement, "executed", out var declaredExecuted)
            || !TryCounter(countersElement, "passed", out var declaredPassed)
            || !TryCounter(countersElement, "failed", out var declaredFailed)
            || !TryCounter(countersElement, "notExecuted", out var declaredNotExecuted))
            return ReadResult.Invalid("verification-evidence-trx-invalid", "TRX required counters must be nonnegative decimal integers");
        foreach (var name in UnsupportedCounters)
        {
            var attribute = countersElement.Attribute(name);
            if (attribute is null) continue;
            if (!TryNonnegative(attribute.Value, out var value) || value != 0)
                return ReadResult.Invalid("verification-evidence-unsupported-dialect", $"unsupported TRX counter '{name}' must be absent or zero");
        }

        var passed = results.Count(item => item.Outcome == "Passed");
        var failed = results.Count(item => item.Outcome == "Failed");
        var skipped = results.Count(item => item.Outcome == "NotExecuted");
        var executed = passed + failed;
        if (declaredTotal != results.Count || declaredExecuted != executed || declaredPassed != passed || declaredFailed != failed)
            return ReadResult.Invalid("verification-evidence-trx-invalid", "TRX total/executed/passed/failed counters do not match result rows");
        if (declaredNotExecuted != skipped && declaredNotExecuted != 0)
            return ReadResult.Invalid("verification-evidence-trx-invalid", "TRX notExecuted counter must equal derived skipped rows or the tolerated zero value");

        var summary = sections["ResultSummary"];
        var summaryOutcome = (string?)summary.Attribute("outcome");
        if (failed > 0 ? summaryOutcome != "Failed" : summaryOutcome is not ("Completed" or "Passed"))
            return ReadResult.Invalid("verification-evidence-trx-invalid", "ResultSummary outcome does not agree with derived failed results");

        var discrepancies = new List<Discrepancy>();
        if (declaredNotExecuted == 0 && skipped > 0)
            discrepancies.Add(new("not-executed-counter-differs", declaredNotExecuted, skipped));
        if (nameDifferences > 0) discrepancies.Add(new("definition-name-differs", Count: nameDifferences));
        discrepancies.Sort((left, right) => StringComparer.Ordinal.Compare(left.Code, right.Code));

        var reported = failed > 0 ? "failed"
            : passed > 0 && skipped > 0 ? "passed-with-skips"
            : passed > 0 ? "passed"
            : skipped > 0 ? "skipped"
            : "not-observed";
        return ReadResult.Complete(new Summary(reported, results.Count, passed, failed, skipped, executed,
            new Counters(declaredTotal, declaredExecuted, declaredPassed, declaredFailed, declaredNotExecuted), discrepancies));
    }

    private sealed record ResultRow(Guid TestId, Guid ExecutionId, string TestName, string Outcome);

    private static bool TryCounter(XElement counters, string name, out int value) =>
        TryNonnegative((string?)counters.Attribute(name), out value);

    private static bool TryNonnegative(string? text, out int value) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value >= 0;

    private static bool TryGuid(string? text, out Guid value) => Guid.TryParse(text, out value);

    private static bool ValidName(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= MaximumNameLength;

    private static string SafeXmlDetail(Exception exception) => exception switch
    {
        XmlException xml => $"XML could not be parsed at line {xml.LineNumber}, position {xml.LinePosition}",
        _ => "TRX could not be parsed as bounded XML",
    };
}
