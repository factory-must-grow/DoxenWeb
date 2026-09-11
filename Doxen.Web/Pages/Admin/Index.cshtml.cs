using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Doxen.Web.Pages.Admin;

// Доступ ограничен политикой AdminOnly на уровне папки /Admin
// (см. AddRazorPages в Program.cs), а не атрибутом здесь.
public class IndexModel : PageModel
{
    public void OnGet()
    {
    }
}
