namespace Doxen.Web.Models;

// Состояние экрана /generate, хранится в сессии (05-screens.md).
// Сам шаблон в сессию не копируется — только ключ записи в
// TemplateCache. SharedJson и ValuesJson каждого набора хранят
// целиком дерево значений (включая то, что не соответствует ни одной
// переменной текущего шаблона) — это и есть механизм «сохранить и
// записать обратно» из 04-answers-file.md.
public sealed class GenerateState
{
    public string? TemplateCacheKey { get; set; }
    public string? TemplateFileName { get; set; }
    public bool BatchMode { get; set; }
    public string SharedJson { get; set; } = "{}";
    public List<DatasetRowState> Datasets { get; set; } = new() { new DatasetRowState() };

    // Пути переменных, перенесённые пользователем (или определённые по
    // структуре загруженного файла) в «своё для каждого документа».
    public List<string> PerDocumentPaths { get; set; } = new();

    // Анонимный пользователь нажал «Собрать документ» — после
    // регистрации собрать сразу, не заставляя нажимать ещё раз.
    public bool PendingGenerateAfterRegister { get; set; }

    // Сообщение, которое нужно показать при следующем открытии
    // страницы. Ответ на «Собрать документ» — это сразу скачивание
    // файла, а не HTML-страница, поэтому предупреждение (например,
    // «пакет не по тарифу — собран только первый документ») показать
    // в тот же момент нельзя — откладываем на следующий заход.
    public string? PendingNotice { get; set; }
}

public sealed class DatasetRowState
{
    public string? Title { get; set; }
    public string ValuesJson { get; set; } = "{}";
}
