using System.Security.Claims;
using Doxen.Data;
using Doxen.Web.Data;
using Microsoft.AspNetCore.Identity;

namespace Doxen.Web.Services;

// Тариф текущего пользователя — free для анонимов, тариф из профиля
// для вошедших (с откатом на free, если профиль почему-то не нашёлся).
public sealed class CurrentPlanResolver
{
    private readonly UserManager<DoxenUser> _userManager;
    private readonly UserProfileRepository _userProfileRepository;
    private readonly PlanRepository _planRepository;

    public CurrentPlanResolver(UserManager<DoxenUser> userManager, UserProfileRepository userProfileRepository,
        PlanRepository planRepository)
    {
        _userManager = userManager;
        _userProfileRepository = userProfileRepository;
        _planRepository = planRepository;
    }

    public async Task<Plan> ResolveAsync(ClaimsPrincipal principal, CancellationToken ct = default)
    {
        if (principal.Identity?.IsAuthenticated == true)
        {
            var user = await _userManager.GetUserAsync(principal);
            if (user is not null)
            {
                var profile = await _userProfileRepository.GetAsync(user.Id, ct);
                if (profile is not null)
                {
                    var plan = await _planRepository.GetByIdAsync(profile.PlanId, ct);
                    if (plan is not null)
                    {
                        return plan;
                    }
                }
            }
        }

        return await _planRepository.GetByCodeAsync("free", ct)
            ?? throw new InvalidOperationException("Тариф «free» не найден в базе.");
    }
}
