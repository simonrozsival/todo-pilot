using System.Text.Json.Serialization;

namespace TodoPilot;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(SessionRegistryEntry))]
[JsonSerializable(typeof(ViewerAttachmentRegistryEntry))]
[JsonSerializable(typeof(ExtensionManifest))]
public sealed partial class AppJsonContext : JsonSerializerContext;
