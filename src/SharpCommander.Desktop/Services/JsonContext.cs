using System.Text.Json.Serialization;
using SharpCommander.Core.Models;

namespace SharpCommander.Desktop.Services;

/// <summary>
/// Source-generated JSON context for the persisted settings (reflection-free, AOT and trim safe).
/// Only <see cref="UserSettings"/> and the types it contains are persisted; <see cref="FileSystemEntry"/> is not.
/// </summary>
[JsonSerializable(typeof(UserSettings))]
[JsonSerializable(typeof(FavoriteItem))]
[JsonSerializable(typeof(NavigationHistoryItem))]
[JsonSerializable(typeof(List<FavoriteItem>))]
[JsonSerializable(typeof(List<NavigationHistoryItem>))]
[JsonSerializable(typeof(TabState))]
[JsonSerializable(typeof(List<TabState>))]
[JsonSerializable(typeof(SftpSite))]
[JsonSerializable(typeof(List<SftpSite>))]
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
public partial class AppJsonContext : JsonSerializerContext
{
}
