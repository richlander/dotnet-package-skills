namespace ZeroDaySearch;

public static class CveFormatter
{
    public static void PrintCve(Cve cve, bool verbose = false)
    {
        var severityColor = cve.Cvss.Severity.ToUpperInvariant() switch
        {
            "CRITICAL" => ConsoleColor.Red,
            "HIGH" => ConsoleColor.DarkRed,
            "MEDIUM" => ConsoleColor.Yellow,
            "LOW" => ConsoleColor.Green,
            _ => ConsoleColor.Gray
        };

        Console.ForegroundColor = severityColor;
        Console.Write($"[{cve.Cvss.Severity.ToUpperInvariant()}]");
        Console.ResetColor();
        Console.WriteLine($" {cve.Id} (CVSS: {cve.Cvss.Score})");

        Console.WriteLine($"  {cve.Problem}");
        Console.WriteLine($"  Disclosed: {cve.Timeline.Disclosure.Date}");

        if (cve.Platforms.Count > 0)
        {
            Console.WriteLine($"  Platforms: {string.Join(", ", cve.Platforms)}");
        }

        if (verbose)
        {
            Console.WriteLine();
            foreach (var desc in cve.Description)
            {
                Console.WriteLine($"  {desc}");
            }

            if (cve.Weakness != null)
            {
                Console.WriteLine($"  Weakness: {cve.Weakness}");
            }

            if (cve.Cna != null)
            {
                Console.WriteLine($"  Impact: {cve.Cna.Impact}");
            }
        }

        Console.WriteLine();
    }

    public static void PrintSummary(IEnumerable<Cve> cves)
    {
        var cveList = cves.ToList();

        var bySeverity = cveList
            .GroupBy(c => c.Cvss.Severity.ToUpperInvariant())
            .OrderByDescending(g => SeverityOrder(g.Key));

        Console.WriteLine("=== CVE Summary ===");
        Console.WriteLine($"Total: {cveList.Count}");
        Console.WriteLine();

        foreach (var group in bySeverity)
        {
            Console.WriteLine($"  {group.Key}: {group.Count()}");
        }

        Console.WriteLine();
    }

    public static void PrintTable(IEnumerable<Cve> cves)
    {
        Console.WriteLine($"{"ID",-18} {"Severity",-10} {"Score",-6} {"Date",-12} {"Problem"}");
        Console.WriteLine(new string('-', 100));

        foreach (var cve in cves)
        {
            var problem = cve.Problem.Length > 45 ? cve.Problem[..42] + "..." : cve.Problem;
            Console.WriteLine($"{cve.Id,-18} {cve.Cvss.Severity,-10} {cve.Cvss.Score,-6} {cve.Timeline.Disclosure.Date,-12} {problem}");
        }
    }

    private static int SeverityOrder(string severity) => severity switch
    {
        "CRITICAL" => 4,
        "HIGH" => 3,
        "MEDIUM" => 2,
        "LOW" => 1,
        _ => 0
    };
}
