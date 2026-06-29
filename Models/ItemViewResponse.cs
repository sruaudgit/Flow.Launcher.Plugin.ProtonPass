using System.Text.Json.Serialization;

namespace Flow.Launcher.Plugin.ProtonPass.Models;

public record ItemViewResponse([property: JsonPropertyName("item")] ItemDetail Item);

public record ItemDetail([property: JsonPropertyName("content")] ItemContent Content);

public record ItemContent(
    [property: JsonPropertyName("content")] LoginContent Content,
    [property: JsonPropertyName("extra_fields")] List<ExtraField>? ExtraFields
);

public record LoginContent([property: JsonPropertyName("Login")] LoginFields Login);

public record LoginFields(
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("username")] string? Username,
    [property: JsonPropertyName("password")] string? Password,
    [property: JsonPropertyName("urls")] List<string>? Urls,
    [property: JsonPropertyName("totp_uri")] string? TotpUri
);

public record ExtraField(
    [property: JsonPropertyName("name")] string FieldName,
    [property: JsonPropertyName("content")] ExtraFieldContent? Content
);

public record ExtraFieldContent([property: JsonPropertyName("Text")] string? Text);
