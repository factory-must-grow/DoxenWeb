using System.ComponentModel.DataAnnotations;
using Doxen.Web.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Doxen.Web.Pages.Account;

[Authorize]
public class ChangePasswordModel : PageModel
{
    private readonly UserManager<DoxenUser> _userManager;
    private readonly SignInManager<DoxenUser> _signInManager;

    public ChangePasswordModel(UserManager<DoxenUser> userManager, SignInManager<DoxenUser> signInManager)
    {
        _userManager = userManager;
        _signInManager = signInManager;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return NotFound();
        }

        var result = await _userManager.ChangePasswordAsync(user, Input.CurrentPassword, Input.NewPassword);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return Page();
        }

        // Обновляем cookie текущей сессии сразу. Остальные сессии
        // перестанут работать благодаря ValidationInterval у
        // SecurityStampValidator, а не немедленно.
        await _signInManager.RefreshSignInAsync(user);

        StatusMessage = "Пароль изменён.";
        return RedirectToPage();
    }

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Укажите текущий пароль.")]
        [DataType(DataType.Password)]
        [Display(Name = "Текущий пароль")]
        public string CurrentPassword { get; set; } = "";

        [Required(ErrorMessage = "Укажите новый пароль.")]
        [StringLength(100, MinimumLength = 10, ErrorMessage = "Пароль — минимум 10 символов.")]
        [DataType(DataType.Password)]
        [Display(Name = "Новый пароль")]
        public string NewPassword { get; set; } = "";

        [DataType(DataType.Password)]
        [Display(Name = "Повторите новый пароль")]
        [Compare("NewPassword", ErrorMessage = "Пароли не совпадают.")]
        public string ConfirmPassword { get; set; } = "";
    }
}
