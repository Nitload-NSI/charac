using System.Text.Json.Serialization;

namespace Charac.Client;

internal sealed record CreateSessionRequest(int Columns, int Rows);
internal sealed record PasswordSessionRequest(string Password, int Columns, int Rows);
internal sealed record ResizeFrame(string Type, int Columns, int Rows);
internal sealed record InputFrame(string Type, string Data);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(ResourceList))]
[JsonSerializable(typeof(CreateSessionRequest))]
[JsonSerializable(typeof(PasswordSessionRequest))]
[JsonSerializable(typeof(ResizeFrame))]
[JsonSerializable(typeof(InputFrame))]
[JsonSerializable(typeof(OidcSessionFile))]
[JsonSerializable(typeof(ServerCertificateTrustFile))]
internal partial class ClientJsonContext : JsonSerializerContext
{
}
