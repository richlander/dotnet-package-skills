using Newtonsoft.Json;
using AdvisoryImport;

// A real-world advisory feed returns camelCase JSON; our model uses idiomatic
// PascalCase properties. Newtonsoft.Json matches these case-insensitively by default.
string json = """
[
  { "packageName": "Contoso.Data", "affectedVersion": "1.2.3", "severity": "High", "cveId": "CVE-2025-0001" },
  { "packageName": "Contoso.Web", "affectedVersion": "4.5.6", "severity": "Critical", "cveId": "CVE-2025-0002" }
]
""";

var advisories = JsonConvert.DeserializeObject<List<Advisory>>(json)!;

foreach (var a in advisories)
{
    Console.WriteLine($"{a.PackageName} {a.AffectedVersion} [{a.Severity}] {a.CveId}");
}
