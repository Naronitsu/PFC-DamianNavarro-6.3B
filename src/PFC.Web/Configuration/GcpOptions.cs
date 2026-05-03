namespace PFC.Web.Configuration;

public class GcpOptions
{
    public const string SectionName = "Gcp";

    public string ProjectId { get; set; } = string.Empty;

    public string StorageBucket { get; set; } = string.Empty;

    public string FirestoreDatabaseId { get; set; } = string.Empty;

    public string Location { get; set; } = string.Empty;

    public string SigningCredentialPath { get; set; } = string.Empty;
}
