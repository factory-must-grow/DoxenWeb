using System.ComponentModel.DataAnnotations;
using Doxen.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Doxen.Web.Pages.Account;

public class LoginModel : PageModel
{
    private readonly SignInManager<DoxenUser> _signInManager;

    public LoginModel(SignInManager<DoxenUser> signInManager)
    {
        _signInManager = signInManager;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public void OnGet(string? returnUrl = null)
    {
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await _signInManager.PasswordSignInAsync(
            Input.Email, Input.Password, Input.RememberMe, lockoutOnFailure: true);

        if (result.Succeeded)
        {
            return LocalRedirect(returnUrl ?? Url.Content("~/"));
        }

        if (result.IsLockedOut)
        {
            ModelState.AddModelError(string.Empty,
                "Слишком много неверных попыток входа. Попробуйте снова через 15 минут.");
            return Page();
        }

        ModelState.AddModelError(string.Empty, "Неверная почта или пароль.");
        return Page();
    }

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Укажите почту.")]
        [EmailAddress(ErrorMessage = "Проверьте адрес почты.")]
        [Display(Name = "Почта")]
        public string Email { get; set; } = "";

        [Required(ErrorMessage = "Укажите пароль.")]
        [DataType(DataType.Password)]
        [Display(Name = "Пароль")]
        public string Password { get; set; } = "";

        [Display(Name = "Запомнить меня")]
        public bool RememberMe { get; set; }
    }
}
