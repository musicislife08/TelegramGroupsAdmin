using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace TelegramGroupsAdmin.BackgroundJobs.Services.Backup;

/// <summary>
/// Custom JSON type info resolver that excludes properties marked with [NotMapped]
/// This prevents serialization errors from computed properties like TokenType
/// </summary>
internal class NotMappedPropertiesIgnoringResolver : DefaultJsonTypeInfoResolver
{
    public override JsonTypeInfo GetTypeInfo(Type type, JsonSerializerOptions options)
    {
        var jsonTypeInfo = base.GetTypeInfo(type, options);

        if (jsonTypeInfo.Kind == JsonTypeInfoKind.Object)
        {
            // Remove properties that have [NotMapped] attribute
            var propertiesToRemove = new List<JsonPropertyInfo>();

            foreach (var property in jsonTypeInfo.Properties)
            {
                var propertyInfo = type.GetProperty(property.Name);
                if (propertyInfo?.GetCustomAttribute<NotMappedAttribute>() != null)
                {
                    propertiesToRemove.Add(property);
                }
            }

            foreach (var property in propertiesToRemove)
            {
                ((IList<JsonPropertyInfo>)jsonTypeInfo.Properties).Remove(property);
            }
        }

        return jsonTypeInfo;
    }
}
