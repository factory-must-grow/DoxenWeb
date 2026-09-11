using Doxen.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Doxen.Web.Pages.Account;

// Выход — только POST (05-screens.md), поэтому GET-обработчика нет.
public class LogoutModel : PageModel
{
    private readonly SignInManager<DoxenUser> _signInManager;

    public LogoutModel(SignInManager<DoxenUser> signInManager)
    {
        _signInManager = signInManager;
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        await _signInManager.SignOutAsync();
        return LocalRedirect(returnUrl ?? Url.Content("~/"));
    }
}
