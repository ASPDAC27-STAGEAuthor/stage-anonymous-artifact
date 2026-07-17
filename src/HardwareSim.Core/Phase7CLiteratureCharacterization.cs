using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HardwareSim.Core;

/// <summary>Loads and validates the Phase 7C exact-point literature characterization catalog.</summary>
public sealed class Phase7CLiteratureCharacterizationCatalog
{
    private const string SupportedSchemaVersion = "phase7c-literature-characterization-1.0";
    private const string SupportedPolicyId = "normalized-full-title-v1";
    private static readonly Regex ConferenceSessionPrefix = new(
        @"^\s*\d+\s*[.]\s*\d+\s+",
        RegexOptions.CultureInvariant);

    private Phase7CLiteratureCharacterizationCatalog(
        string catalogId,
        string sourceWorkbookPath,
        string sourceWorkbookSha256,
        IReadOnlyList<Phase7CLiteratureSource> sources,
        IReadOnlyList<Phase7CLiteratureRecord> records,
        IReadOnlyList<Phase7CLiteratureImportIssue> issues)
    {
        CatalogId = catalogId;
        SourceWorkbookPath = sourceWorkbookPath;
        SourceWorkbookSha256 = sourceWorkbookSha256;
        Sources = sources;
        Records = records;
        Issues = issues;
    }

    /// <summary>Gets the deterministic catalog identifier.</summary>
    public string CatalogId { get; }

    /// <summary>Gets the repository-relative source workbook path.</summary>
    public string SourceWorkbookPath { get; }

    /// <summary>Gets the SHA-256 of the source workbook used for this import.</summary>
    public string SourceWorkbookSha256 { get; }

    /// <summary>Gets the independently verified literature sources.</summary>
    public IReadOnlyList<Phase7CLiteratureSource> Sources { get; }

    /// <summary>Gets all imported records, including explicitly excluded gaps.</summary>
    public IReadOnlyList<Phase7CLiteratureRecord> Records { get; }

    /// <summary>Gets validation issues found while loading the catalog.</summary>
    public IReadOnlyList<Phase7CLiteratureImportIssue> Issues { get; }

    /// <summary>Gets whether the catalog passed all import-contract checks.</summary>
    public bool IsValid => Issues.Count == 0;

    /// <summary>Loads and validates a catalog JSON file.</summary>
    public static Phase7CLiteratureCharacterizationCatalog Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Catalog path is required.", nameof(path));
        }

        return Parse(File.ReadAllText(path, Encoding.UTF8));
    }

    /// <summary>Parses and validates catalog JSON.</summary>
    public static Phase7CLiteratureCharacterizationCatalog Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ArgumentException("Catalog JSON is required.", nameof(json));
        }

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var issues = new List<Phase7CLiteratureImportIssue>();

        var schemaVersion = RequiredString(root, "schemaVersion", "$", issues);
        if (!string.Equals(schemaVersion, SupportedSchemaVersion, StringComparison.Ordinal))
        {
            issues.Add(new("UnsupportedSchemaVersion", "$.schemaVersion", $"Expected '{SupportedSchemaVersion}', found '{schemaVersion}'."));
        }

        var catalogId = RequiredString(root, "catalogId", "$", issues);
        var workbook = RequiredObject(root, "sourceWorkbook", "$", issues);
        var workbookPath = RequiredString(workbook, "path", "$.sourceWorkbook", issues);
        var workbookHash = RequiredString(workbook, "sha256", "$.sourceWorkbook", issues);
        var workbookRecordCount = RequiredInt(workbook, "totalRecordCount", "$.sourceWorkbook", issues);
        RequireSha256(workbookHash, "$.sourceWorkbook.sha256", issues);

        var policy = RequiredObject(root, "verificationPolicy", "$", issues);
        var policyId = RequiredString(policy, "policyId", "$.verificationPolicy", issues);
        var minimumSiteCount = RequiredInt(policy, "minimumIndependentWebsiteCount", "$.verificationPolicy", issues);
        if (!string.Equals(policyId, SupportedPolicyId, StringComparison.Ordinal) || minimumSiteCount < 2)
        {
            issues.Add(new("InvalidVerificationPolicy", "$.verificationPolicy", "The catalog must require normalized full-title matches on at least two independent websites."));
        }

        var sources = ParseSources(root, policyId, minimumSiteCount, issues);
        var records = ParseRecords(root, sources, issues);
        if (workbookRecordCount != records.Count)
        {
            issues.Add(new("WorkbookRecordCountMismatch", "$.sourceWorkbook.totalRecordCount", $"Expected {records.Count}, found {workbookRecordCount}."));
        }
        ValidateSummary(root, sources, records, issues);

        return new(
            catalogId,
            workbookPath,
            workbookHash,
            new ReadOnlyCollection<Phase7CLiteratureSource>(sources),
            new ReadOnlyCollection<Phase7CLiteratureRecord>(records),
            new ReadOnlyCollection<Phase7CLiteratureImportIssue>(issues));
    }

    /// <summary>Verifies that a workbook is byte-for-byte identical to the imported source workbook.</summary>
    public bool SourceWorkbookMatches(string workbookPath)
    {
        using var stream = File.OpenRead(workbookPath);
        using var sha256 = SHA256.Create();
        var actual = ToLowerHex(sha256.ComputeHash(stream));
        return string.Equals(SourceWorkbookSha256, actual, StringComparison.Ordinal);
    }

    /// <summary>Returns exact-point records for one profile without interpolation or extrapolation.</summary>
    public IReadOnlyList<Phase7CLiteratureRecord> QueryProfile(string profileId, bool includeExcludedGaps = false)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            throw new ArgumentException("Profile id is required.", nameof(profileId));
        }

        return Records
            .Where(record => string.Equals(record.ProfileId, profileId, StringComparison.Ordinal))
            .Where(record => includeExcludedGaps || record.ImportStatus == Phase7CLiteratureImportStatus.Active)
            .OrderBy(record => record.Field, StringComparer.Ordinal)
            .ThenBy(record => record.Mode, StringComparer.Ordinal)
            .ThenBy(record => record.RecordId, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>Creates numeric profile snapshots from active records at their reported work point.</summary>
    public IReadOnlyList<CharacterizedProfileSnapshot> CreateNumericSnapshots(string profileId, string targetId)
    {
        if (!IsValid)
        {
            throw new InvalidOperationException("Cannot create characterized snapshots from an invalid literature catalog.");
        }

        if (string.IsNullOrWhiteSpace(targetId))
        {
            throw new ArgumentException("Target id is required.", nameof(targetId));
        }

        return QueryProfile(profileId)
            .Where(record => record.NormalizedValue.ValueKind == JsonValueKind.Number)
            .Select(record => new CharacterizedProfileSnapshot
            {
                Id = $"literature:{record.RecordId}",
                TargetKind = ModelBindingTargetKind.Component,
                TargetId = targetId.Trim(),
                ModelId = $"{record.ProfileId}:{record.Field}",
                OutputQuantity = record.Field,
                Units = record.NormalizedUnits,
                Value = record.NormalizedValue.GetDouble(),
                Source = $"{record.Doi}#{record.EvidenceLocator}",
                Version = record.ProfileVersion,
                Calibrated = record.Calibrated,
                Hash = record.RecordId
            })
            .ToArray();
    }

    private static List<Phase7CLiteratureSource> ParseSources(
        JsonElement root,
        string policyId,
        int minimumSiteCount,
        List<Phase7CLiteratureImportIssue> issues)
    {
        var result = new List<Phase7CLiteratureSource>();
        if (!root.TryGetProperty("sources", out var sourcesElement) || sourcesElement.ValueKind != JsonValueKind.Array)
        {
            issues.Add(new("MissingSources", "$.sources", "A sources array is required."));
            return result;
        }

        var index = 0;
        foreach (var element in sourcesElement.EnumerateArray())
        {
            var path = $"$.sources[{index}]";
            var sourceId = RequiredString(element, "sourceId", path, issues);
            var title = RequiredString(element, "title", path, issues);
            var doi = RequiredString(element, "doi", path, issues);
            var sourceHash = RequiredString(element, "sourceRecordSha256", path, issues);
            RequireSha256(sourceHash, $"{path}.sourceRecordSha256", issues);
            var verification = RequiredObject(element, "verification", path, issues);
            var verifiedPolicy = RequiredString(verification, "policyId", $"{path}.verification", issues);
            var passed = RequiredBool(verification, "passed", $"{path}.verification", issues);
            var sites = new List<Phase7CLiteratureVerificationSite>();
            var independentHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (!verification.TryGetProperty("sites", out var sitesElement) || sitesElement.ValueKind != JsonValueKind.Array)
            {
                issues.Add(new("MissingVerificationSites", $"{path}.verification.sites", "Verification sites are required."));
            }
            else
            {
                var siteIndex = 0;
                foreach (var siteElement in sitesElement.EnumerateArray())
                {
                    var sitePath = $"{path}.verification.sites[{siteIndex}]";
                    var siteName = RequiredString(siteElement, "siteName", sitePath, issues);
                    var url = RequiredString(siteElement, "url", sitePath, issues);
                    var matchedTitle = RequiredString(siteElement, "matchedTitle", sitePath, issues);
                    var claimedMatch = RequiredBool(siteElement, "normalizedFullTitleMatch", sitePath, issues);
                    var actualMatch = string.Equals(NormalizeTitle(title), NormalizeTitle(matchedTitle), StringComparison.Ordinal);
                    if (!claimedMatch || !actualMatch)
                    {
                        issues.Add(new("FullTitleMismatch", sitePath, "The complete normalized title does not match the catalog source title."));
                    }

                    if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
                    {
                        issues.Add(new("InvalidVerificationUrl", $"{sitePath}.url", "Verification URL must be an absolute HTTP(S) URL."));
                    }
                    else
                    {
                        independentHosts.Add(RemoveWwwPrefix(uri.Host));
                    }

                    sites.Add(new(siteName, url, matchedTitle, actualMatch));
                    siteIndex++;
                }
            }

            if (!passed || !string.Equals(policyId, verifiedPolicy, StringComparison.Ordinal))
            {
                issues.Add(new("UnverifiedSource", $"{path}.verification", "Source verification must pass under the catalog policy."));
            }

            if (independentHosts.Count < minimumSiteCount)
            {
                issues.Add(new("InsufficientIndependentSites", $"{path}.verification.sites", $"At least {minimumSiteCount} independent website hosts are required."));
            }

            result.Add(new(sourceId, title, doi, sourceHash, new ReadOnlyCollection<Phase7CLiteratureVerificationSite>(sites)));
            index++;
        }

        foreach (var duplicate in result.GroupBy(source => source.SourceId, StringComparer.Ordinal).Where(group => group.Count() > 1))
        {
            issues.Add(new("DuplicateSourceId", "$.sources", $"Source id '{duplicate.Key}' is duplicated."));
        }

        return result;
    }

    private static List<Phase7CLiteratureRecord> ParseRecords(
        JsonElement root,
        IReadOnlyList<Phase7CLiteratureSource> sources,
        List<Phase7CLiteratureImportIssue> issues)
    {
        var result = new List<Phase7CLiteratureRecord>();
        var sourceById = sources
            .GroupBy(source => source.SourceId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        if (!root.TryGetProperty("records", out var recordsElement) || recordsElement.ValueKind != JsonValueKind.Array)
        {
            issues.Add(new("MissingRecords", "$.records", "A records array is required."));
            return result;
        }

        var index = 0;
        foreach (var element in recordsElement.EnumerateArray())
        {
            var path = $"$.records[{index}]";
            var recordId = RequiredString(element, "recordId", path, issues);
            var sourceId = RequiredString(element, "sourceId", path, issues);
            var sourceHash = RequiredString(element, "sourceRecordSha256", path, issues);
            var statusText = RequiredString(element, "importStatus", path, issues);
            var profileId = RequiredString(element, "profileId", path, issues);
            var field = RequiredString(element, "field", path, issues);
            var mode = RequiredString(element, "mode", path, issues);
            var component = RequiredString(element, "component", path, issues);
            var technicalContext = RequiredString(element, "technicalContext", path, issues);
            var units = OptionalString(element, "normalizedUnits");
            var dataOrigin = RequiredString(element, "dataOrigin", path, issues);
            var evidenceType = RequiredString(element, "evidenceType", path, issues);
            var evidenceGrade = RequiredString(element, "evidenceGrade", path, issues);
            var conversionFormula = OptionalString(element, "conversionFormula");
            var validRange = RequiredString(element, "validRange", path, issues);
            var interpolation = RequiredString(element, "interpolationMethod", path, issues);
            var extrapolation = RequiredString(element, "extrapolationPolicy", path, issues);
            var calibrated = RequiredBool(element, "calibrated", path, issues);
            var notes = OptionalString(element, "notes");
            var title = RequiredString(element, "title", path, issues);
            var doi = RequiredString(element, "doi", path, issues);
            var locator = RequiredString(element, "evidenceLocator", path, issues);
            var version = RequiredString(element, "profileVersion", path, issues);
            var normalizedValue = OptionalElement(element, "normalizedValue");
            var reportedValueA = OptionalElement(element, "reportedValueA");
            var reportedUnitsA = OptionalString(element, "reportedUnitsA");
            var auxiliaryValueB = OptionalElement(element, "auxiliaryValueB");
            var auxiliaryUnitsB = OptionalString(element, "auxiliaryUnitsB");
            var auxiliaryValueC = OptionalElement(element, "auxiliaryValueC");
            var auxiliaryUnitsC = OptionalString(element, "auxiliaryUnitsC");
            var status = statusText switch
            {
                "active" => Phase7CLiteratureImportStatus.Active,
                "excluded_gap" => Phase7CLiteratureImportStatus.ExcludedGap,
                _ => Phase7CLiteratureImportStatus.Unknown
            };

            RequireSha256(recordId, $"{path}.recordId", issues);
            RequireSha256(sourceHash, $"{path}.sourceRecordSha256", issues);
            if (status == Phase7CLiteratureImportStatus.Unknown)
            {
                issues.Add(new("InvalidImportStatus", $"{path}.importStatus", $"Unsupported import status '{statusText}'."));
            }

            if (!string.Equals(interpolation, "none", StringComparison.OrdinalIgnoreCase) || !string.Equals(extrapolation, "Error", StringComparison.Ordinal))
            {
                issues.Add(new("NonExactPointPolicy", path, "Literature records must disable interpolation and reject extrapolation."));
            }

            if (status == Phase7CLiteratureImportStatus.Active)
            {
                if (normalizedValue.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined || string.IsNullOrWhiteSpace(units))
                {
                    issues.Add(new("IncompleteActiveRecord", path, "Active records require a normalized value and units."));
                }

                if (normalizedValue.ValueKind == JsonValueKind.Number &&
                    (!normalizedValue.TryGetDouble(out var numericValue) || double.IsNaN(numericValue) || double.IsInfinity(numericValue)))
                {
                    issues.Add(new("InvalidNumericValue", $"{path}.normalizedValue", "Numeric values must be finite doubles."));
                }
            }
            else if (status == Phase7CLiteratureImportStatus.ExcludedGap &&
                     (!string.Equals(dataOrigin, "gap", StringComparison.Ordinal) || normalizedValue.ValueKind != JsonValueKind.Null))
            {
                issues.Add(new("InvalidExcludedGap", path, "Excluded gaps must use dataOrigin 'gap' and a null normalized value."));
            }

            if (!sourceById.TryGetValue(sourceId, out var source))
            {
                issues.Add(new("UnknownSource", $"{path}.sourceId", $"Source '{sourceId}' is not in the verified source index."));
            }
            else
            {
                if (!string.Equals(NormalizeTitle(title), NormalizeTitle(source.Title), StringComparison.Ordinal))
                {
                    issues.Add(new("RecordSourceTitleMismatch", $"{path}.title", "Record title does not match its verified source title."));
                }

                if (!string.Equals(doi, source.Doi, StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add(new("RecordSourceDoiMismatch", $"{path}.doi", "Record DOI does not match its verified source DOI."));
                }
            }

            result.Add(new(
                recordId, sourceId, sourceHash, status, profileId, field, mode, component, technicalContext,
                normalizedValue, units, dataOrigin, evidenceType, evidenceGrade, conversionFormula, validRange,
                interpolation, extrapolation, calibrated, notes, reportedValueA, reportedUnitsA, auxiliaryValueB,
                auxiliaryUnitsB, auxiliaryValueC, auxiliaryUnitsC, title, doi, locator, version));
            index++;
        }

        foreach (var duplicate in result.GroupBy(record => record.RecordId, StringComparer.Ordinal).Where(group => group.Count() > 1))
        {
            issues.Add(new("DuplicateRecordId", "$.records", $"Record id '{duplicate.Key}' is duplicated."));
        }

        return result;
    }

    private static void ValidateSummary(
        JsonElement root,
        IReadOnlyCollection<Phase7CLiteratureSource> sources,
        IReadOnlyCollection<Phase7CLiteratureRecord> records,
        List<Phase7CLiteratureImportIssue> issues)
    {
        var summary = RequiredObject(root, "summary", "$", issues);
        var expected = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["sourceCount"] = sources.Count,
            ["recordCount"] = records.Count,
            ["activeRecordCount"] = records.Count(record => record.ImportStatus == Phase7CLiteratureImportStatus.Active),
            ["excludedGapRecordCount"] = records.Count(record => record.ImportStatus == Phase7CLiteratureImportStatus.ExcludedGap),
            ["profileCount"] = records.Select(record => record.ProfileId).Distinct(StringComparer.Ordinal).Count(),
            ["duplicateSourceHashGroupCount"] = records.GroupBy(record => record.SourceRecordSha256, StringComparer.Ordinal).Count(group => group.Count() > 1)
        };

        foreach (var pair in expected)
        {
            var actual = RequiredInt(summary, pair.Key, "$.summary", issues);
            if (actual != pair.Value)
            {
                issues.Add(new("SummaryMismatch", $"$.summary.{pair.Key}", $"Expected {pair.Value}, found {actual}."));
            }
        }
    }

    private static string NormalizeTitle(string value)
    {
        var normalized = ConferenceSessionPrefix.Replace(value.Normalize(NormalizationForm.FormKC), "").ToLowerInvariant();
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            var mapped = character is 'μ' or 'µ' ? 'u' : character;
            if (char.IsLetterOrDigit(mapped))
            {
                builder.Append(mapped);
            }
            else if (mapped == '×')
            {
                builder.Append('x');
            }
            else if (mapped == '²')
            {
                builder.Append('2');
            }
        }

        return builder.ToString();
    }

    private static JsonElement RequiredObject(JsonElement element, string name, string path, List<Phase7CLiteratureImportIssue> issues)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object)
        {
            return value;
        }

        issues.Add(new("MissingObject", $"{path}.{name}", "Required object is missing."));
        return default;
    }

    private static string RequiredString(JsonElement element, string name, string path, List<Phase7CLiteratureImportIssue> issues)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString() ?? "";
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        issues.Add(new("MissingString", $"{path}.{name}", "Required string is missing."));
        return "";
    }

    private static string OptionalString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static JsonElement OptionalElement(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            ? value.Clone()
            : default;

    private static int RequiredInt(JsonElement element, string name, string path, List<Phase7CLiteratureImportIssue> issues)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.TryGetInt32(out var result))
        {
            return result;
        }

        issues.Add(new("MissingInteger", $"{path}.{name}", "Required integer is missing."));
        return 0;
    }

    private static bool RequiredBool(JsonElement element, string name, string path, List<Phase7CLiteratureImportIssue> issues)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return value.GetBoolean();
        }

        issues.Add(new("MissingBoolean", $"{path}.{name}", "Required Boolean is missing."));
        return false;
    }

    private static void RequireSha256(string value, string path, List<Phase7CLiteratureImportIssue> issues)
    {
        if (value.Length != 64 || value.Any(character => !Uri.IsHexDigit(character)))
        {
            issues.Add(new("InvalidSha256", path, "Expected a 64-character hexadecimal SHA-256 value."));
        }
    }

    private static string RemoveWwwPrefix(string host) =>
        host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;

    private static string ToLowerHex(byte[] bytes)
    {
        var builder = new StringBuilder(bytes.Length * 2);
        foreach (var value in bytes)
        {
            builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }
}

/// <summary>Describes one verified literature source.</summary>
public sealed class Phase7CLiteratureSource
{
    internal Phase7CLiteratureSource(string sourceId, string title, string doi, string sourceRecordSha256, IReadOnlyList<Phase7CLiteratureVerificationSite> sites)
    {
        SourceId = sourceId;
        Title = title;
        Doi = doi;
        SourceRecordSha256 = sourceRecordSha256;
        VerificationSites = sites;
    }

    /// <summary>Gets the source identifier.</summary>
    public string SourceId { get; }
    /// <summary>Gets the complete source title.</summary>
    public string Title { get; }
    /// <summary>Gets the source DOI or standard identifier.</summary>
    public string Doi { get; }
    /// <summary>Gets the workbook source-row SHA-256.</summary>
    public string SourceRecordSha256 { get; }
    /// <summary>Gets the independent full-title verification sites.</summary>
    public IReadOnlyList<Phase7CLiteratureVerificationSite> VerificationSites { get; }
}

/// <summary>Describes one independent full-title verification site.</summary>
public sealed class Phase7CLiteratureVerificationSite
{
    internal Phase7CLiteratureVerificationSite(string siteName, string url, string matchedTitle, bool fullTitleMatch)
    {
        SiteName = siteName;
        Url = url;
        MatchedTitle = matchedTitle;
        FullTitleMatch = fullTitleMatch;
    }

    /// <summary>Gets the website name.</summary>
    public string SiteName { get; }
    /// <summary>Gets the verification URL.</summary>
    public string Url { get; }
    /// <summary>Gets the complete title found on the site.</summary>
    public string MatchedTitle { get; }
    /// <summary>Gets whether the normalized complete title matched.</summary>
    public bool FullTitleMatch { get; }
}

/// <summary>Describes one exact-point Phase 7C literature record.</summary>
public sealed class Phase7CLiteratureRecord
{
    internal Phase7CLiteratureRecord(
        string recordId,
        string sourceId,
        string sourceRecordSha256,
        Phase7CLiteratureImportStatus importStatus,
        string profileId,
        string field,
        string mode,
        string component,
        string technicalContext,
        JsonElement normalizedValue,
        string normalizedUnits,
        string dataOrigin,
        string evidenceType,
        string evidenceGrade,
        string conversionFormula,
        string validRange,
        string interpolationMethod,
        string extrapolationPolicy,
        bool calibrated,
        string notes,
        JsonElement reportedValueA,
        string reportedUnitsA,
        JsonElement auxiliaryValueB,
        string auxiliaryUnitsB,
        JsonElement auxiliaryValueC,
        string auxiliaryUnitsC,
        string title,
        string doi,
        string evidenceLocator,
        string profileVersion)
    {
        RecordId = recordId;
        SourceId = sourceId;
        SourceRecordSha256 = sourceRecordSha256;
        ImportStatus = importStatus;
        ProfileId = profileId;
        Field = field;
        Mode = mode;
        Component = component;
        TechnicalContext = technicalContext;
        NormalizedValue = normalizedValue;
        NormalizedUnits = normalizedUnits;
        DataOrigin = dataOrigin;
        EvidenceType = evidenceType;
        EvidenceGrade = evidenceGrade;
        ConversionFormula = conversionFormula;
        ValidRange = validRange;
        InterpolationMethod = interpolationMethod;
        ExtrapolationPolicy = extrapolationPolicy;
        Calibrated = calibrated;
        Notes = notes;
        ReportedValueA = reportedValueA;
        ReportedUnitsA = reportedUnitsA;
        AuxiliaryValueB = auxiliaryValueB;
        AuxiliaryUnitsB = auxiliaryUnitsB;
        AuxiliaryValueC = auxiliaryValueC;
        AuxiliaryUnitsC = auxiliaryUnitsC;
        Title = title;
        Doi = doi;
        EvidenceLocator = evidenceLocator;
        ProfileVersion = profileVersion;
    }

    /// <summary>Gets the collision-safe import record SHA-256.</summary>
    public string RecordId { get; }
    /// <summary>Gets the verified source identifier.</summary>
    public string SourceId { get; }
    /// <summary>Gets the original workbook row SHA-256, which may be reused by multiple modes.</summary>
    public string SourceRecordSha256 { get; }
    /// <summary>Gets whether this record is active or an explicitly excluded gap.</summary>
    public Phase7CLiteratureImportStatus ImportStatus { get; }
    /// <summary>Gets the exact-point profile identifier.</summary>
    public string ProfileId { get; }
    /// <summary>Gets the target field.</summary>
    public string Field { get; }
    /// <summary>Gets the reported mode or precision.</summary>
    public string Mode { get; }
    /// <summary>Gets the source component family label without mapping it to a closed enum.</summary>
    public string Component { get; }
    /// <summary>Gets the source technology and operating context.</summary>
    public string TechnicalContext { get; }
    /// <summary>Gets the normalized scalar, Boolean, or string value.</summary>
    public JsonElement NormalizedValue { get; }
    /// <summary>Gets the normalized units.</summary>
    public string NormalizedUnits { get; }
    /// <summary>Gets whether the value was reported, derived, generated, or is a gap.</summary>
    public string DataOrigin { get; }
    /// <summary>Gets the source evidence type.</summary>
    public string EvidenceType { get; }
    /// <summary>Gets the source evidence grade.</summary>
    public string EvidenceGrade { get; }
    /// <summary>Gets the exact conversion or derivation formula.</summary>
    public string ConversionFormula { get; }
    /// <summary>Gets the valid range statement.</summary>
    public string ValidRange { get; }
    /// <summary>Gets the interpolation policy.</summary>
    public string InterpolationMethod { get; }
    /// <summary>Gets the extrapolation policy.</summary>
    public string ExtrapolationPolicy { get; }
    /// <summary>Gets whether the record is based on calibrated or measured evidence.</summary>
    public bool Calibrated { get; }
    /// <summary>Gets source limitations or usage notes.</summary>
    public string Notes { get; }
    /// <summary>Gets the primary reported input value used by conversion or derivation.</summary>
    public JsonElement ReportedValueA { get; }
    /// <summary>Gets the units of the primary reported input.</summary>
    public string ReportedUnitsA { get; }
    /// <summary>Gets the optional second reported input value.</summary>
    public JsonElement AuxiliaryValueB { get; }
    /// <summary>Gets the units of the optional second input.</summary>
    public string AuxiliaryUnitsB { get; }
    /// <summary>Gets the optional third reported input value.</summary>
    public JsonElement AuxiliaryValueC { get; }
    /// <summary>Gets the units of the optional third input.</summary>
    public string AuxiliaryUnitsC { get; }
    /// <summary>Gets the source title copied into the record.</summary>
    public string Title { get; }
    /// <summary>Gets the source DOI or standard identifier.</summary>
    public string Doi { get; }
    /// <summary>Gets the precise source locator.</summary>
    public string EvidenceLocator { get; }
    /// <summary>Gets the profile version.</summary>
    public string ProfileVersion { get; }
}
/// <summary>Identifies whether a literature record can participate in characterized snapshots.</summary>
public enum Phase7CLiteratureImportStatus
{
    /// <summary>The status was not recognized.</summary>
    Unknown,
    /// <summary>The record is usable only at its reported point.</summary>
    Active,
    /// <summary>The record documents a gap and is never converted into a snapshot.</summary>
    ExcludedGap
}

/// <summary>Describes one catalog validation failure.</summary>
public sealed class Phase7CLiteratureImportIssue
{
    internal Phase7CLiteratureImportIssue(string code, string path, string message)
    {
        Code = code;
        Path = path;
        Message = message;
    }

    /// <summary>Gets the stable issue code.</summary>
    public string Code { get; }
    /// <summary>Gets the JSON path associated with the issue.</summary>
    public string Path { get; }
    /// <summary>Gets the human-readable issue text.</summary>
    public string Message { get; }
}
