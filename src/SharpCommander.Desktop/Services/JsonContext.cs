using System.Text.Json.Serialization;
using SharpCommander.Core.Models;

namespace SharpCommander.Desktop.Services;

/// <summary>
/// JSON serialization context for AOT compatibility.
/// </summary>
[JsonSerializable(typeof(UserSettings))]
[JsonSerializable(typeof(FavoriteItem))]
[JsonSerializable(typeof(NavigationHistoryItem))]
[JsonSerializable(typeof(List<FavoriteItem>))]
[JsonSerializable(typeof(List<NavigationHistoryItem>))]
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
public partial class AppJsonContext : JsonSerializerContext
{
}
