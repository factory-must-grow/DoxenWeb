using Doxen.Web.Data;
using Microsoft.AspNetCore.Identity;

namespace Doxen.Web.Services;

// Создание первого администратора при старте на чистой базе. Если в
// системе уже есть хотя бы один пользователь в роли Admin — ничего не
// делает, поэтому повторный старт безопасен.
public static class AdminSeeder
{
    private const string AdminRole = "Admin";

    public static async Task SeedAsync(IServiceProvider services, ILogger logger)
    {
        var userManager = services.GetRequiredService<UserManager<DoxenUser>>();
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole<long>>>();

        var existingAdmins = await userManager.GetUsersInRoleAsync(AdminRole);
        if (existingAdmins.Count > 0)
        {
            return;
        }

        var email = Environment.GetEnvironmentVariable("DOXEN_ADMIN_EMAIL");
        var password = Environment.GetEnvironmentVariable("DOXEN_ADMIN_PASSWORD");

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning(
                "Администратор не создан: не заданы переменные окружения DOXEN_ADMIN_EMAIL и DOXEN_ADMIN_PASSWORD.");
            return;
        }

        if (!await roleManager.RoleExistsAsync(AdminRole))
        {
            await roleManager.CreateAsync(new IdentityRole<long>(AdminRole));
        }

        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            user = new DoxenUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
            };

            var createResult = await userManager.CreateAsync(user, password);
            if (!createResult.Succeeded)
            {
                var errors = string.Join("; ", createResult.Errors.Select(e => e.Description));
                logger.LogError("Не удалось создать администратора: {Errors}", errors);
                return;
            }
        }

        await userManager.AddToRoleAsync(user, AdminRole);
    }
}
