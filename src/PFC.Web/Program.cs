using Google.Apis.Auth.OAuth2;
using Google.Cloud.Firestore;
using Google.Cloud.Storage.V1;
using Microsoft.Extensions.Options;
using PFC.Web.Configuration;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .Configure<GcpOptions>(builder.Configuration.GetSection(GcpOptions.SectionName));

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
    if (!string.IsNullOrWhiteSpace(gcp.SigningCredentialPath))
        return UrlSigner.FromCredential(CredentialFactory.FromFile<ServiceAccountCredential>(gcp.SigningCredentialPath));
    return UrlSigner.FromCredential(GoogleCredential.GetApplicationDefault());
});

builder.Services.AddControllersWithViews();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
