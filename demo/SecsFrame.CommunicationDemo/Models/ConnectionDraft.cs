using System.ComponentModel.DataAnnotations;
using System.Net;

namespace SecsFrame.CommunicationDemo.Models;

internal sealed class ConnectionDraft : IValidatableObject
{
    [Required]
    public string IpAddress { get; set; } = "127.0.0.1";

    [Range(1, 65535)]
    public int Port { get; set; } = 5000;

    public HsmsConnectionMode ConnectionMode { get; set; } =
        HsmsConnectionMode.Active;

    [Range(0, 65534)]
    public int SessionId { get; set; }

    [Range(1, 3600)]
    public int T3Seconds { get; set; } = 10;

    [Range(1, 3600)]
    public int T5Seconds { get; set; } = 2;

    [Range(1, 3600)]
    public int T6Seconds { get; set; } = 5;

    [Range(1, 3600)]
    public int T7Seconds { get; set; } = 10;

    [Range(1, 3600)]
    public int T8Seconds { get; set; } = 5;

    public bool UseLoopbackPeer { get; set; } = true;

    public IEnumerable<ValidationResult> Validate(
        ValidationContext validationContext)
    {
        if (!IPAddress.TryParse(IpAddress, out _))
        {
            yield return new ValidationResult(
                "请输入有效的 IPv4 或 IPv6 地址。",
                new[] { nameof(IpAddress) });
        }
    }

    public ConnectionDraft Snapshot()
        => new()
        {
            IpAddress = IpAddress,
            Port = Port,
            ConnectionMode = ConnectionMode,
            SessionId = SessionId,
            T3Seconds = T3Seconds,
            T5Seconds = T5Seconds,
            T6Seconds = T6Seconds,
            T7Seconds = T7Seconds,
            T8Seconds = T8Seconds,
            UseLoopbackPeer = UseLoopbackPeer,
        };
}
