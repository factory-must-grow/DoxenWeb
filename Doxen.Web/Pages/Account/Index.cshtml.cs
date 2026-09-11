using Doxen.Data;
using Doxen.Web.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Doxen.Web.Pages.Account;

[Authorize]
public class IndexModel : PageModel
{
    private readonly UserManager<DoxenUser> _userManager;
    private readonly UserProfileRepository _userProfileRepository;

    public IndexModel(UserManager<DoxenUser> userManager, UserProfileRepository userProfileRepository)
    {
        _userManager = userManager;
        _userProfileRepository = userProfileRepository;
    }

    public string Email { get; set; } = "";
    public string? PlanName { get; set; }
    public DateTimeOffset? MemberSince { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return NotFound();
        }

        Email = user.Email ?? "";

        var profile = await _userProfileRepository.GetAsync(user.Id);
        if (profile is not null)
        {
            PlanName = profile.PlanName;
            MemberSince = profile.CreatedAt;
        }

        return Page();
    }
}
