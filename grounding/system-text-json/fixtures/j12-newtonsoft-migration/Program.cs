using Newtonsoft.Json;

var report = new PackageReport();
var settings = new JsonSerializerSettings
{
    NullValueHandling = NullValueHandling.Ignore,
    Formatting = Formatting.Indented
};
var json = JsonConvert.SerializeObject(report, settings);
Console.WriteLine(json);
var roundTrip = JsonConvert.DeserializeObject<PackageReport>("""{"packageId":"System.Text.Json","downloadCount":12345,"ownerName":"runtime"}""")!;
Console.WriteLine($"package={roundTrip.PackageId} owner={roundTrip.OwnerName} downloads={roundTrip.DownloadCount}");

class PackageReport
{
    [JsonProperty("packageId")]
    public string PackageId = "System.Text.Json";

    [JsonProperty("downloadCount")]
    public int? DownloadCount { get; set; }

    [JsonProperty("notes")]
    public string? Notes { get; set; }

    public string? OwnerName { get; set; }
}
