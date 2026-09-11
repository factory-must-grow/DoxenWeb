using Doxen.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Npgsql;

namespace Doxen.Web.Pages.Admin;

public class PlansModel : PageModel
{
    private readonly PlanRepository _planRepository;

    public PlansModel(PlanRepository planRepository)
    {
        _planRepository = planRepository;
    }

    [BindProperty]
    public NewPlanInput NewPlan { get; set; } = new();

    public IReadOnlyList<Plan> Plans { get; private set; } = Array.Empty<Plan>();
    public string? ErrorMessage { get; private set; }

    public async Task OnGetAsync()
    {
        Plans = await _planRepository.GetAllAsync();
    }

    public async Task<IActionResult> OnPostUpdateAsync(int id, string name, int priceRub,
        int maxDocumentsPerMonth, int maxVariablesPerTemplate, int maxFileSizeMb, bool allowBatch, bool isActive)
    {
        await _planRepository.UpdateAsync(id, name, priceRub, maxDocumentsPerMonth, maxVariablesPerTemplate,
            maxFileSizeMb, allowBatch, isActive);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostCreateAsync()
    {
        if (string.IsNullOrWhiteSpace(NewPlan.Code) || string.IsNullOrWhiteSpace(NewPlan.Name))
        {
            ErrorMessage = "Укажите код и название тарифа.";
            Plans = await _planRepository.GetAllAsync();
            return Page();
        }

        try
        {
            await _planRepository.CreateAsync(NewPlan.Code.Trim(), NewPlan.Name.Trim(), NewPlan.PriceRub,
                NewPlan.MaxDocumentsPerMonth, NewPlan.MaxVariablesPerTemplate, NewPlan.MaxFileSizeMb,
                NewPlan.AllowBatch, NewPlan.IsActive);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            ErrorMessage = $"Тариф с кодом «{NewPlan.Code}» уже существует.";
            Plans = await _planRepository.GetAllAsync();
            return Page();
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var result = await _planRepository.DeleteAsync(id);
        if (!result.Success)
        {
            ErrorMessage = $"Тариф использует {result.AttachedUsersCount} {PluralizeUsers(result.AttachedUsersCount)}. " +
                            "Сначала переведите их на другой тариф.";
            Plans = await _planRepository.GetAllAsync();
            return Page();
        }

        return RedirectToPage();
    }

    private static string PluralizeUsers(int count)
    {
        var mod100 = count % 100;
        if (mod100 is >= 11 and <= 14)
        {
            return "пользователей";
        }

        return (count % 10) switch
        {
            1 => "пользователь",
            2 or 3 or 4 => "пользователя",
            _ => "пользователей",
        };
    }

    public sealed class NewPlanInput
    {
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public int PriceRub { get; set; }
        public int MaxDocumentsPerMonth { get; set; }
        public int MaxVariablesPerTemplate { get; set; }
        public int MaxFileSizeMb { get; set; }
        public bool AllowBatch { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
