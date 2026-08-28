namespace SecsFrame.Gem;

internal static class GemMessageCodec
{
    internal static SecsItem EncodeIdentity(GemIdentity identity)
        => SecsItem.List(
            SecsItem.Ascii(identity.Model),
            SecsItem.Ascii(identity.SoftwareRevision));

    internal static GemIdentity DecodeIdentity(SecsItem? root, string operation)
    {
        var items = RequireList(root, 2, operation);
        return new GemIdentity(
            RequireAscii(items[0], operation),
            RequireAscii(items[1], operation));
    }

    internal static SecsItem EncodeCommunicationReply(
        byte acknowledgement,
        GemIdentity identity)
        => SecsItem.List(
            EncodeAcknowledgement(acknowledgement),
            EncodeIdentity(identity));

    internal static (byte Acknowledgement, GemIdentity? Identity)
        DecodeCommunicationReply(SecsItem? root)
    {
        const string operation = "communication-establishment reply";
        var items = RequireList(root, 2, operation);
        if (items[1].Format == SecsItemFormat.List && items[1].Count == 0)
        {
            return (
                DecodeAcknowledgement(items[0], operation),
                null);
        }

        return (
            DecodeAcknowledgement(items[0], operation),
            DecodeIdentity(items[1], operation));
    }

    internal static SecsItem EncodeAcknowledgement(byte value)
        => SecsItem.Binary(value);

    internal static byte DecodeAcknowledgement(SecsItem? root, string operation)
        => RequireBinaryByte(root, $"{operation} acknowledgement");

    private static byte RequireBinaryByte(SecsItem? item, string field)
    {
        if (item is null || item.Format != SecsItemFormat.Binary || item.Count != 1)
        {
            throw new GemProtocolException(
                $"The {field} must be one Binary byte.");
        }

        return item.GetValues<byte>()[0];
    }

    internal static SecsItem EncodeIdentifiers(IEnumerable<SecsItem> identifiers)
    {
        if (identifiers is null)
            throw new ArgumentNullException(nameof(identifiers));

        return SecsItem.List(identifiers);
    }

    internal static IReadOnlyList<SecsItem> DecodeList(
        SecsItem? root,
        string operation)
    {
        if (root is null || root.Format != SecsItemFormat.List)
            throw new GemProtocolException($"The {operation} body must be a List.");

        return root.Items;
    }

    internal static SecsItem EncodeReportDefinitions(
        SecsItem dataId,
        IEnumerable<GemReportDefinition> reports)
    {
        if (dataId is null)
            throw new ArgumentNullException(nameof(dataId));

        var definitions = GemCollection.Copy(reports, nameof(reports));
        ValidateUniqueReportDefinitions(definitions, nameof(reports));
        var encoded = new SecsItem[definitions.Count];
        for (var index = 0; index < definitions.Count; index++)
        {
            var definition = definitions[index];
            encoded[index] = SecsItem.List(
                definition.ReportId,
                SecsItem.List(definition.ValueIds));
        }

        return SecsItem.List(dataId, SecsItem.List(encoded));
    }

    internal static (SecsItem DataId, IReadOnlyList<GemReportDefinition> Reports)
        DecodeReportDefinitions(SecsItem? root)
    {
        const string operation = "report-definition request";
        var body = RequireList(root, 2, operation);
        var encoded = DecodeList(body[1], operation);
        var definitions = new GemReportDefinition[encoded.Count];
        var identifiers = new HashSet<SecsItem>();
        for (var index = 0; index < encoded.Count; index++)
        {
            var fields = RequireList(encoded[index], 2, operation);
            if (!identifiers.Add(fields[0]))
            {
                throw new GemProtocolException(
                    $"The {operation} contains duplicate report identifier at index {index}.");
            }

            definitions[index] = new GemReportDefinition(
                fields[0],
                DecodeList(fields[1], operation));
        }

        return (body[0], Array.AsReadOnly(definitions));
    }

    internal static SecsItem EncodeEventReportLinks(
        SecsItem dataId,
        IEnumerable<GemEventReportLink> links)
    {
        if (dataId is null)
            throw new ArgumentNullException(nameof(dataId));

        var eventLinks = GemCollection.Copy(links, nameof(links));
        ValidateUniqueEventLinks(eventLinks, nameof(links));
        var encoded = new SecsItem[eventLinks.Count];
        for (var index = 0; index < eventLinks.Count; index++)
        {
            var link = eventLinks[index];
            encoded[index] = SecsItem.List(
                link.EventId,
                SecsItem.List(link.ReportIds));
        }

        return SecsItem.List(dataId, SecsItem.List(encoded));
    }

    internal static (SecsItem DataId, IReadOnlyList<GemEventReportLink> Links)
        DecodeEventReportLinks(SecsItem? root)
    {
        const string operation = "event-report-link request";
        var body = RequireList(root, 2, operation);
        var encoded = DecodeList(body[1], operation);
        var links = new GemEventReportLink[encoded.Count];
        var identifiers = new HashSet<SecsItem>();
        for (var index = 0; index < encoded.Count; index++)
        {
            var fields = RequireList(encoded[index], 2, operation);
            if (!identifiers.Add(fields[0]))
            {
                throw new GemProtocolException(
                    $"The {operation} contains duplicate event identifier at index {index}.");
            }

            var reportIds = DecodeList(fields[1], operation);
            RequireUniqueIdentifiers(
                reportIds,
                operation,
                "report",
                index);
            links[index] = new GemEventReportLink(fields[0], reportIds);
        }

        return (body[0], Array.AsReadOnly(links));
    }

    internal static SecsItem EncodeCollectionEvent(
        GemCollectionEvent collectionEvent)
    {
        if (collectionEvent is null)
            throw new ArgumentNullException(nameof(collectionEvent));

        var identifiers = new HashSet<SecsItem>();
        var encoded = new SecsItem[collectionEvent.Reports.Count];
        for (var index = 0; index < collectionEvent.Reports.Count; index++)
        {
            var report = collectionEvent.Reports[index];
            if (!identifiers.Add(report.ReportId))
            {
                throw new ArgumentException(
                    $"The Collection Event contains duplicate report identifier at index {index}.",
                    nameof(collectionEvent));
            }

            encoded[index] = SecsItem.List(
                report.ReportId,
                SecsItem.List(report.Values));
        }

        return SecsItem.List(
            collectionEvent.DataId,
            collectionEvent.EventId,
            SecsItem.List(encoded));
    }

    internal static GemCollectionEvent DecodeCollectionEvent(SecsItem? root)
    {
        const string operation = "Collection Event";
        var body = RequireList(root, 3, operation);
        var encoded = DecodeList(body[2], operation);
        var reports = new GemCollectedReport[encoded.Count];
        var identifiers = new HashSet<SecsItem>();
        for (var index = 0; index < encoded.Count; index++)
        {
            var fields = RequireList(encoded[index], 2, operation);
            if (!identifiers.Add(fields[0]))
            {
                throw new GemProtocolException(
                    $"The {operation} contains duplicate report identifier at index {index}.");
            }

            reports[index] = new GemCollectedReport(
                fields[0],
                DecodeList(fields[1], operation));
        }

        return new GemCollectionEvent(
            body[0],
            body[1],
            reports);
    }

    internal static SecsItem EncodeAlarmNotification(
        GemAlarmNotification notification)
    {
        if (notification is null)
            throw new ArgumentNullException(nameof(notification));

        return SecsItem.List(
            SecsItem.Binary(notification.Code),
            notification.AlarmId,
            SecsItem.Ascii(notification.Text));
    }

    internal static GemAlarmNotification DecodeAlarmNotification(SecsItem? root)
    {
        const string operation = "alarm notification";
        var body = RequireList(root, 3, operation);
        return new GemAlarmNotification(
            RequireBinaryByte(body[0], "alarm-notification code"),
            body[1],
            RequireAscii(body[2], operation, "text"));
    }

    internal static SecsItem EncodeClock(DateTimeOffset value, GemClockCodec codec)
        => SecsItem.Ascii(codec.Encode(value));

    internal static DateTimeOffset DecodeClock(
        SecsItem? root,
        GemClockCodec codec,
        string operation)
    {
        if (root is null || root.Format != SecsItemFormat.Ascii)
            throw new GemProtocolException($"The {operation} body must be ASCII.");

        try
        {
            return codec.Decode(root.GetString());
        }
        catch (Exception exception) when (
            exception is ArgumentException or FormatException or OverflowException)
        {
            throw new GemProtocolException(
                $"The {operation} body contains an invalid clock value.",
                exception);
        }
    }

    internal static void RequireEmptyBody(SecsItem? root, string operation)
    {
        if (root is not null)
            throw new GemProtocolException($"The {operation} body must be empty.");
    }

    internal static void RequireReplyExpected(
        HsmsPrimaryContext context,
        string operation)
    {
        if (!context.ReplyExpected)
        {
            throw new GemProtocolException(
                $"The {operation} Primary must request a Secondary reply.");
        }
    }

    internal static void RequireSecondary(
        HsmsDataMessage response,
        GemMessagePair pair,
        GemOperation operation)
    {
        var message = response.Message;
        if (message.Stream != pair.Stream ||
            message.Function != pair.SecondaryFunction ||
            message.ReplyExpected)
        {
            throw new GemProtocolException(
                $"The {operation} response must be " +
                $"S{pair.Stream}F{pair.SecondaryFunction} without W-Bit.");
        }
    }

    private static IReadOnlyList<SecsItem> RequireList(
        SecsItem? root,
        int count,
        string operation)
    {
        if (root is null || root.Format != SecsItemFormat.List || root.Count != count)
        {
            throw new GemProtocolException(
                $"The {operation} body must be a {count}-element List.");
        }

        return root.Items;
    }

    private static string RequireAscii(
        SecsItem item,
        string operation,
        string field = "identity value")
    {
        if (item.Format != SecsItemFormat.Ascii)
        {
            throw new GemProtocolException(
                $"The {operation} {field} must be ASCII.");
        }

        return item.GetString();
    }

    private static void ValidateUniqueReportDefinitions(
        IReadOnlyList<GemReportDefinition> reports,
        string parameterName)
    {
        var identifiers = new HashSet<SecsItem>();
        for (var index = 0; index < reports.Count; index++)
        {
            if (!identifiers.Add(reports[index].ReportId))
            {
                throw new ArgumentException(
                    $"Report identifier at index {index} is duplicated.",
                    parameterName);
            }
        }
    }

    private static void ValidateUniqueEventLinks(
        IReadOnlyList<GemEventReportLink> links,
        string parameterName)
    {
        var identifiers = new HashSet<SecsItem>();
        for (var index = 0; index < links.Count; index++)
        {
            if (!identifiers.Add(links[index].EventId))
            {
                throw new ArgumentException(
                    $"Event identifier at index {index} is duplicated.",
                    parameterName);
            }
        }
    }

    private static void RequireUniqueIdentifiers(
        IReadOnlyList<SecsItem> identifiers,
        string operation,
        string kind,
        int parentIndex)
    {
        var unique = new HashSet<SecsItem>();
        for (var index = 0; index < identifiers.Count; index++)
        {
            if (!unique.Add(identifiers[index]))
            {
                throw new GemProtocolException(
                    $"The {operation} entry at index {parentIndex} contains " +
                    $"duplicate {kind} identifier at index {index}.");
            }
        }
    }
}
