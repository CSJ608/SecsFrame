using System.Text.Json;
using System.Text.Json.Serialization;
using SecsFrame.CommunicationDemo.Models;
using SecsFrame.Sml;

namespace SecsFrame.CommunicationDemo.Services;

internal static class ActivityExportBuilder
{
    private const string FormatIdentifier =
        "SecsFrame-CommunicationDemo-Activity/1";
    private static readonly SmlMessageCodec Sml = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Build(
        IEnumerable<ActivityEntry> entries,
        ActivityExportClassification classification)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (!Enum.IsDefined(classification))
        {
            throw new ArgumentOutOfRangeException(
                nameof(classification),
                classification,
                "Unknown activity export classification.");
        }

        var records = entries
            .Select(entry => CreateRecord(entry, classification))
            .ToArray();
        var document = new ActivityExportDocument(
            FormatIdentifier,
            DateTimeOffset.UtcNow,
            classification,
            classification == ActivityExportClassification.MetadataOnly
                ? "仅含时间、级别、类别和标题。"
                : "含受限运维元数据；SECS-II Body 已整体替换为 REDACTED。",
            records);
        return JsonSerializer.Serialize(document, JsonOptions);
    }

    private static ActivityExportRecord CreateRecord(
        ActivityEntry entry,
        ActivityExportClassification classification)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (classification == ActivityExportClassification.MetadataOnly)
        {
            return new ActivityExportRecord(
                entry.Id,
                entry.Timestamp.ToUniversalTime(),
                entry.Tone,
                entry.Category,
                entry.Title,
                null,
                null);
        }

        return new ActivityExportRecord(
            entry.Id,
            entry.Timestamp.ToUniversalTime(),
            entry.Tone,
            entry.Category,
            entry.Title,
            entry.Summary,
            RedactDetail(entry));
    }

    private static string? RedactDetail(ActivityEntry entry)
        => entry.DetailKind switch
        {
            ActivityDetailKind.None => null,
            ActivityDetailKind.ProtocolMessage => RedactSml(entry.Detail),
            ActivityDetailKind.DiagnosticMetadata => entry.Detail,
            ActivityDetailKind.BoundaryNote => entry.Detail,
            _ => throw new ArgumentOutOfRangeException(
                nameof(entry),
                entry.DetailKind,
                "Unknown activity detail kind."),
        };

    private static string? RedactSml(string? text)
    {
        if (text is null)
            return null;

        var message = Sml.Decode(text);
        var redacted = new SecsMessage(
            message.Stream,
            message.Function,
            message.ReplyExpected,
            message.RootItem is null ? null : SecsItem.Ascii("REDACTED"));
        return Sml.Encode(redacted);
    }

    private sealed record ActivityExportDocument(
        string Format,
        DateTimeOffset ExportedAt,
        ActivityExportClassification Classification,
        string DataBoundary,
        IReadOnlyList<ActivityExportRecord> Records);

    private sealed record ActivityExportRecord(
        long Id,
        DateTimeOffset Timestamp,
        ActivityEntryTone Tone,
        string Category,
        string Title,
        string? Summary,
        string? Detail);
}
