using Doxen.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Doxen.Web.Pages.Admin;

public class UsersModel : PageModel
{
    private const int PageSize = 50;

    private readonly AdminUserRepository _adminUserRepository;
    private readonly PlanRepository _planRepository;

    public UsersModel(AdminUserRepository adminUserRepository, PlanRepository planRepository)
    {
        _adminUserRepository = adminUserRepository;
        _planRepository = planRepository;
    }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    public IReadOnlyList<AdminUserRow> Rows { get; private set; } = Array.Empty<AdminUserRow>();
    public IReadOnlyList<Plan> Plans { get; private set; } = Array.Empty<Plan>();
    public int TotalCount { get; private set; }
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
    public string? StatusMessage { get; private set; }

    public async Task OnGetAsync()
    {
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostChangePlanAsync(long userId, int planId)
    {
        await _adminUserRepository.ChangePlanAsync(userId, planId);
        return RedirectToPage(new { Search, PageNumber });
    }

    private async Task LoadAsync()
    {
        if (PageNumber < 1)
        {
            PageNumber = 1;
        }

        var result = await _adminUserRepository.SearchAsync(Search, PageNumber, PageSize);
        Rows = result.Rows;
        TotalCount = result.TotalCount;
        Plans = await _planRepository.GetAllAsync();
    }
}
