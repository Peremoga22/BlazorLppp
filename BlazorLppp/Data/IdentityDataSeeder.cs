using BlazorLppp.Domain;

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace BlazorLppp.Data;

public class AdminSeedOptions
{
    public const string SectionName = "Admin";

    public string EmailAdmin { get; set; } = "admin@local.test";

    public string Password { get; set; } = "Admin123!";
}

public static class IdentityDataSeeder
{
    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var options = services.GetRequiredService<IOptions<AdminSeedOptions>>().Value;

        if (string.IsNullOrWhiteSpace(options.EmailAdmin) || string.IsNullOrWhiteSpace(options.Password))
        {
            return;
        }

        if (!await roleManager.RoleExistsAsync(AppRoles.Admin))
        {
            var roleResult = await roleManager.CreateAsync(new IdentityRole(AppRoles.Admin));
            if (!roleResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Failed to create role '{AppRoles.Admin}': {FormatErrors(roleResult)}");
            }
        }

        var admin = await userManager.FindByEmailAsync(options.EmailAdmin);
        if (admin is null)
        {
            admin = new ApplicationUser
            {
                UserName = options.EmailAdmin,
                Email = options.EmailAdmin,
                EmailConfirmed = true
            };

            var createResult = await userManager.CreateAsync(admin, options.Password);
            if (!createResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Failed to create admin user '{options.EmailAdmin}': {FormatErrors(createResult)}");
            }
        }
        else if (!admin.EmailConfirmed)
        {
            admin.EmailConfirmed = true;
            await userManager.UpdateAsync(admin);
        }

        if (!await userManager.IsInRoleAsync(admin, AppRoles.Admin))
        {
            var addRoleResult = await userManager.AddToRoleAsync(admin, AppRoles.Admin);
            if (!addRoleResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Failed to assign role '{AppRoles.Admin}' to '{options.EmailAdmin}': {FormatErrors(addRoleResult)}");
            }
        }
    }

    private static string FormatErrors(IdentityResult result) =>
        string.Join("; ", result.Errors.Select(e => e.Description));
}
