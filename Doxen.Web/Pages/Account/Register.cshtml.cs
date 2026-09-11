using System.ComponentModel.DataAnnotations;
using Doxen.Data;
using Doxen.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Doxen.Web.Pages.Account;

public class RegisterModel : PageModel
{
    private readonly UserManager<DoxenUser> _userManager;
    private readonly SignInManager<DoxenUser> _signInManager;
    private readonly PlanRepository _planRepository;
    private readonly UserProfileRepository _userProfileRepository;

    public RegisterModel(UserManager<DoxenUser> userManager, SignInManager<DoxenUser> signInManager,
        PlanRepository planRepository, UserProfileRepository userProfileRepository)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _planRepository = planRepository;
        _userProfileRepository = userProfileRepository;
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

        var user = new DoxenUser { UserName = Input.Email, Email = Input.Email };
        var createResult = await _userManager.CreateAsync(user, Input.Password);

        if (!createResult.Succeeded)
        {
            foreach (var error in createResult.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return Page();
        }

        try
        {
            var freePlanId = await _planRepository.GetIdByCodeAsync("free")
                ?? throw new InvalidOperationException("Тариф «free» не найден в базе.");
            await _userProfileRepository.CreateAsync(user.Id, freePlanId);
        }
        catch
        {
            // Не удалось создать профиль — откатываем учётную запись,
            // иначе появится пользователь Identity без строки
            // user_profiles, и все запросы к тарифу по нему будут падать.
            await _userManager.DeleteAsync(user);
            ModelState.AddModelError(string.Empty, "Не удалось завершить регистрацию. Попробуйте ещё раз.");
            return Page();
        }

        await _signInManager.SignInAsync(user, isPersistent: false);

        return LocalRedirect(returnUrl ?? Url.Content("~/"));
    }

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Укажите почту.")]
        [EmailAddress(ErrorMessage = "Проверьте адрес почты.")]
        [Display(Name = "Почта")]
        public string Email { get; set; } = "";

        [Required(ErrorMessage = "Укажите пароль.")]
        [StringLength(100, MinimumLength = 10, ErrorMessage = "Пароль — минимум 10 символов.")]
        [DataType(DataType.Password)]
        [Display(Name = "Пароль")]
        public string Password { get; set; } = "";

        [DataType(DataType.Password)]
        [Display(Name = "Повторите пароль")]
        [Compare("Password", ErrorMessage = "Пароли не совпадают.")]
        public string ConfirmPassword { get; set; } = "";
    }
}
