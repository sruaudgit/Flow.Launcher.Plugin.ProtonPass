namespace Flow.Launcher.Plugin.ProtonPass.Settings;

public class ProtonPassSettings
{
    public string DefaultVault { get; set; } = "Personal";
    public int MaxResults { get; set; } = 10;
    public bool KeepOpenAfterAction { get; set; } = true;
}
