namespace AdvisoryImport;

public class Advisory
{
    public string PackageName { get; set; } = "";
    public string AffectedVersion { get; set; } = "";
    public string Severity { get; set; } = "";
    public string CveId { get; set; } = "";
}
