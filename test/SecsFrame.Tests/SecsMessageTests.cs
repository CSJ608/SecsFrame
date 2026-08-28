namespace SecsFrame.Tests;

public sealed class SecsMessageTests
{
    [Fact]
    public void Message_preserves_dynamic_header_fields_and_root_item()
    {
        var rootItem = SecsItem.List(SecsItem.Ascii("LOT-001"), SecsItem.U4(1001));

        var message = new SecsMessage(127, byte.MaxValue, true, rootItem);

        Assert.Equal(127, message.Stream);
        Assert.Equal(byte.MaxValue, message.Function);
        Assert.True(message.ReplyExpected);
        Assert.Same(rootItem, message.RootItem);
    }

    [Fact]
    public void Message_rejects_stream_outside_seven_bit_range()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SecsMessage(128, 1));
    }

    [Fact]
    public void Hsms_envelope_requires_a_message()
    {
        Assert.Throws<ArgumentNullException>(
            () => new HsmsDataMessage(1, 2, null!));
    }
}
