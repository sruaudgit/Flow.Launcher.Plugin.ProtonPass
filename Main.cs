using System.Diagnostics;
using System.Windows.Controls;
using Flow.Launcher.Plugin.ProtonPass.Models;
using Flow.Launcher.Plugin.ProtonPass.Services;
using Flow.Launcher.Plugin.ProtonPass.Settings;

namespace Flow.Launcher.Plugin.ProtonPass;

public class Main : IAsyncPlugin, IContextMenu, ISettingProvider, IResultUpdated
{
    private PluginInitContext _context = null!;
    private ProtonPassSettings _settings = null!;
    private SettingsViewModel _settingsViewModel = null!;
    private ProtonPassService _protonPassService = null!;
    private Dictionary<string, List<ItemEntry>> _vaultCache = [];
    private Dictionary<string, string> _vaultNameById = [];
    private List<VaultInfo> _vaults = [];
    private string? _defaultVaultId;
    private DateTime _lastLoginPrompt = DateTime.MinValue;
    private CancellationTokenSource? _totpCts;
    private Timer? _totpTimer;
    private Query? _lastQuery;
    private List<Result>? _lastResults;
    private TotpGenerator? _totpGenerator;
    public event ResultUpdatedEventHandler? ResultsUpdated;

    public async Task InitAsync(PluginInitContext context)
    {
        _context = context;
        _settings = context.API.LoadSettingJsonStorage<ProtonPassSettings>();
        _settingsViewModel = new SettingsViewModel(_settings);
        _protonPassService = new ProtonPassService(context);

        _context.API.LogInfo(nameof(Main), $"DefaultVault setting: '{_settings.DefaultVault}'");

        try
        {
            _vaults = await _protonPassService.GetVaultListAsync();
            _context.API.LogInfo(nameof(Main), $"Fetched {_vaults.Count} vaults");

            _vaultNameById = _vaults.ToDictionary(v => v.VaultId, v => v.Name);

            if (_vaults.Count == 1)
            {
                _settings.DefaultVault = _vaults[0].Name;
                _context.API.LogInfo(nameof(Main), $"Single vault, default overridden to '{_settings.DefaultVault}'");
            }

            var defaultVault = _vaults.FirstOrDefault(v =>
                v.Name.Trim().Equals(_settings.DefaultVault.Trim(), StringComparison.OrdinalIgnoreCase));
            if (defaultVault is null)
            {
                _context.API.LogInfo(nameof(Main),
                    $"DefaultVault '{_settings.DefaultVault}' did not match any vault (available: {string.Join(", ", _vaults.Select(v => v.Name))}), falling back to first vault");
                defaultVault = _vaults.FirstOrDefault();
            }
            _defaultVaultId = defaultVault?.VaultId;
            _context.API.LogInfo(nameof(Main), $"DefaultVaultId resolved to '{_defaultVaultId}' for vault '{defaultVault?.Name}'");

            foreach (var vault in _vaults)
            {
                try
                {
                    var items = await _protonPassService.GetItemListAsync(vault.Name);
                    _vaultCache[vault.VaultId] = items;
                    _context.API.LogInfo(nameof(Main), $"Cached {items.Count} items for vault '{vault.Name}'");
                }
                catch (Exception ex)
                {
                    _context.API.LogInfo(nameof(Main), $"Failed to cache vault '{vault.Name}': {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            _context.API.LogInfo(nameof(Main), $"Init failed: {ex.Message}");
        }
    }

    public async Task<List<Result>> QueryAsync(Query query, CancellationToken token)
    {
        _totpCts?.Cancel();
        _totpCts = null;
        _totpGenerator = null;
        _lastResults = null;


        var searchTerm = query.Search;
        var defaultVault = _vaults.FirstOrDefault(v =>
            v.Name.Trim().Equals(_settings.DefaultVault.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? _vaults.FirstOrDefault();
        string? selectedVaultId = defaultVault?.VaultId;
        var displayVaultName = defaultVault?.Name ?? _settings.DefaultVault;
        var vaultPrefix = "";

        // --- Parse @ prefix ---
        if (searchTerm.StartsWith('@'))
        {
            string? vaultName = null;
            var rest = "";

            if (searchTerm.Length == 1 || searchTerm[1] == ' ')
            {
                // Just '@' or '@ ' — vault selection mode
                if (_vaults.Count == 1)
                {
                    var only = _vaults[0];
                    return
                    [
                        new Result
                        {
                            Title = $"Search in '{only.Name}' vault",
                            SubTitle = "Press Enter to select this vault",
                            IcoPath = "Images\\vault.png",
                            Score = Result.MaxScore,
                            Action = _ =>
                            {
                                _context.API.ChangeQuery(VaultSearchQuery(only.Name));
                                return false;
                            }
                        }
                    ];
                }

                var vaultResults = new List<Result>();
                foreach (var v in _vaults)
                {
                    vaultResults.Add(new Result
                    {
                        Title = v.Name,
                        SubTitle = "Select vault",
                        IcoPath = "Images\\vault.png",
                        Action = _ =>
                        {
                            _context.API.ChangeQuery(VaultSearchQuery(v.Name));
                            return false;
                        }
                    });
                }
                vaultResults.Add(new Result
                {
                    Title = "*",
                    SubTitle = "Search all vaults",
                    IcoPath = "Images\\vault.png",
                    Action = _ =>
                    {
                        _context.API.ChangeQuery("pp @* ");
                        return false;
                    }
                });
                return vaultResults;
            }

            if (searchTerm[1] == '"')
            {
                var closeQuote = searchTerm.IndexOf('"', 2);
                if (closeQuote > 2)
                {
                    vaultName = searchTerm[2..closeQuote];
                    rest = searchTerm[(closeQuote + 1)..].TrimStart();
                }
            }
            else if (searchTerm[1] == '*')
            {
                vaultName = "*";
                rest = searchTerm[2..].TrimStart();
            }
            else
            {
                var spaceIndex = searchTerm.IndexOf(' ', 1);
                if (spaceIndex > 1)
                {
                    vaultName = searchTerm[1..spaceIndex];
                    rest = searchTerm[(spaceIndex + 1)..];
                }
                else
                {
                    vaultName = searchTerm[1..];
                    rest = "";
                }
            }

            if (vaultName == "*")
            {
                selectedVaultId = null;
                displayVaultName = "All vaults";
                vaultPrefix = "@* ";
            }
            else if (vaultName != null)
            {
                var match = _vaults.FirstOrDefault(v =>
                    v.Name.Trim().Equals(vaultName.Trim(), StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    selectedVaultId = match.VaultId;
                    displayVaultName = match.Name;
                    vaultPrefix = vaultName.Contains(' ') ? $"@\"{vaultName}\" " : $"@{vaultName} ";
                }
                else
                {
                    return [];
                }
            }

            searchTerm = rest;
            _context.API.LogInfo(nameof(Main), $"Vault override: '{displayVaultName}', searchTerm now '{searchTerm}'");
        }

        // --- Gather items from selected vault(s) ---
        IEnumerable<ItemEntry> source;
        if (selectedVaultId == null)
            source = _vaultCache.SelectMany(kvp => kvp.Value);
        else if (_vaultCache.TryGetValue(selectedVaultId, out var items))
            source = items;
        else
            source = [];

        var logins = source
            .Where(i => i.ItemType == "login" && i.State == "Active")
            .ToList();

        var totalCached = _vaultCache.Values.Sum(l => l.Count);
       

        // --- Empty original search — show refresh + list ---
        if (string.IsNullOrWhiteSpace(query.Search))
        {
            var results = new List<Result>
            {
                new Result
                {
                    Title = "Refresh Proton Pass Cache",
                    SubTitle = $"Currently {totalCached} items cached across {_vaults.Count} vault(s)",
                    IcoPath = "Images\\refresh.png",
                    Action = _ =>
                    {
                        if (!_protonPassService.IsAuthenticated())
                        {
                            ShowNotAuthenticated();
                            return true;
                        }
                        _context.API.ShowMsg("Refreshing...", "Proton Pass cache refresh triggered");
                        Task.Run(async () => await RefreshCacheAsync());
                        return true;
                    },
                    Score = Result.MaxScore
                }
            };

            results.AddRange(logins.Take(_settings.MaxResults).Select(item => new Result
            {
                Title = item.Title,
                SubTitle = $"Vault: {(_vaultNameById.TryGetValue(item.VaultId, out var n) ? n : displayVaultName)}",
                IcoPath = "Images\\account.png",
                AutoCompleteText = $"{vaultPrefix}{item.Title}",
                Action = _ =>
                {
                    _context.API.ChangeQuery($"pp {vaultPrefix}\"{item.Title}\"");
                    return false;
                }
            }));

            return results;
        }

        // --- Parse exact match ---
        bool exactMatch = searchTerm.StartsWith('"');
        if (exactMatch)
        {
            searchTerm = searchTerm.Trim('"');
            _context.API.LogInfo(nameof(Main), $"Exact match mode, searchTerm now '{searchTerm}'");
        }

        // --- Match against logins ---
        List<ItemEntry> matches;
        if (exactMatch)
            matches = logins.Where(i => i.Title.Equals(searchTerm, StringComparison.OrdinalIgnoreCase)).ToList();
        else if (string.IsNullOrWhiteSpace(searchTerm))
            matches = logins;
        else
            matches = logins.Where(i => i.Title.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)).ToList();

        //_context.API.LogInfo(nameof(Main), $"Matches after filter: {matches.Count}");

        if (matches.Count == 0)
            return [];

        if (matches.Count == 1)
        {
            _context.API.LogInfo(nameof(Main), $"Single match, fetching detail for '{matches[0].Title}'");
            _totpCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            _lastQuery = query;
            var results = await BuildDetailResults(matches[0]);
            _lastResults = results;
            if (_totpGenerator != null)
                StartTotpTimer();
            return results;
        }

        return matches.Take(_settings.MaxResults).Select(item => new Result
        {
            Title = item.Title,
            SubTitle = $"Vault: {(_vaultNameById.TryGetValue(item.VaultId, out var n) ? n : displayVaultName)}",
            IcoPath = "Images\\account.png",
            AutoCompleteText = $"{vaultPrefix}{item.Title}",
            Action = _ =>
            {
                _context.API.ChangeQuery($"pp {vaultPrefix}\"{item.Title}\"");
                return false;
            }
        }).ToList();
    }

    private static string VaultSearchQuery(string vaultName)
    {
        var quoted = vaultName.Contains(' ') ? $"\"{vaultName}\"" : vaultName;
        return $"pp @{quoted} ";
    }

    private async Task<List<Result>> BuildDetailResults(ItemEntry item)
    {
       

        var results = new List<Result>();
        ItemViewResponse? detail;

        try
        {
            detail = await _protonPassService.GetItemDetailAsync(item.Id, item.ShareId);
        }
        catch (PassCliNotAuthenticatedException ex)
        {
            _context.API.LogInfo(nameof(Main), $"Detail fetch failed - not authenticated: {ex.Message}");
            ShowNotAuthenticated();
            return results;
        }
        catch (Exception ex)
        {
            _context.API.LogInfo(nameof(Main), $"Detail fetch failed: {ex.Message}");
            return results;
        }

        if (detail?.Item?.Content?.Content?.Login is not { } login)
        {
            _context.API.LogInfo(nameof(Main), "Detail response did not contain expected Login content");
            return results;
        }

        if (!string.IsNullOrEmpty(login.Email))
        {
            results.Add(MakeCopyResult(login.Email, "Email", "Images\\email.png"));
        }

        if (!string.IsNullOrEmpty(login.Username))
        {
            results.Add(MakeCopyResult(login.Username, "Username", "Images\\username.png"));
        }

        if (!string.IsNullOrEmpty(login.Password))
        {
            var password = login.Password;
            results.Add(new Result
            {
                Title = "****",
                SubTitle = "Password",
                IcoPath = "Images\\password.png",
                Action = _ =>
                {
                    CopyToClipboard(password, "Password");
                    return !_settings.KeepOpenAfterAction;
                }
            });
        }

        if (!string.IsNullOrEmpty(login.TotpUri))
        {
            _totpGenerator = new TotpGenerator(login.TotpUri);
            var token = _totpGenerator.GenerateTotp();
            var remaining = _totpGenerator.GetRemainingSeconds();
            results.Add(new Result
            {
                Title = FormatTotpToken(token),
                SubTitle = $"TOTP · expires in {remaining}s",
                IcoPath = "Images\\password.png",
                RecordKey = "totp_" + item.Id,
                Action = _ =>
                {
                    CopyToClipboard(token, "TOTP");
                    return !_settings.KeepOpenAfterAction;
                }
            });
        }

        if (login.Urls is not null)
        {
            foreach (var url in login.Urls)
            {
                if (!string.IsNullOrEmpty(url))
                    results.Add(MakeUrlResult(url, "Images\\url.png"));
            }
        }

        if (detail.Item.Content.ExtraFields is not null)
        {
            foreach (var field in detail.Item.Content.ExtraFields)
            {
                var value = field.Content?.Text;
                if (!string.IsNullOrEmpty(value))
                    results.Add(MakeCopyResult(value, field.FieldName, "Images\\protonpass.png"));
            }
        }

        return results;
    }

    private void StartTotpTimer()
    {
        _totpTimer?.Dispose();
        _totpTimer = new Timer(TotpTimerCallback, null, 0, 1000);
    }

    private void TotpTimerCallback(object? state)
    {
        if (_totpCts?.IsCancellationRequested == true || _totpGenerator == null || _lastResults == null)
            return;

        try
        {
            var token = _totpGenerator.GenerateTotp();
            var remaining = _totpGenerator.GetRemainingSeconds();
            var totpResult = _lastResults.FirstOrDefault(r => r.RecordKey?.StartsWith("totp_") == true);
            if (totpResult == null)
                return;

            totpResult.Title = FormatTotpToken(token);
            totpResult.SubTitle = $"TOTP · expires in {remaining}s";

            void Fire() => ResultsUpdated?.Invoke(this, new ResultUpdatedEventArgs
            {
                Results = _lastResults,
                Query = _lastQuery!,
                Token = _totpCts?.Token ?? default
            });
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher?.CheckAccess() == true)
                Fire();
            else
                dispatcher?.Invoke(Fire);
        }
        catch (Exception ex)
        {
            _context.API.LogInfo(nameof(Main), $"TOTP timer error: {ex.Message}");
        }
    }

    private static string FormatTotpToken(string token)
    {
        return token.Length switch
        {
            6 => $"{token[..3]} {token[3..]}",
            8 => $"{token[..3]} {token[3..6]} {token[6..]}",
            _ => token
        };
    }

    private void CopyToClipboard(string text, string label)
    {
        try
        {
            System.Windows.Clipboard.SetText(text);
            
        }
        catch (Exception ex)
        {
            _context.API.LogInfo(nameof(Main), $"Clipboard copy failed for '{label}': {ex.Message}");
            _context.API.ShowMsg("Clipboard Error", $"Could not copy {label}. The clipboard may be in use by another application.");
        }
    }

    private Result MakeCopyResult(string value, string label, string iconPath)
    {
        return new Result
        {
            Title = value,
            SubTitle = label,
            IcoPath = iconPath,
            Action = _ =>
            {
                CopyToClipboard(value, label);
                return !_settings.KeepOpenAfterAction;
            }
        };
    }

    private Result MakeUrlResult(string url, string iconPath)
    {
        return new Result
        {
            Title = url,
            SubTitle = "URL",
            IcoPath = iconPath,
            Action = _ =>
            {
                _context.API.OpenUrl(url);
                return !_settings.KeepOpenAfterAction;
            }
        };
    }

    private void ShowNotAuthenticated()
    {
        if ((DateTime.UtcNow - _lastLoginPrompt).TotalSeconds < 3)
        {
            _context.API.LogInfo(nameof(Main), "ShowNotAuthenticated suppressed (debounce)");
            return;
        }
        _lastLoginPrompt = DateTime.UtcNow;

       
        try
        {
            void Launch()
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd",
                    Arguments = "/k pass-cli login",
                    UseShellExecute = true
                });
            }

            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher?.CheckAccess() == true)
                Launch();
            else
                dispatcher?.Invoke(Launch);
        }
        catch (Exception ex)
        {
            _context.API.LogInfo(nameof(Main), $"Failed to launch cmd: {ex.Message}");
        }
    }

    private async Task RefreshCacheAsync()
    {
        try
        {
            var total = 0;
            foreach (var vault in _vaults)
            {
                var items = await _protonPassService.GetItemListAsync(vault.Name);
                _vaultCache[vault.VaultId] = items;
                total += items.Count;
            }
            _context.API.LogInfo(nameof(Main), $"Cache refreshed: {total} items across {_vaults.Count} vault(s)");
            _context.API.ShowMsg("Proton Pass", $"Cache refreshed: {total} items across {_vaults.Count} vault(s)");
        }
        catch (Exception ex)
        {
            _context.API.LogInfo(nameof(Main), $"Cache refresh failed: {ex.Message}");
        }
    }

    public List<Result> LoadContextMenus(Result selectedResult)
    {
        return [];
    }

    public Control CreateSettingPanel()
    {
        return new ProtonPassSettingsPanel(_settingsViewModel);
    }
}
