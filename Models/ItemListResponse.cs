using System.Text.Json.Serialization;

namespace Flow.Launcher.Plugin.ProtonPass.Models;

public record ItemListResponse([property: JsonPropertyName("Items")] List<ItemEntry> Items);

public record ItemEntry(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("share_id")] string ShareId,
    [property: JsonPropertyName("vault_id")] string VaultId,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("item_type")] string ItemType
);
