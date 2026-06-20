using System.CommandLine;
using ZeroDaySearch;

var rootCommand = new RootCommand(".NET CVE Search Tool - Search and filter .NET security vulnerabilities");

// Global options
var versionsOption = new Option<string[]>(
    aliases: ["--versions", "-v"],
    description: "Filter by .NET versions (e.g., 8.0 9.0)")
{
    AllowMultipleArgumentsPerToken = true
};

// NOTE (intentional migration trap): this uses the beta4 *positional* constructor
// where the 2nd argument is the option DESCRIPTION. In 3.x the 2nd positional ctor
// argument is an ALIAS, so a naive "keep it as-is" migration compiles but silently
// turns the description into a bogus alias and drops the help text. A single word is
// used so the break is silent (a multi-word value throws at runtime on the alias).
var verboseOption = new Option<bool>("--verbose", "detailed");

var tableOption = new Option<bool>(
    aliases: ["--table", "-t"],
    description: "Output as table format");

// List command - list latest CVEs
var listCommand = new Command("list", "List recent CVEs");
var countOption = new Option<int>(
    aliases: ["--count", "-n"],
    getDefaultValue: () => 10,
    description: "Number of CVEs to display");

listCommand.AddOption(countOption);
listCommand.AddOption(versionsOption);
listCommand.AddOption(verboseOption);
listCommand.AddOption(tableOption);

Handler.SetHandler(listCommand, async (int count, string[] versions, bool verbose, bool table) =>
{
    var dataSource = new CveDataSource();
    Console.WriteLine("Fetching CVE data...");

    var records = await dataSource.GetAllCveRecordsAsync(versions);
    var cves = dataSource.GetLatestCves(records, count).ToList();

    Console.WriteLine($"Found {cves.Count} CVEs\n");

    if (table)
    {
        CveFormatter.PrintTable(cves);
    }
    else
    {
        foreach (var cve in cves)
        {
            CveFormatter.PrintCve(cve, verbose);
        }
    }
}, countOption, versionsOption, verboseOption, tableOption);

// Search command - search by keyword
var searchCommand = new Command("search", "Search CVEs by keyword");
var keywordArgument = new Argument<string>("keyword", "Search term (CVE ID, description, problem)");

searchCommand.AddArgument(keywordArgument);
searchCommand.AddOption(versionsOption);
searchCommand.AddOption(verboseOption);
searchCommand.AddOption(tableOption);

Handler.SetHandler(searchCommand, async (string keyword, string[] versions, bool verbose, bool table) =>
{
    var dataSource = new CveDataSource();
    Console.WriteLine($"Searching for '{keyword}'...");

    var records = await dataSource.GetAllCveRecordsAsync(versions);
    var cves = dataSource.SearchByKeyword(records, keyword).ToList();

    Console.WriteLine($"Found {cves.Count} matching CVEs\n");

    if (table)
    {
        CveFormatter.PrintTable(cves);
    }
    else
    {
        foreach (var cve in cves)
        {
            CveFormatter.PrintCve(cve, verbose);
        }
    }
}, keywordArgument, versionsOption, verboseOption, tableOption);

// Severity command - filter by severity
var severityCommand = new Command("severity", "Filter CVEs by severity level");
var severityArgument = new Argument<string>(
    "level",
    "Severity level (CRITICAL, HIGH, MEDIUM, LOW)");

severityCommand.AddArgument(severityArgument);
severityCommand.AddOption(versionsOption);
severityCommand.AddOption(verboseOption);
severityCommand.AddOption(tableOption);

Handler.SetHandler(severityCommand, async (string level, string[] versions, bool verbose, bool table) =>
{
    var dataSource = new CveDataSource();
    Console.WriteLine($"Fetching {level.ToUpperInvariant()} severity CVEs...");

    var records = await dataSource.GetAllCveRecordsAsync(versions);
    var cves = dataSource.FilterBySeverity(records, level).ToList();

    Console.WriteLine($"Found {cves.Count} {level.ToUpperInvariant()} CVEs\n");

    if (table)
    {
        CveFormatter.PrintTable(cves);
    }
    else
    {
        foreach (var cve in cves)
        {
            CveFormatter.PrintCve(cve, verbose);
        }
    }
}, severityArgument, versionsOption, verboseOption, tableOption);

// Product command - filter by product
var productCommand = new Command("product", "Filter CVEs by affected product");
var productArgument = new Argument<string>(
    "name",
    "Product name (runtime, aspnetcore, sdk, etc.)");

productCommand.AddArgument(productArgument);
productCommand.AddOption(versionsOption);
productCommand.AddOption(verboseOption);
productCommand.AddOption(tableOption);

Handler.SetHandler(productCommand, async (string name, string[] versions, bool verbose, bool table) =>
{
    var dataSource = new CveDataSource();
    Console.WriteLine($"Fetching CVEs affecting '{name}'...");

    var records = await dataSource.GetAllCveRecordsAsync(versions);
    var cves = dataSource.FilterByProduct(records, name).ToList();

    Console.WriteLine($"Found {cves.Count} CVEs affecting '{name}'\n");

    if (table)
    {
        CveFormatter.PrintTable(cves);
    }
    else
    {
        foreach (var cve in cves)
        {
            CveFormatter.PrintCve(cve, verbose);
        }
    }
}, productArgument, versionsOption, verboseOption, tableOption);

// Platform command - filter by platform
var platformCommand = new Command("platform", "Filter CVEs by affected platform");
var platformArgument = new Argument<string>(
    "name",
    "Platform name (linux, windows, macos)");

platformCommand.AddArgument(platformArgument);
platformCommand.AddOption(versionsOption);
platformCommand.AddOption(verboseOption);
platformCommand.AddOption(tableOption);

Handler.SetHandler(platformCommand, async (string name, string[] versions, bool verbose, bool table) =>
{
    var dataSource = new CveDataSource();
    Console.WriteLine($"Fetching CVEs affecting '{name}' platform...");

    var records = await dataSource.GetAllCveRecordsAsync(versions);
    var cves = dataSource.FilterByPlatform(records, name).ToList();

    Console.WriteLine($"Found {cves.Count} CVEs affecting '{name}'\n");

    if (table)
    {
        CveFormatter.PrintTable(cves);
    }
    else
    {
        foreach (var cve in cves)
        {
            CveFormatter.PrintCve(cve, verbose);
        }
    }
}, platformArgument, versionsOption, verboseOption, tableOption);

// Date range command
var dateCommand = new Command("date", "Filter CVEs by date range");
var fromOption = new Option<DateOnly>(
    aliases: ["--from", "-f"],
    description: "Start date (yyyy-MM-dd)");
var toOption = new Option<DateOnly>(
    aliases: ["--to"],
    getDefaultValue: () => DateOnly.FromDateTime(DateTime.Today),
    description: "End date (yyyy-MM-dd)");

dateCommand.AddOption(fromOption);
dateCommand.AddOption(toOption);
dateCommand.AddOption(versionsOption);
dateCommand.AddOption(verboseOption);
dateCommand.AddOption(tableOption);

Handler.SetHandler(dateCommand, async (DateOnly from, DateOnly to, string[] versions, bool verbose, bool table) =>
{
    var dataSource = new CveDataSource();
    Console.WriteLine($"Fetching CVEs from {from} to {to}...");

    var records = await dataSource.GetAllCveRecordsAsync(versions);
    var cves = dataSource.FilterByDateRange(records, from, to).ToList();

    Console.WriteLine($"Found {cves.Count} CVEs in date range\n");

    if (table)
    {
        CveFormatter.PrintTable(cves);
    }
    else
    {
        foreach (var cve in cves)
        {
            CveFormatter.PrintCve(cve, verbose);
        }
    }
}, fromOption, toOption, versionsOption, verboseOption, tableOption);

// Summary command
var summaryCommand = new Command("summary", "Show CVE statistics summary");
summaryCommand.AddOption(versionsOption);

Handler.SetHandler(summaryCommand, async (string[] versions) =>
{
    var dataSource = new CveDataSource();
    Console.WriteLine("Fetching CVE data...\n");

    var records = await dataSource.GetAllCveRecordsAsync(versions);
    var allCves = records.SelectMany(r => r.Disclosures).DistinctBy(c => c.Id).ToList();

    CveFormatter.PrintSummary(allCves);

    // Product breakdown
    var products = records
        .SelectMany(r => r.Products)
        .GroupBy(p => p.Name)
        .OrderByDescending(g => g.Select(p => p.CveId).Distinct().Count());

    Console.WriteLine("=== By Product ===");
    foreach (var product in products.Take(10))
    {
        var cveCount = product.Select(p => p.CveId).Distinct().Count();
        Console.WriteLine($"  {product.Key}: {cveCount}");
    }
}, versionsOption);

// Sample command - parse a bundled sample.json and print one CVE summary.
// Uses System.Text.Json. sample.json has camelCase keys ("id","severity") and
// CveSummary has no [JsonPropertyName] mappings, so binding depends on STJ's
// case-matching behavior.
var sampleCommand = new Command("sample", "Print a sample CVE parsed from sample.json");
Handler.SetHandler(sampleCommand, () =>
{
    string json = File.ReadAllText("sample.json");
    var summary = System.Text.Json.JsonSerializer.Deserialize<CveSummary>(json)!;
    Console.WriteLine($"Sample: {summary.Id} / {summary.Severity}");
});

// Add all commands
rootCommand.AddCommand(listCommand);
rootCommand.AddCommand(searchCommand);
rootCommand.AddCommand(severityCommand);
rootCommand.AddCommand(productCommand);
rootCommand.AddCommand(platformCommand);
rootCommand.AddCommand(dateCommand);
rootCommand.AddCommand(summaryCommand);
rootCommand.AddCommand(sampleCommand);

return await rootCommand.InvokeAsync(args);
