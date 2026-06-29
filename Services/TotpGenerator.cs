using System.Text.RegularExpressions;
using OtpNet;

namespace Flow.Launcher.Plugin.ProtonPass.Services;

public class TotpGenerator
{
    private readonly Totp _totp;
    private readonly int _period;

    public TotpGenerator(string totpUri)
    {
        var secret = ExtractQueryParam(totpUri, "secret")
            ?? throw new ArgumentException("Missing secret in TOTP URI");
        var secretBytes = Base32Encoding.ToBytes(secret);

        _period = int.TryParse(ExtractQueryParam(totpUri, "period"), out var p) ? p : 30;
        var digits = int.TryParse(ExtractQueryParam(totpUri, "digits"), out var d) ? d : 6;
        var algorithm = ExtractQueryParam(totpUri, "algorithm")?.ToUpperInvariant() ?? "SHA1";

        var mode = algorithm switch
        {
            "SHA256" => OtpHashMode.Sha256,
            "SHA512" => OtpHashMode.Sha512,
            _ => OtpHashMode.Sha1,
        };

        _totp = new Totp(secretBytes, step: _period, mode: mode, totpSize: digits);
    }

    public string GenerateTotp() => _totp.ComputeTotp();

    public int GetRemainingSeconds() => _period - (int)((DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond) % _period);

    private static string? ExtractQueryParam(string uri, string param)
    {
        var pattern = $@"[?&]{param}=([^&]+)";
        var m = Regex.Match(uri, pattern, RegexOptions.IgnoreCase);
        return m.Success ? Uri.UnescapeDataString(m.Groups[1].Value) : null;
    }
}
