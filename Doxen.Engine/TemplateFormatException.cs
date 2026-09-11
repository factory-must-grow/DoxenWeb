namespace Doxen.Engine;

// Файл не открылся как .docx: повреждён, не является OOXML-пакетом или
// переименован из другого формата. Отдельный тип нужен, чтобы вызывающая
// сторона могла показать пользователю понятное сообщение вместо трассы
// стека необработанного исключения OpenXml.
public sealed class TemplateFormatException : Exception
{
    public TemplateFormatException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}
