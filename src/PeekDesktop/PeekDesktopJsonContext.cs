using System.Text.Json.Serialization;

namespace PeekDesktop;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(Settings))]
internal sealed partial class PeekDesktopJsonContext : JsonSerializerContext
{
}
