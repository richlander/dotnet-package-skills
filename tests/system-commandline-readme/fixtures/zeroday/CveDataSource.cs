using Newtonsoft.Json;

namespace ZeroDaySearch;

public class CveDataSource
{
    private const string ReleaseIndexUrl = "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/index.json";
    private readonly HttpClient _httpClient;

    public CveDataSource(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<ReleaseIndex> GetReleaseIndexAsync()
    {
        var json = await _httpClient.GetStringAsync(ReleaseIndexUrl);
        return JsonConvert.DeserializeObject<ReleaseIndex>(json)
            ?? throw new InvalidOperationException("Failed to deserialize release index");
    }

    public async Task<MajorReleaseIndex> GetMajorReleaseAsync(string url)
    {
        var json = await _httpClient.GetStringAsync(url);
        return JsonConvert.DeserializeObject<MajorReleaseIndex>(json)
            ?? throw new InvalidOperationException($"Failed to deserialize major release from {url}");
    }

    public async Task<CveRecords> GetCveRecordsAsync(string url)
    {
        var json = await _httpClient.GetStringAsync(url);
        return JsonConvert.DeserializeObject<CveRecords>(json)
            ?? throw new InvalidOperationException($"Failed to deserialize CVE records from {url}");
    }

    public async Task<List<CveRecords>> GetAllCveRecordsAsync(IEnumerable<string>? versions = null)
    {
        var releaseIndex = await GetReleaseIndexAsync();
        var allCveRecords = new List<CveRecords>();
        var seenCveUrls = new HashSet<string>();

        var releases = releaseIndex.Embedded.Releases
            .Where(r => versions == null || !versions.Any() || versions.Contains(r.Version));

        foreach (var release in releases)
        {
            try
            {
                var majorRelease = await GetMajorReleaseAsync(release.Links.Self.Href);

                foreach (var patch in majorRelease.Embedded.Patches.Where(p => p.Security && p.Links.CveJson != null))
                {
                    var cveUrl = patch.Links.CveJson!.Href;
                    if (seenCveUrls.Add(cveUrl))
                    {
                        try
                        {
                            var cveRecords = await GetCveRecordsAsync(cveUrl);
                            allCveRecords.Add(cveRecords);
                        }
                        catch (HttpRequestException)
                        {
                            // Skip unavailable CVE files
                        }
                    }
                }
            }
            catch (HttpRequestException)
            {
                // Skip unavailable releases
            }
        }

        return allCveRecords;
    }

    public IEnumerable<Cve> FilterBySeverity(IEnumerable<CveRecords> records, string severity)
    {
        return records
            .SelectMany(r => r.Disclosures)
            .Where(c => c.Cvss.Severity.Equals(severity, StringComparison.OrdinalIgnoreCase))
            .DistinctBy(c => c.Id);
    }

    public IEnumerable<Cve> FilterByProduct(IEnumerable<CveRecords> records, string product)
    {
        var cveIds = records
            .SelectMany(r => r.Products)
            .Where(p => p.Name.Contains(product, StringComparison.OrdinalIgnoreCase))
            .Select(p => p.CveId)
            .ToHashSet();

        return records
            .SelectMany(r => r.Disclosures)
            .Where(c => cveIds.Contains(c.Id))
            .DistinctBy(c => c.Id);
    }

    public IEnumerable<Cve> FilterByDateRange(IEnumerable<CveRecords> records, DateOnly from, DateOnly to)
    {
        return records
            .SelectMany(r => r.Disclosures)
            .Where(c =>
            {
                if (DateOnly.TryParse(c.Timeline.Disclosure.Date, out var date))
                {
                    return date >= from && date <= to;
                }
                return false;
            })
            .DistinctBy(c => c.Id);
    }

    public IEnumerable<Cve> FilterByPlatform(IEnumerable<CveRecords> records, string platform)
    {
        return records
            .SelectMany(r => r.Disclosures)
            .Where(c => c.Platforms.Any(p =>
                p.Equals(platform, StringComparison.OrdinalIgnoreCase) ||
                p.Equals("all", StringComparison.OrdinalIgnoreCase)))
            .DistinctBy(c => c.Id);
    }

    public IEnumerable<Cve> GetLatestCves(IEnumerable<CveRecords> records, int count)
    {
        return records
            .SelectMany(r => r.Disclosures)
            .DistinctBy(c => c.Id)
            .OrderByDescending(c => c.Timeline.Disclosure.Date)
            .Take(count);
    }

    public IEnumerable<Cve> SearchByKeyword(IEnumerable<CveRecords> records, string keyword)
    {
        return records
            .SelectMany(r => r.Disclosures)
            .Where(c =>
                c.Id.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                c.Problem.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                c.Description.Any(d => d.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            .DistinctBy(c => c.Id);
    }
}
