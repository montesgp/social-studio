using System.Text.Json;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Visual.Composers;

internal static class PayloadReader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static T? As<T>(object payload)
        where T : class
    {
        if (payload is T typed)
        {
            return typed;
        }

        if (payload is JsonElement element)
        {
            try
            {
                return element.Deserialize<T>(JsonOptions);
            }
            catch
            {
                return null;
            }
        }

        return null;
    }
}
