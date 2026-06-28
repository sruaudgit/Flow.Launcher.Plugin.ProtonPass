using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Flow.Launcher.Plugin.ProtonPass.Settings;

public class SettingsViewModel : INotifyPropertyChanged
{
    private readonly ProtonPassSettings _settings;

    public SettingsViewModel(ProtonPassSettings settings)
    {
        _settings = settings;
    }

    public string DefaultVault
    {
        get => _settings.DefaultVault;
        set
        {
            _settings.DefaultVault = value;
            OnPropertyChanged();
        }
    }

    public int MaxResults
    {
        get => _settings.MaxResults;
        set
        {
            _settings.MaxResults = value;
            OnPropertyChanged();
        }
    }

    public bool KeepOpenAfterAction
    {
        get => _settings.KeepOpenAfterAction;
        set
        {
            _settings.KeepOpenAfterAction = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
