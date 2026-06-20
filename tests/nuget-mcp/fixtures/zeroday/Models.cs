using Newtonsoft.Json;

namespace ZeroDaySearch;

public record ReleaseIndex(
    [property: JsonProperty("latest_major")] string LatestMajor,
    [property: JsonProperty("latest_lts_major")] string LatestLtsMajor,
    [property: JsonProperty("_embedded")] EmbeddedReleases Embedded
);

public record EmbeddedReleases(
    [property: JsonProperty("releases")] IList<ReleaseEntry> Releases
);

public record ReleaseEntry(
    [property: JsonProperty("version")] string Version,
    [property: JsonProperty("release_type")] string ReleaseType,
    [property: JsonProperty("supported")] bool Supported,
    [property: JsonProperty("_links")] ReleaseLinks Links
);

public record ReleaseLinks(
    [property: JsonProperty("self")] LinkRef Self
);

public record LinkRef(
    [property: JsonProperty("href")] string Href
);

public record MajorReleaseIndex(
    [property: JsonProperty("version")] string Version,
    [property: JsonProperty("release_type")] string ReleaseType,
    [property: JsonProperty("supported")] bool Supported,
    [property: JsonProperty("latest_patch")] string LatestPatch,
    [property: JsonProperty("latest_security_patch")] string? LatestSecurityPatch,
    [property: JsonProperty("_links")] MajorReleaseLinks Links,
    [property: JsonProperty("_embedded")] EmbeddedPatches Embedded
);

public record MajorReleaseLinks(
    [property: JsonProperty("self")] LinkRef Self,
    [property: JsonProperty("latest-cve-json")] LinkRef? LatestCveJson
);

public record EmbeddedPatches(
    [property: JsonProperty("patches")] IList<PatchEntry> Patches
);

public record PatchEntry(
    [property: JsonProperty("version")] string Version,
    [property: JsonProperty("date")] string Date,
    [property: JsonProperty("security")] bool Security,
    [property: JsonProperty("_links")] PatchLinks Links
);

public record PatchLinks(
    [property: JsonProperty("self")] LinkRef Self,
    [property: JsonProperty("cve-json")] LinkRef? CveJson
);

public record CveRecords(
    [property: JsonProperty("last_updated")] string LastUpdated,
    [property: JsonProperty("title")] string Title,
    [property: JsonProperty("disclosures")] IList<Cve> Disclosures,
    [property: JsonProperty("products")] IList<Product> Products
);

public record Cve(
    [property: JsonProperty("id")] string Id,
    [property: JsonProperty("problem")] string Problem,
    [property: JsonProperty("description")] IList<string> Description,
    [property: JsonProperty("cvss")] Cvss Cvss,
    [property: JsonProperty("timeline")] Timeline Timeline,
    [property: JsonProperty("platforms")] IList<string> Platforms,
    [property: JsonProperty("weakness")] string? Weakness,
    [property: JsonProperty("cna")] Cna? Cna
);

public record Cvss(
    [property: JsonProperty("version")] string Version,
    [property: JsonProperty("vector")] string Vector,
    [property: JsonProperty("score")] decimal Score,
    [property: JsonProperty("severity")] string Severity
);

public record Timeline(
    [property: JsonProperty("disclosure")] TimelineEvent Disclosure,
    [property: JsonProperty("fixed")] TimelineEvent? Fixed
);

public record TimelineEvent(
    [property: JsonProperty("date")] string Date,
    [property: JsonProperty("description")] string Description
);

public record Cna(
    [property: JsonProperty("name")] string Name,
    [property: JsonProperty("severity")] string? Severity,
    [property: JsonProperty("impact")] string? Impact
);

public record Product(
    [property: JsonProperty("cve_id")] string CveId,
    [property: JsonProperty("name")] string Name,
    [property: JsonProperty("min_vulnerable")] string MinVulnerable,
    [property: JsonProperty("max_vulnerable")] string MaxVulnerable,
    [property: JsonProperty("fixed")] string Fixed,
    [property: JsonProperty("release")] string Release
);
