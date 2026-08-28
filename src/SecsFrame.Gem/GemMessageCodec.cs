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
    {
        if (root is null || root.Format != SecsItemFormat.Binary || root.Count != 1)
        {
            throw new GemProtocolException(
                $"The {operation} body must be one Binary acknowledgement byte.");
        }

        return root.GetValues<byte>()[0];
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

    private static string RequireAscii(SecsItem item, string operation)
    {
        if (item.Format != SecsItemFormat.Ascii)
        {
            throw new GemProtocolException(
                $"The {operation} identity values must be ASCII.");
        }

        return item.GetString();
    }
}
