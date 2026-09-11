using System.Text.Json;
using System.Text.Json.Nodes;

namespace Doxen.Web.Services;

// Запись значения по пути внутрь дерева JSON, хранящегося в сессии как
// строка — обратная операция к ValueResolver.Resolve. Нужна, чтобы
// собирать shared/values набора из введённых в форме значений, не
// теряя при этом то, что туда положил загруженный файл ответов.
internal static class JsonPathBuilder
{
    public static string SetPath(string json, string path, string value)
    {
        var root = ParseObject(json);
        var segments = path.Split('.');
        var current = root;

        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (current[segments[i]] is not JsonObject child)
            {
                child = new JsonObject();
                current[segments[i]] = child;
            }

            current = child;
        }

        current[segments[^1]] = JsonValue.Create(value);
        return root.ToJsonString();
    }

    public static string RemovePath(string json, string path)
    {
        var root = ParseObject(json);
        var segments = path.Split('.');
        var current = root;

        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (current[segments[i]] is not JsonObject child)
            {
                return root.ToJsonString();
            }

            current = child;
        }

        current.Remove(segments[^1]);
        return root.ToJsonString();
    }

    public static JsonElement ToElement(string json) =>
        JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json).RootElement;

    private static JsonObject ParseObject(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new JsonObject();
        }

        return JsonNode.Parse(json) as JsonObject ?? new JsonObject();
    }
}
