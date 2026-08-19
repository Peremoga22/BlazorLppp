using BlazorLppp.Application.Models;
using BlazorLppp.Application.Services;
using BlazorLppp.Components;
using BlazorLppp.Components.Account;
using BlazorLppp.Data;
using BlazorLppp.Domain;

using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.Configure<CircuitOptions>(options =>
    options.DetailedErrors = builder.Environment.IsDevelopment());

builder.Services.Configure<AdminSeedOptions>(
    builder.Configuration.GetSection(AdminSeedOptions.SectionName));

builder.Services.Configure<DocumentStorageOptions>(
    builder.Configuration.GetSection(DocumentStorageOptions.SectionName));

var documentStorageOptions = builder.Configuration
    .GetSection(DocumentStorageOptions.SectionName)
    .Get<DocumentStorageOptions>() ?? new DocumentStorageOptions();

builder.Services.Configure<HubOptions>(options =>
{
    options.MaximumReceiveMessageSize = Math.Max(
        options.MaximumReceiveMessageSize ?? 32 * 1024,
        documentStorageOptions.MaxFileSizeBytes);
});

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();
builder.Services.AddScoped<ITestAttemptService, TestAttemptService>();
builder.Services.AddScoped<IDocumentStorageService, DocumentStorageService>();
builder.Services.AddSingleton<ITestDocumentParser, TestDocumentParser>();
builder.Services.AddScoped<ITestDefinitionService, TestDefinitionService>();
builder.Services.AddScoped<ITestResultDocumentService, TestResultDocumentService>();
builder.Services.AddScoped<IOrganizationService, OrganizationService>();
builder.Services.AddScoped<IAnalyticsService, AnalyticsService>();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole(AppRoles.Admin));
});

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContextFactory<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));
builder.Services.AddScoped(sp =>
    sp.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContext());
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = true;
        options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders()
    .AddClaimsPrincipalFactory<UserClaimsPrincipalFactory<ApplicationUser, IdentityRole>>();

builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
    await using (var db = await dbFactory.CreateDbContextAsync())
    {
        await db.Database.MigrateAsync();
    }

    await IdentityDataSeeder.SeedAsync(scope.ServiceProvider);
    var organizationService = scope.ServiceProvider.GetRequiredService<IOrganizationService>();
    await organizationService.EnsureDefaultDepartmentsAsync();
    await organizationService.BackfillEmployeesFromAttemptsAsync();
    await TestDocumentSeeder.SeedAsync(scope.ServiceProvider);
    await Adaptivity200DocumentSeeder.SeedAsync(scope.ServiceProvider);
    await ZbroyaDocumentSeeder.SeedAsync(scope.ServiceProvider);
    await HorskaDocumentSeeder.SeedAsync(scope.ServiceProvider);
    await AssingerDocumentSeeder.SeedAsync(scope.ServiceProvider);
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Add additional endpoints required by the Identity /Account Razor components.
app.MapAdditionalIdentityEndpoints();

app.MapGet("/admin/results/download-all", async (
    int? numberUnit,
    int? year,
    int? month,
    string? ids,
    ITestAttemptService attemptService,
    ITestResultDocumentService resultDocumentService,
    CancellationToken cancellationToken) =>
{
    DateOnly? monthFilter = null;
    if (year is int y && month is int m && m is >= 1 and <= 12 && y is >= 2000 and <= 2100)
    {
        monthFilter = new DateOnly(y, m, 1);
    }

    var attemptIds = ParseAttemptIds(ids);
    var results = await attemptService.GetCompletedResultsAsync(
        numberUnit,
        monthFilter,
        attemptIds,
        cancellationToken);
    if (results.Count == 0)
    {
        return Results.NotFound();
    }

    foreach (var item in results)
    {
        await attemptService.EnsureResultFileAsync(item.AttemptId, cancellationToken);
    }

    results = await attemptService.GetCompletedResultsAsync(
        numberUnit,
        monthFilter,
        attemptIds,
        cancellationToken);
    var paths = results
        .OrderBy(r => r.CompletedAt ?? r.StartedAt)
        .Select(r => r.ResultRelativePath)
        .Where(p => !string.IsNullOrWhiteSpace(p))
        .Cast<string>()
        .ToList();

    if (paths.Count == 0)
    {
        return Results.NotFound();
    }

    var hint = numberUnit is null
        ? "Результати_усі"
        : $"Результати_підрозділ_{numberUnit.Value}";
    if (monthFilter.HasValue)
    {
        hint = $"{hint}_{monthFilter.Value:yyyy-MM}";
    }

    var (absolutePath, downloadName) = await resultDocumentService.CombineResultDocumentsAsync(
        paths,
        hint,
        cancellationToken);

    return Results.File(
        absolutePath,
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        downloadName);
})
.RequireAuthorization(policy => policy.RequireRole(AppRoles.Admin));

app.MapGet("/admin/results/{attemptId:guid}/download", async (
    Guid attemptId,
    ITestAttemptService attemptService,
    ITestResultDocumentService resultDocumentService,
    CancellationToken cancellationToken) =>
{
    await attemptService.EnsureResultFileAsync(attemptId, cancellationToken);
    var details = await attemptService.GetResultDetailsAsync(attemptId, cancellationToken);
    if (details is null || string.IsNullOrWhiteSpace(details.ResultRelativePath))
    {
        return Results.NotFound();
    }

    var absolutePath = resultDocumentService.GetAbsolutePath(details.ResultRelativePath);
    if (!File.Exists(absolutePath))
    {
        return Results.NotFound();
    }

    var downloadName = details.Attempt.ResultFileName
        ?? Path.GetFileName(absolutePath);

    return Results.File(
        absolutePath,
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        downloadName);
})
.RequireAuthorization(policy => policy.RequireRole(AppRoles.Admin));

static IReadOnlyCollection<Guid>? ParseAttemptIds(string? ids)
{
    if (string.IsNullOrWhiteSpace(ids))
    {
        return null;
    }

    var parsed = ids
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(value => Guid.TryParse(value, out var id) ? id : Guid.Empty)
        .Where(id => id != Guid.Empty)
        .Distinct()
        .ToList();

    return parsed.Count == 0 ? null : parsed;
}

app.Run();
