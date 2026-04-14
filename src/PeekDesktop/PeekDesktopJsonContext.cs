using System.Text.Json.Serialization;

namespace PeekDesktop;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(Settings))]
[JsonSerializable(typeof(GitHubReleaseInfo))]
internal sealed partial class PeekDesktopJsonContext : JsonSerializerContext
{
}
