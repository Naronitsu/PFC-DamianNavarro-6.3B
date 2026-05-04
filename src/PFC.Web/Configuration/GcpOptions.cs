namespace PFC.Web.Configuration;

public class GcpOptions
{
    public const string SectionName = "Gcp";

    public string ProjectId { get; set; } = string.Empty;

    public string StorageBucket { get; set; } = string.Empty;

    public string FirestoreDatabaseId { get; set; } = string.Empty;

    public string Location { get; set; } = string.Empty;

    public string SigningCredentialPath { get; set; } = string.Empty;

    public string MenuUploadsPubSubTopic { get; set; } = "menu-uploads-topic";

    public string TranslateFunctionUrl { get; set; } = string.Empty;

    public string OAuthGoogleClientId { get; set; } = string.Empty;

    public string OAuthClientSecretSecretId { get; set; } = string.Empty;

    public string OAuthGoogleClientSecret { get; set; } = string.Empty;

    public bool OAuthConfigured =>
        !string.IsNullOrWhiteSpace(OAuthGoogleClientId)
        && (!string.IsNullOrWhiteSpace(OAuthGoogleClientSecret)
            || !string.IsNullOrWhiteSpace(OAuthClientSecretSecretId));
}
