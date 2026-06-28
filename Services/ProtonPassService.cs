using System.Diagnostics;
using System.Text.Json;
using Flow.Launcher.Plugin.ProtonPass.Models;

namespace Flow.Launcher.Plugin.ProtonPass.Services;

public class ProtonPassService
{
    private readonly PluginInitContext _context;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ProtonPassService(PluginInitContext context)
    {
        _context = context;
    }

    public bool IsAuthenticated()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "pass-cli",
            Arguments = "test",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.EnvironmentVariables["RUST_LOG"] = "error";

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        process.WaitForExit(5000);

        return process.ExitCode == 0;
    }

    public async Task<List<VaultInfo>> GetVaultListAsync()
    {
        _context.API.LogInfo(nameof(ProtonPassService), "Fetching vault list");

        var json = await RunPassCliAsync("vault list --output json");

        _context.API.LogInfo(nameof(ProtonPassService), $"Vault list JSON length: {json.Length} chars");

        try
        {
            var response = JsonSerializer.Deserialize<VaultListResponse>(json, JsonOptions);
            var count = response?.Vaults?.Count ?? 0;
            _context.API.LogInfo(nameof(ProtonPassService), $"Deserialized {count} vaults");
            return response?.Vaults ?? [];
        }
        catch (Exception ex)
        {
            _context.API.LogInfo(nameof(ProtonPassService), $"Vault list deserialization failed: {ex.Message}");
            throw;
        }
    }

    public async Task<List<ItemEntry>> GetItemListAsync(string vault)
    {
        _context.API.LogInfo(nameof(ProtonPassService), $"Fetching item list for vault: {vault}");

        var json = await RunPassCliAsync($"item list \"{vault}\" --output json");

        _context.API.LogInfo(nameof(ProtonPassService), $"Raw JSON output length: {json.Length} chars");

        try
        {
            var response = JsonSerializer.Deserialize<ItemListResponse>(json, JsonOptions);
            var count = response?.Items?.Count ?? 0;
            _context.API.LogInfo(nameof(ProtonPassService), $"Deserialized {count} items");
            return response?.Items ?? [];
        }
        catch (Exception ex)
        {
            _context.API.LogInfo(nameof(ProtonPassService), $"Deserialization failed: {ex.Message}");
            throw;
        }
    }

    public async Task<ItemViewResponse?> GetItemDetailAsync(string itemId, string shareId)
    {
        _context.API.LogInfo(nameof(ProtonPassService), $"Fetching detail for itemId={itemId}, shareId={shareId}");

        var json = await RunPassCliAsync($"item view --output json --item-id {itemId} --share-id {shareId}");

        _context.API.LogInfo(nameof(ProtonPassService), $"Detail JSON length: {json.Length} chars");

        try
        {
            return JsonSerializer.Deserialize<ItemViewResponse>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            _context.API.LogInfo(nameof(ProtonPassService), $"Detail deserialization failed: {ex.Message}");
            return null;
        }
    }

    private async Task<string> RunPassCliAsync(string arguments)
    {
        _context.API.LogInfo(nameof(ProtonPassService), $"Running: pass-cli {arguments}");

        var startInfo = new ProcessStartInfo
        {
            FileName = "pass-cli",
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.EnvironmentVariables["RUST_LOG"] = "error";

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = await outputTask;
        var error = await errorTask;

        _context.API.LogInfo(nameof(ProtonPassService), $"Exit code: {process.ExitCode}");

        if (!string.IsNullOrEmpty(error))
            _context.API.LogInfo(nameof(ProtonPassService), $"Stderr: {error}");
        if (!string.IsNullOrEmpty(output))
            _context.API.LogInfo(nameof(ProtonPassService), $"Stdout: {output}");

        var combinedOutput = (error + output).Trim();
        if (!string.IsNullOrEmpty(combinedOutput) &&
            (combinedOutput.Contains("not authenticated", StringComparison.OrdinalIgnoreCase) ||
             combinedOutput.Contains("no session", StringComparison.OrdinalIgnoreCase) ||
             combinedOutput.Contains("there is no session", StringComparison.OrdinalIgnoreCase) ||
             combinedOutput.Contains("requires an authenticated client", StringComparison.OrdinalIgnoreCase)))
        {
            throw new PassCliNotAuthenticatedException(combinedOutput);
        }

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"pass-cli failed (exit {process.ExitCode}): {error}");

        return output;
    }
}

public class PassCliNotAuthenticatedException(string stderr) : Exception(stderr)
{
}
