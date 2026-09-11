using System.Text.Json;
using Doxen.Web.Models;

namespace Doxen.Web.Services;

// Сериализация GenerateState в сессию. Сессия хранит только строки —
// состояние экрана заполнения кладём одним JSON-блобом под одним ключом.
public sealed class GenerateSessionStore
{
    private const string Key = "Doxen.Generate.State";

    public GenerateState Load(ISession session)
    {
        var json = session.GetString(Key);
        if (string.IsNullOrEmpty(json))
        {
            return new GenerateState();
        }

        try
        {
            return JsonSerializer.Deserialize<GenerateState>(json) ?? new GenerateState();
        }
        catch (JsonException)
        {
            return new GenerateState();
        }
    }

    public void Save(ISession session, GenerateState state) =>
        session.SetString(Key, JsonSerializer.Serialize(state));

    public void Clear(ISession session) => session.Remove(Key);
}
