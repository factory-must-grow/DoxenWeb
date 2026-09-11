using Doxen.Data;

namespace Doxen.Web.Services;

public sealed class MonthlyLimitResult
{
    public bool Allowed { get; init; }
    public bool RequiresConfirmation { get; init; }
    public int AllowedDocuments { get; init; }
    public string? Message { get; init; }

    public static MonthlyLimitResult Ok(int allowedDocuments) =>
        new() { Allowed = true, AllowedDocuments = allowedDocuments };

    public static MonthlyLimitResult Blocked(string message) =>
        new() { Allowed = false, Message = message };

    public static MonthlyLimitResult NeedsConfirmation(int allowedDocuments, string message) =>
        new() { Allowed = false, RequiresConfirmation = true, AllowedDocuments = allowedDocuments, Message = message };
}

// Три проверки лимитов в порядке из 05-screens.md, раздел «Проверка
// лимитов». Формулировки объясняют, что случилось и что делать, а не
// просто отказывают (00-README.md, тон сообщений).
public sealed class LimitChecker
{
    private static readonly string[] MonthNamesGenitive =
    {
        "января", "февраля", "марта", "апреля", "мая", "июня",
        "июля", "августа", "сентября", "октября", "ноября", "декабря",
    };

    private readonly PlanRepository _planRepository;
    private readonly UsageRepository _usageRepository;

    public LimitChecker(PlanRepository planRepository, UsageRepository usageRepository)
    {
        _planRepository = planRepository;
        _usageRepository = usageRepository;
    }

    // 1. Размер файла — до разбора.
    public async Task<string?> CheckFileSizeAsync(long fileSizeBytes, Plan plan, CancellationToken ct = default)
    {
        var maxBytes = (long)plan.MaxFileSizeMb * 1024 * 1024;
        if (fileSizeBytes <= maxBytes)
        {
            return null;
        }

        var pro = await _planRepository.GetByCodeAsync("pro", ct);
        var proClause = pro is not null ? $" на «{pro.Name}» — {pro.MaxFileSizeMb} МБ." : "";
        return $"Файл больше {plan.MaxFileSizeMb} МБ. На тарифе «{plan.Name}» это предел;{proClause}";
    }

    // 2. Количество переменных в шаблоне — после разбора.
    public string? CheckVariableCount(int variableCount, Plan plan)
    {
        if (variableCount <= plan.MaxVariablesPerTemplate)
        {
            return null;
        }

        return $"В этом шаблоне {variableCount} переменных, а на тарифе «{plan.Name}» можно до " +
               $"{plan.MaxVariablesPerTemplate}. Обычно это значит, что в одном файле собрано много документов " +
               "сразу — разделите их или перейдите на «Pro».";
    }

    // 3. Документов за месяц — перед генерацией. При пакете считается
    // весь пакет целиком: не хватает — не собирать частично, а спросить.
    public async Task<MonthlyLimitResult> CheckMonthlyDocumentsAsync(long userId, int documentsNeeded, Plan plan,
        CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var usage = await _usageRepository.GetCurrentMonthAsync(userId, now.Year, now.Month, ct);
        var remaining = plan.MaxDocumentsPerMonth - usage.DocumentsGenerated;

        if (remaining <= 0)
        {
            var nextMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1);
            return MonthlyLimitResult.Blocked(
                $"В этом месяце вы собрали все {plan.MaxDocumentsPerMonth} документов тарифа «{plan.Name}». " +
                $"Лимит обновится 1 {MonthNamesGenitive[nextMonth.Month - 1]} или можно перейти на «Pro».");
        }

        if (documentsNeeded > remaining)
        {
            return MonthlyLimitResult.NeedsConfirmation(remaining,
                $"В пакете {documentsNeeded} документов, а до конца лимита осталось {remaining}.");
        }

        return MonthlyLimitResult.Ok(remaining);
    }
}
