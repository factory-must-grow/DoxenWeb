using Doxen.Engine;
using Doxen.Engine.Models;
using Doxen.Web.Models;
using Doxen.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Doxen.Web.Pages;

public class IndexModel : PageModel
{
    private readonly TemplateCache _templateCache;
    private readonly GenerateSessionStore _sessionStore;
    private readonly AnonymousRateLimiter _rateLimiter;
    private readonly CurrentPlanResolver _planResolver;
    private readonly LimitChecker _limitChecker;

    public IndexModel(TemplateCache templateCache, GenerateSessionStore sessionStore,
        AnonymousRateLimiter rateLimiter, CurrentPlanResolver planResolver, LimitChecker limitChecker)
    {
        _templateCache = templateCache;
        _sessionStore = sessionStore;
        _rateLimiter = rateLimiter;
        _planResolver = planResolver;
        _limitChecker = limitChecker;
    }

    [BindProperty]
    public IFormFile? Template { get; set; }

    public IReadOnlyList<TemplateVariable>? FoundVariables { get; private set; }
    public string? UploadError { get; private set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var isAnonymous = User.Identity?.IsAuthenticated != true;

        if (isAnonymous)
        {
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            if (!_rateLimiter.TryRegister(ip))
            {
                UploadError = "Слишком много запросов с вашего адреса. Попробуйте ещё раз через минуту.";
                return Page();
            }
        }

        if (Template is null || Template.Length == 0)
        {
            UploadError = "Выберите файл .docx.";
            return Page();
        }

        if (!Template.FileName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
        {
            UploadError = "Doxen понимает только файлы .docx. Если у вас .doc, пересохраните его в Word как .docx.";
            return Page();
        }

        var plan = await _planResolver.ResolveAsync(User);

        var sizeError = await _limitChecker.CheckFileSizeAsync(Template.Length, plan);
        if (sizeError is not null)
        {
            UploadError = sizeError;
            return Page();
        }

        var admit = await _templateCache.TryAdmitAsync(plan.Code != "free", HttpContext.RequestAborted);
        if (admit == TemplateAdmitResult.Rejected)
        {
            Response.Headers.RetryAfter = "60";
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            UploadError = "Сервер сейчас загружен. Попробуйте через минуту — или перейдите на «Pro», там сборка идёт без очереди.";
            return Page();
        }

        byte[] bytes;
        using (var ms = new MemoryStream())
        {
            await Template.CopyToAsync(ms);
            bytes = ms.ToArray();
        }

        IReadOnlyList<TemplateVariable> variables;
        try
        {
            using var stream = new MemoryStream(bytes);
            variables = TemplateParser.Parse(stream);
        }
        catch (TemplateFormatException ex)
        {
            UploadError = ex.Message;
            return Page();
        }

        if (variables.Count == 0)
        {
            UploadError = "В этом шаблоне нет ни одной переменной вида {{...}}. Проверьте, что вы загрузили нужный файл.";
            return Page();
        }

        var variableCountError = _limitChecker.CheckVariableCount(variables.Count, plan);
        if (variableCountError is not null)
        {
            UploadError = variableCountError;
            return Page();
        }

        var cacheKey = _templateCache.Store(bytes, Template.FileName);

        _sessionStore.Save(HttpContext.Session, new GenerateState
        {
            TemplateCacheKey = cacheKey,
            TemplateFileName = Template.FileName,
        });

        FoundVariables = variables;
        return Page();
    }
}
