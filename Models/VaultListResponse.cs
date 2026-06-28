using System.Text.Json.Serialization;

namespace Flow.Launcher.Plugin.ProtonPass.Models;

public record VaultListResponse([property: JsonPropertyName("vaults")] List<VaultInfo> Vaults);

public record VaultInfo(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("vault_id")] string VaultId,
    [property: JsonPropertyName("share_id")] string ShareId
);