using Google.Apis.Auth.OAuth2;
using Google.Cloud.Firestore;
using Google.Cloud.PubSub.V1;
using Google.Cloud.SecretManager.V1;
using Google.Cloud.Storage.V1;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using PFC.Web.Configuration;
using PFC.Web.Filters;
using PFC.Web.Services;

AppContext.SetSwitch("System.Net.SocketsHttpHandler.Http3Support", false);

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .Configure<GcpOptions>(builder.Configuration.GetSection(GcpOptions.SectionName));

var gcpSnap = builder.Configuration.GetSection(GcpOptions.SectionName).Get<GcpOptions>() ?? new GcpOptions();

string googleClientSecret = "";
if (gcpSnap.OAuthConfigured)
{
    googleClientSecret = gcpSnap.OAuthGoogleClientSecret?.Trim() ?? "";
    if (string.IsNullOrWhiteSpace(googleClientSecret))
    {
        var sm = SecretManagerServiceClient.Create();
        var vn = new SecretVersionName(gcpSnap.ProjectId, gcpSnap.OAuthClientSecretSecretId, "latest");
        var sv = await sm.AccessSecretVersionAsync(vn).ConfigureAwait(false);
        googleClientSecret = sv.Payload.Data.ToStringUtf8().Trim();
    }
    if (string.IsNullOrWhiteSpace(googleClientSecret))
        throw new InvalidOperationException(
            "OAuth client secret from Secret Manager was empty. Check the secret value and Gcp:OAuthClientSecretSecretId.");
}

var oauthReady = gcpSnap.OAuthConfigured && !string.IsNullOrWhiteSpace(googleClientSecret);

var authBuilder = builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = oauthReady
        ? GoogleDefaults.AuthenticationScheme
        : CookieAuthenticationDefaults.AuthenticationScheme;
});

authBuilder.AddCookie(o =>
{
    o.LoginPath = "/Account/Login";
});

if (oauthReady)
{
    authBuilder.AddGoogle(options =>
    {
        options.ClientId = gcpSnap.OAuthGoogleClientId;
        options.ClientSecret = googleClientSecret;
        options.CallbackPath = "/signin-google";
        options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    });
}

builder.Services.AddAuthorization();
builder.Services.AddScoped<RequireGoogleForUploadFilter>();

builder.Services.AddSingleton(sp =>
{
    var gcp = sp.GetRequiredService<IOptions<GcpOptions>>().Value;
    if (string.IsNullOrWhiteSpace(gcp.ProjectId))
        throw new InvalidOperationException("Set Gcp:ProjectId in configuration.");
    if (string.IsNullOrWhiteSpace(gcp.FirestoreDatabaseId))
        throw new InvalidOperationException("Set Gcp:FirestoreDatabaseId in configuration.");

    return new FirestoreDbBuilder
    {
        ProjectId = gcp.ProjectId,
        DatabaseId = gcp.FirestoreDatabaseId
    }.Build();
});

builder.Services.AddSingleton(_ => StorageClient.Create());

builder.Services.AddSingleton(sp =>
{
    var gcp = sp.GetRequiredService<IOptions<GcpOptions>>().Value;
    if (!string.IsNullOrWhiteSpace(gcp.SigningCredentialPath) && File.Exists(gcp.SigningCredentialPath))
        return UrlSigner.FromCredential(CredentialFactory.FromFile<ServiceAccountCredential>(gcp.SigningCredentialPath));

    var adc = GoogleCredential.GetApplicationDefault();
    if (adc.UnderlyingCredential is UserCredential)
        throw new InvalidOperationException(
            "Gcp:SigningCredentialPath must point to a service account JSON key file. User credentials from \"gcloud auth application-default login\" cannot sign GCS URLs.");

    return UrlSigner.FromCredential(adc);
});

builder.Services.AddSingleton(sp =>
{
    var gcp = sp.GetRequiredService<IOptions<GcpOptions>>().Value;
    var topicId = string.IsNullOrWhiteSpace(gcp.MenuUploadsPubSubTopic)
        ? "menu-uploads-topic"
        : gcp.MenuUploadsPubSubTopic;
    var topicName = Google.Cloud.PubSub.V1.TopicName.FromProjectTopic(gcp.ProjectId, topicId);
    return PublisherClient.Create(topicName);
});

builder.Services.AddSingleton<MenuUploadNotificationService>();

builder.Services.AddMemoryCache();
builder.Services.AddHttpClient();
builder.Services.AddScoped<TranslationProxyService>();

builder.Services.AddControllersWithViews();

var app = builder.Build();

var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
};
forwardedHeadersOptions.KnownNetworks.Clear();
forwardedHeadersOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeadersOptions);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
