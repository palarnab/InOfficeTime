using System.Net.NetworkInformation;

namespace InOfficeTime;

/// <summary>
/// Detects whether the machine is on the office network by pinging a known office-only host.
/// Host and timeout can be overridden via appsettings.json:
///   "OfficeDetection": { "Host": "10.41.11.71", "TimeoutMs": 1000 }
/// </summary>
public sealed class LocationDetector
{
    public const string DefaultHost = "10.41.11.71";
    public const int DefaultTimeoutMs = 1000;

    private readonly ILogger<LocationDetector> _logger;
    private readonly string _host;
    private readonly int _timeoutMs;

    public LocationDetector(IConfiguration configuration, ILogger<LocationDetector> logger)
    {
        _logger = logger;

        var section = configuration.GetSection("OfficeDetection");
        var host = section["Host"];
        _host = string.IsNullOrWhiteSpace(host) ? DefaultHost : host.Trim();
        _timeoutMs = section.GetValue<int?>("TimeoutMs") ?? DefaultTimeoutMs;

        _logger.LogInformation(
            "LocationDetector configured with Host={Host}, TimeoutMs={TimeoutMs}.",
            _host, _timeoutMs);
    }

    public string Detect()
    {
        try
        {
            using var ping = new Ping();
            var reply = ping.Send(_host, _timeoutMs);
            if (reply.Status == IPStatus.Success)
                return Location.Office;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Ping to {Host} failed; treating as remote.", _host);
        }

        return Location.Remote;
    }
}
