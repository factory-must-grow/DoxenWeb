using System.Text;
using System.Text.Json;
using Doxen.Data;
using Doxen.Engine;
using Doxen.Engine.Models;
using Doxen.Web.Data;
using Doxen.Web.Models;
using Doxen.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Doxen.Web.Pages;

public class GenerateModel : PageModel
{
    private readonly TemplateCache _templateCache;
    private readonly GenerateSessionStore _sessionStore;
    private readonly CurrentPlanResolver _planResolver;
    private readonly LimitChecker _limitChecker;
    private readonly UsageRepository _usageRepository;
    private readonly GenerationLogRepository _generationLogRepository;
    private readonly UserManager<DoxenUser> _userManager;

    public GenerateModel(TemplateCache templateCache, GenerateSessionStore sessionStore,
        CurrentPlanResolver planResolver, LimitChecker limitChecker, UsageRepository usageRepository,
        GenerationLogRepository generationLogRepository, UserManager<DoxenUser> userManager)
    {
        _templateCache = templateCache;
        _sessionStore = sessionStore;
        _planResolver = planResolver;
        _limitChecker = limitChecker;
        _usageRepository = usageRepository;
        _generationLogRepository = generationLogRepository;
        _userManager = userManager;
    }

    [BindProperty]
    public IFormFile? TemplateUpload { get; set; }

    [BindProperty]
    public IFormFile? AnswersUpload { get; set; }

    [BindProperty]
    public bool ConfirmPartialBatch { get; set; }

    public GenerateState State { get; private set; } = new();
    public bool TemplateMissing { get; private set; }
    public string? ErrorMessage { get; private set; }
    public string? InfoMessage { get; private set; }
    public List<FieldGroupViewModel> SharedGroups { get; private set; } = new();
    public List<FieldViewModel> TableColumns { get; private set; } = new();
    public int ResolvedCount { get; private set; }
    public int TotalCount { get; private set; }
    public int? AwaitingPartialBatchCount { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        State = _sessionStore.Load(HttpContext.Session);

        if (State.PendingNotice is not null)
        {
            InfoMessage = State.PendingNotice;
            State.PendingNotice = null;
            _sessionStore.Save(HttpContext.Session, State);
        }

        if (State.PendingGenerateAfterRegister && User.Identity?.IsAuthenticated == true)
        {
            State.PendingGenerateAfterRegister = false;
            _sessionStore.Save(HttpContext.Session, State);

            var download = await TryGenerateAsync(State);
            if (download is not null)
            {
                return download;
            }
        }

        await BuildViewAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostUploadTemplateAsync()
    {
        State = _sessionStore.Load(HttpContext.Session);

        if (TemplateUpload is null || TemplateUpload.Length == 0)
        {
            ErrorMessage = "Выберите файл .docx.";
            await BuildViewAsync();
            return Page();
        }

        if (!TemplateUpload.FileName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
        {
            ErrorMessage = "Doxen понимает только файлы .docx. Если у вас .doc, пересохраните его в Word как .docx.";
            await BuildViewAsync();
            return Page();
        }

        var plan = await _planResolver.ResolveAsync(User);

        var sizeError = await _limitChecker.CheckFileSizeAsync(TemplateUpload.Length, plan);
        if (sizeError is not null)
        {
            ErrorMessage = sizeError;
            await BuildViewAsync();
            return Page();
        }

        byte[] bytes;
        using (var ms = new MemoryStream())
        {
            await TemplateUpload.CopyToAsync(ms);
            bytes = ms.ToArray();
        }

        try
        {
            using var stream = new MemoryStream(bytes);
            var variables = TemplateParser.Parse(stream);
            if (variables.Count == 0)
            {
                ErrorMessage = "В этом шаблоне нет ни одной переменной вида {{...}}. Проверьте, что вы загрузили нужный файл.";
                await BuildViewAsync();
                return Page();
            }

            var variableCountError = _limitChecker.CheckVariableCount(variables.Count, plan);
            if (variableCountError is not null)
            {
                ErrorMessage = variableCountError;
                await BuildViewAsync();
                return Page();
            }
        }
        catch (TemplateFormatException ex)
        {
            ErrorMessage = ex.Message;
            await BuildViewAsync();
            return Page();
        }

        var admit = await _templateCache.TryAdmitAsync(plan.Code != "free", HttpContext.RequestAborted);
        if (admit == TemplateAdmitResult.Rejected)
        {
            Response.Headers.RetryAfter = "60";
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            ErrorMessage = "Сервер сейчас загружен. Попробуйте через минуту — или перейдите на «Pro», там сборка идёт без очереди.";
            await BuildViewAsync();
            return Page();
        }

        State.TemplateCacheKey = _templateCache.Store(bytes, TemplateUpload.FileName);
        State.TemplateFileName = TemplateUpload.FileName;
        _sessionStore.Save(HttpContext.Session, State);

        await BuildViewAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostUploadAnswersAsync()
    {
        State = _sessionStore.Load(HttpContext.Session);

        if (AnswersUpload is null || AnswersUpload.Length == 0)
        {
            ErrorMessage = "Выберите файл ответов (.json).";
            await BuildViewAsync();
            return Page();
        }

        string json;
        using (var reader = new StreamReader(AnswersUpload.OpenReadStream(), Encoding.UTF8))
        {
            json = await reader.ReadToEndAsync();
        }

        var result = AnswersFile.Read(json);
        if (!result.Success)
        {
            ErrorMessage = result.Error;
            await BuildViewAsync();
            return Page();
        }

        var variables = await LoadVariablesAsync(State);
        MergeAnswersIntoState(State, result.Root, variables);

        if (result.IsNewerVersion)
        {
            InfoMessage = "Файл ответов создан более новой версией Doxen — часть значений может не примениться.";
        }

        _sessionStore.Save(HttpContext.Session, State);
        await BuildViewAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        State = _sessionStore.Load(HttpContext.Session);
        ApplyPostedValues(State);
        _sessionStore.Save(HttpContext.Session, State);
        await BuildViewAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAddRowAsync()
    {
        State = _sessionStore.Load(HttpContext.Session);
        ApplyPostedValues(State);
        State.Datasets.Add(new DatasetRowState());
        State.BatchMode = true;
        _sessionStore.Save(HttpContext.Session, State);
        await BuildViewAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostRemoveRowAsync(int index)
    {
        State = _sessionStore.Load(HttpContext.Session);
        ApplyPostedValues(State);
        if (index >= 0 && index < State.Datasets.Count && State.Datasets.Count > 1)
        {
            State.Datasets.RemoveAt(index);
        }
        _sessionStore.Save(HttpContext.Session, State);
        await BuildViewAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostMoveFieldAsync(string path, bool toPerDocument)
    {
        State = _sessionStore.Load(HttpContext.Session);
        ApplyPostedValues(State);

        if (toPerDocument)
        {
            if (!State.PerDocumentPaths.Contains(path))
            {
                State.PerDocumentPaths.Add(path);
            }
        }
        else
        {
            State.PerDocumentPaths.Remove(path);
        }

        _sessionStore.Save(HttpContext.Session, State);
        await BuildViewAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostGenerateAsync()
    {
        State = _sessionStore.Load(HttpContext.Session);
        ApplyPostedValues(State);
        _sessionStore.Save(HttpContext.Session, State);

        if (User.Identity?.IsAuthenticated != true)
        {
            // Кнопка «Собрать документ» ведёт на регистрацию с сохранением
            // введённого в сессии — после регистрации возвращаемся сюда и
            // сразу собираем документ, не заставляя вводить всё заново.
            State.PendingGenerateAfterRegister = true;
            _sessionStore.Save(HttpContext.Session, State);
            return RedirectToPage("/Account/Register", new { returnUrl = "/Generate" });
        }

        var download = await TryGenerateAsync(State);
        if (download is not null)
        {
            return download;
        }

        await BuildViewAsync();
        return Page();
    }

    public IActionResult OnPostDownloadAnswers()
    {
        State = _sessionStore.Load(HttpContext.Session);
        ApplyPostedValues(State);
        _sessionStore.Save(HttpContext.Session, State);

        var shared = JsonPathBuilder.ToElement(State.SharedJson);
        var datasets = State.Datasets
            .Select(d => ((string?)d.Title, JsonPathBuilder.ToElement(d.ValuesJson)))
            .ToList();

        var json = AnswersFile.Write(shared, datasets);
        var bytes = Encoding.UTF8.GetBytes(json);
        var baseName = string.IsNullOrEmpty(State.TemplateFileName)
            ? "doxen"
            : Path.GetFileNameWithoutExtension(State.TemplateFileName);

        return File(bytes, "application/json", $"{baseName}.answers.json");
    }

    // Собирает документ (или пакет) и возвращает файл. Возвращает null,
    // если сборка не удалась — тогда вызывающий код перерисовывает
    // страницу с ErrorMessage.
    private async Task<IActionResult?> TryGenerateAsync(GenerateState state)
    {
        var uploaded = state.TemplateCacheKey is null ? null : _templateCache.Get(state.TemplateCacheKey);
        if (uploaded is null)
        {
            ErrorMessage = "Загрузка шаблона устарела. Выберите файл ещё раз.";
            TemplateMissing = true;
            return null;
        }

        var plan = await _planResolver.ResolveAsync(User);
        var isBatch = state.Datasets.Count > 1;

        if (isBatch && !plan.AllowBatch)
        {
            // Ответ на «Собрать документ» — сразу файл, а не HTML, поэтому
            // предупреждение показать в этот же момент нельзя: откладываем
            // на следующее открытие страницы (см. PendingNotice).
            state.PendingNotice = $"В файле ответов {state.Datasets.Count} наборов данных. Сборка нескольких " +
                                   $"документов за раз доступна на тарифе «Pro»; на «{plan.Name}» был собран только первый.";
            _sessionStore.Save(HttpContext.Session, state);
            isBatch = false;
        }

        var datasetsToGenerate = state.Datasets;
        var documentsNeeded = isBatch ? state.Datasets.Count : 1;

        var userId = _userManager.GetUserId(User);
        if (userId is null || !long.TryParse(userId, out var userIdLong))
        {
            ErrorMessage = "Не удалось определить учётную запись. Войдите ещё раз.";
            return null;
        }

        // 3. Документов за месяц — перед генерацией. При пакете считается
        // весь пакет целиком: не хватает — не собирать частично, а спросить
        // (05-screens.md).
        var monthly = await _limitChecker.CheckMonthlyDocumentsAsync(userIdLong, documentsNeeded, plan);
        if (!monthly.Allowed)
        {
            if (monthly.RequiresConfirmation && ConfirmPartialBatch)
            {
                datasetsToGenerate = state.Datasets.Take(monthly.AllowedDocuments).ToList();
                documentsNeeded = datasetsToGenerate.Count;
            }
            else
            {
                ErrorMessage = monthly.Message;
                AwaitingPartialBatchCount = monthly.RequiresConfirmation ? monthly.AllowedDocuments : null;
                return null;
            }
        }

        var baseName = string.IsNullOrEmpty(state.TemplateFileName)
            ? "document"
            : Path.GetFileNameWithoutExtension(state.TemplateFileName);

        var variablesCount = CountVariables(uploaded.Bytes);

        if (!isBatch)
        {
            var merged = MergeSharedAndFirstDataset(state);

            GenerationResult result;
            try
            {
                using var templateStream = new MemoryStream(uploaded.Bytes, writable: false);
                result = TemplateGenerator.Generate(templateStream, merged);
            }
            catch (Exception)
            {
                await _generationLogRepository.InsertAsync(userIdLong, uploaded.Bytes.Length, 0, variablesCount, 0,
                    succeeded: false, "Ошибка при сборке документа.");
                ErrorMessage = "Не удалось собрать документ. Попробуйте ещё раз.";
                return null;
            }

            await _usageRepository.IncrementAsync(userIdLong, DateTimeOffset.UtcNow.Year, DateTimeOffset.UtcNow.Month,
                1, result.SubstitutionCount);
            await _generationLogRepository.InsertAsync(userIdLong, uploaded.Bytes.Length, 1, variablesCount,
                result.SubstitutionCount, succeeded: true, errorMessage: null);

            return File(result.Document,
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                $"{baseName}.docx");
        }

        var answersRoot = BuildAnswersRoot(state, datasetsToGenerate);
        using var batchTemplateStream = new MemoryStream(uploaded.Bytes, writable: false);
        var batchResults = TemplateGenerator.GenerateBatch(batchTemplateStream, answersRoot);

        var succeededResults = batchResults.Where(r => r.Document is not null).ToList();
        var substitutionsTotal = succeededResults.Sum(r => (long)r.SubstitutionCount);

        if (succeededResults.Count > 0)
        {
            await _usageRepository.IncrementAsync(userIdLong, DateTimeOffset.UtcNow.Year, DateTimeOffset.UtcNow.Month,
                succeededResults.Count, substitutionsTotal);
        }

        var failedCount = batchResults.Count - succeededResults.Count;
        await _generationLogRepository.InsertAsync(userIdLong, uploaded.Bytes.Length, succeededResults.Count,
            variablesCount, substitutionsTotal,
            succeeded: succeededResults.Count > 0,
            errorMessage: failedCount > 0 ? $"Не удалось собрать {failedCount} из {batchResults.Count} документов." : null);

        using var zipStream = new MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(zipStream, System.IO.Compression.ZipArchiveMode.Create, true))
        {
            foreach (var item in succeededResults)
            {
                var entry = archive.CreateEntry(SanitizeFileName(item.Title) + ".docx");
                await using var entryStream = entry.Open();
                await entryStream.WriteAsync(item.Document);
            }
        }

        zipStream.Position = 0;
        return File(zipStream.ToArray(), "application/zip", $"{baseName}.zip");
    }

    private static int CountVariables(byte[] templateBytes)
    {
        try
        {
            using var stream = new MemoryStream(templateBytes, writable: false);
            return TemplateParser.Parse(stream).Count;
        }
        catch (TemplateFormatException)
        {
            return 0;
        }
    }

    private static string SanitizeFileName(string title)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(title.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return string.IsNullOrEmpty(cleaned) ? "Документ" : cleaned;
    }

    private static JsonElement MergeSharedAndFirstDataset(GenerateState state)
    {
        var shared = JsonPathBuilder.ToElement(state.SharedJson);
        var values = state.Datasets.Count > 0
            ? JsonPathBuilder.ToElement(state.Datasets[0].ValuesJson)
            : JsonPathBuilder.ToElement("{}");

        // Простое неглубокое слияние для одиночного документа — то же
        // самое, что делает GenerateBatch, но без пакета.
        var root = System.Text.Json.Nodes.JsonNode.Parse(shared.GetRawText()) as System.Text.Json.Nodes.JsonObject
                   ?? new System.Text.Json.Nodes.JsonObject();
        var overlay = System.Text.Json.Nodes.JsonNode.Parse(values.GetRawText()) as System.Text.Json.Nodes.JsonObject;

        if (overlay is not null)
        {
            MergeInto(root, overlay);
        }

        return JsonDocument.Parse(root.ToJsonString()).RootElement;
    }

    private static void MergeInto(System.Text.Json.Nodes.JsonObject target, System.Text.Json.Nodes.JsonObject overlay)
    {
        foreach (var property in overlay)
        {
            if (property.Value is System.Text.Json.Nodes.JsonObject overlayChild &&
                target[property.Key] is System.Text.Json.Nodes.JsonObject targetChild)
            {
                MergeInto(targetChild, overlayChild);
            }
            else
            {
                target[property.Key] = property.Value?.DeepClone();
            }
        }
    }

    private static JsonElement BuildAnswersRoot(GenerateState state, IReadOnlyList<DatasetRowState> datasetsToInclude)
    {
        var root = new System.Text.Json.Nodes.JsonObject
        {
            ["shared"] = System.Text.Json.Nodes.JsonNode.Parse(state.SharedJson),
        };

        var datasets = new System.Text.Json.Nodes.JsonArray();
        foreach (var row in datasetsToInclude)
        {
            var item = new System.Text.Json.Nodes.JsonObject
            {
                ["values"] = System.Text.Json.Nodes.JsonNode.Parse(row.ValuesJson),
            };
            if (!string.IsNullOrWhiteSpace(row.Title))
            {
                item["title"] = row.Title;
            }

            datasets.Add(item);
        }

        root["datasets"] = datasets;
        return JsonDocument.Parse(root.ToJsonString()).RootElement;
    }

    private void ApplyPostedValues(GenerateState state)
    {
        var form = Request.HasFormContentType ? Request.Form : null;
        if (form is null)
        {
            return;
        }

        foreach (var key in form.Keys)
        {
            var value = form[key].ToString();

            if (key.StartsWith("shared|", StringComparison.Ordinal))
            {
                var path = key["shared|".Length..];
                state.SharedJson = JsonPathBuilder.SetPath(state.SharedJson, path, value);
            }
            else if (key.StartsWith("ds|", StringComparison.Ordinal))
            {
                var rest = key["ds|".Length..];
                var sep = rest.IndexOf('|');
                if (sep <= 0)
                {
                    continue;
                }

                if (!int.TryParse(rest[..sep], out var rowIndex) || rowIndex < 0 || rowIndex >= state.Datasets.Count)
                {
                    continue;
                }

                var path = rest[(sep + 1)..];
                state.Datasets[rowIndex].ValuesJson = JsonPathBuilder.SetPath(state.Datasets[rowIndex].ValuesJson, path, value);
            }
            else if (key.StartsWith("dstitle|", StringComparison.Ordinal))
            {
                if (int.TryParse(key["dstitle|".Length..], out var rowIndex) &&
                    rowIndex >= 0 && rowIndex < state.Datasets.Count)
                {
                    state.Datasets[rowIndex].Title = string.IsNullOrWhiteSpace(value) ? null : value;
                }
            }
        }

        // «Без даты» — приоритет над обычным значением, стирает путь.
        foreach (var key in form.Keys)
        {
            if (!key.StartsWith("nodate|", StringComparison.Ordinal))
            {
                continue;
            }

            var path = key["nodate|".Length..];
            state.SharedJson = JsonPathBuilder.RemovePath(state.SharedJson, path);
            for (var i = 0; i < state.Datasets.Count; i++)
            {
                state.Datasets[i].ValuesJson = JsonPathBuilder.RemovePath(state.Datasets[i].ValuesJson, path);
            }
        }
    }

    private async Task<IReadOnlyList<TemplateVariable>?> LoadVariablesAsync(GenerateState state)
    {
        if (state.TemplateCacheKey is null)
        {
            return null;
        }

        var uploaded = _templateCache.Get(state.TemplateCacheKey);
        if (uploaded is null)
        {
            return null;
        }

        using var stream = new MemoryStream(uploaded.Bytes, writable: false);
        return await Task.Run(() => TemplateParser.Parse(stream));
    }

    private static void MergeAnswersIntoState(GenerateState state, JsonElement canonicalRoot,
        IReadOnlyList<TemplateVariable>? variables)
    {
        var shared = canonicalRoot.GetProperty("shared");
        state.SharedJson = shared.GetRawText();

        var datasetsEl = canonicalRoot.GetProperty("datasets");
        var datasets = new List<DatasetRowState>();
        foreach (var item in datasetsEl.EnumerateArray())
        {
            var title = item.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
            var values = item.TryGetProperty("values", out var v) ? v : item;
            datasets.Add(new DatasetRowState { Title = title, ValuesJson = values.GetRawText() });
        }

        state.Datasets = datasets.Count > 0 ? datasets : new List<DatasetRowState> { new() };
        state.BatchMode = state.Datasets.Count > 1;

        state.PerDocumentPaths.Clear();
        if (variables is not null)
        {
            foreach (var variable in variables)
            {
                var inShared = ValueResolver.Resolve(shared, variable.Path).Status != ResolveStatus.MissingValue;
                if (inShared)
                {
                    continue;
                }

                var inAnyDataset = state.Datasets.Any(d =>
                    ValueResolver.Resolve(JsonPathBuilder.ToElement(d.ValuesJson), variable.Path).Status != ResolveStatus.MissingValue);

                if (inAnyDataset)
                {
                    state.PerDocumentPaths.Add(variable.Path);
                }
            }
        }
    }

    private async Task BuildViewAsync()
    {
        SharedGroups = new List<FieldGroupViewModel>();
        TableColumns = new List<FieldViewModel>();
        ResolvedCount = 0;
        TotalCount = 0;

        if (State.TemplateCacheKey is null)
        {
            return;
        }

        var uploaded = _templateCache.Get(State.TemplateCacheKey);
        if (uploaded is null)
        {
            TemplateMissing = true;
            ErrorMessage ??= "Загрузка шаблона устарела. Выберите файл ещё раз.";
            return;
        }

        IReadOnlyList<TemplateVariable> variables;
        try
        {
            using var stream = new MemoryStream(uploaded.Bytes, writable: false);
            variables = TemplateParser.Parse(stream);
        }
        catch (TemplateFormatException ex)
        {
            ErrorMessage = ex.Message;
            return;
        }

        var sharedElement = JsonPathBuilder.ToElement(State.SharedJson);
        var firstRowElement = State.Datasets.Count > 0
            ? JsonPathBuilder.ToElement(State.Datasets[0].ValuesJson)
            : JsonPathBuilder.ToElement("{}");

        var groups = new Dictionary<string, FieldGroupViewModel>();

        foreach (var variable in variables)
        {
            var isPerDocument = State.PerDocumentPaths.Contains(variable.Path);
            var kind = FieldKindDetector.Detect(variable);

            if (isPerDocument && State.BatchMode)
            {
                // В пакете переменная считается один раз на каждый документ.
                TotalCount += variable.OccurrenceCount * Math.Max(1, State.Datasets.Count);
                var column = BuildField(variable, kind, isPerDocument: true, JsonPathBuilder.ToElement("{}"));
                TableColumns.Add(column);
                continue;
            }

            TotalCount += variable.OccurrenceCount;

            var sourceElement = isPerDocument ? firstRowElement : sharedElement;
            var field = BuildField(variable, kind, isPerDocument, sourceElement);

            if (IsFieldResolved(field))
            {
                ResolvedCount += variable.OccurrenceCount;
            }

            var segment = variable.Path.Split('.')[0];
            if (!groups.TryGetValue(segment, out var group))
            {
                group = new FieldGroupViewModel
                {
                    Key = segment,
                    Title = ResolveGroupTitle(segment, sharedElement),
                    Fields = new List<FieldViewModel>(),
                };
                groups[segment] = group;
                SharedGroups.Add(group);
            }

            group.Fields.Add(field);
        }

        if (State.BatchMode)
        {
            foreach (var row in State.Datasets)
            {
                var rowElement = JsonPathBuilder.ToElement(row.ValuesJson);
                foreach (var column in TableColumns)
                {
                    var value = ValueResolver.Resolve(rowElement, column.Variable.Path);
                    if (value.Status == ResolveStatus.Resolved)
                    {
                        ResolvedCount += column.Variable.OccurrenceCount;
                    }
                }
            }
        }

        await Task.CompletedTask;
    }

    private static bool IsFieldResolved(FieldViewModel field) => field.Kind switch
    {
        FieldKind.Person => !string.IsNullOrEmpty(field.FirstName) || !string.IsNullOrEmpty(field.MiddleName) ||
                             !string.IsNullOrEmpty(field.LastName),
        _ => !string.IsNullOrEmpty(field.Value),
    };

    private static FieldViewModel BuildField(TemplateVariable variable, FieldKind kind, bool isPerDocument,
        JsonElement source)
    {
        if (kind == FieldKind.Person)
        {
            return new FieldViewModel
            {
                Variable = variable,
                Kind = kind,
                IsPerDocument = isPerDocument,
                FirstName = ReadString(source, variable.Path + ".firstName"),
                MiddleName = ReadString(source, variable.Path + ".middleName"),
                LastName = ReadString(source, variable.Path + ".lastName"),
            };
        }

        return new FieldViewModel
        {
            Variable = variable,
            Kind = kind,
            IsPerDocument = isPerDocument,
            Value = ReadString(source, variable.Path),
        };
    }

    private static string? ReadString(JsonElement source, string path)
    {
        var result = ValueResolver.Resolve(source, path);
        return result.Status == ResolveStatus.Resolved ? result.Value : null;
    }

    private static string ResolveGroupTitle(string segment, JsonElement shared)
    {
        var titleResult = ValueResolver.Resolve(shared, segment + ".title");
        return titleResult.Status == ResolveStatus.Resolved ? titleResult.Value! : segment;
    }
}
