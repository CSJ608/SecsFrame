using SecsFrame.Sml;

namespace SecsFrame.Tests;

public sealed class SmlMessageCodecTests
{
    [Fact]
    public void Encode_produces_a_deterministic_nested_vector()
    {
        var codec = new SmlMessageCodec(indentSize: 2);
        var message = new SecsMessage(
            1,
            2,
            replyExpected: true,
            SecsItem.List(
                SecsItem.Ascii("LOT"),
                SecsItem.Binary(0x00, 0xFF),
                SecsItem.List()));

        var text = codec.Encode(message);

        Assert.Equal(
            "'S1F2'W\n" +
            "<L [3]\n" +
            "  <A [3] 'LOT'>\n" +
            "  <B [2] 0x00 0xFF>\n" +
            "  <L [0]\n" +
            "  >\n" +
            ">\n" +
            ".\n",
            text);
    }

    [Fact]
    public void All_item_types_round_trip_without_culture_or_width_loss()
    {
        var root = SecsItem.List(
            SecsItem.Binary(0x00, 0x80, 0xFF),
            SecsItem.Boolean(true, false),
            SecsItem.Ascii("A'\\\r\n\t\0Z"),
            SecsItem.Jis8(0x00, 0x7F, 0x80, 0xFF),
            SecsItem.I8(long.MinValue, 0, long.MaxValue),
            SecsItem.I1(sbyte.MinValue, 0, sbyte.MaxValue),
            SecsItem.I2(short.MinValue, 0, short.MaxValue),
            SecsItem.I4(int.MinValue, 0, int.MaxValue),
            SecsItem.F8(double.NegativeInfinity, -0d, double.Epsilon, double.NaN, double.PositiveInfinity),
            SecsItem.F4(float.NegativeInfinity, -0f, float.Epsilon, float.NaN, float.PositiveInfinity),
            SecsItem.U8(0, ulong.MaxValue),
            SecsItem.U1(0, byte.MaxValue),
            SecsItem.U2(0, ushort.MaxValue),
            SecsItem.U4(0, uint.MaxValue),
            SecsItem.List());
        var expected = new SecsMessage(127, 255, true, root);
        var codec = new SmlMessageCodec();

        var encoded = codec.Encode(expected);
        var actual = codec.Decode(encoded);

        AssertMessageEqual(expected, actual);
        Assert.Contains("<J [4] 0x00 0x7F 0x80 0xFF>", encoded, StringComparison.Ordinal);
        Assert.Contains("<A [8] 'A\\'\\\\\\r\\n\\t\\x00Z'>", encoded, StringComparison.Ordinal);
    }

    [Fact]
    public void No_body_and_empty_list_remain_distinct()
    {
        var codec = new SmlMessageCodec();

        var noBody = codec.Decode(codec.Encode(new SecsMessage(1, 0)));
        var emptyList = codec.Decode(codec.Encode(new SecsMessage(1, 0, rootItem: SecsItem.List())));

        Assert.Null(noBody.RootItem);
        Assert.NotNull(emptyList.RootItem);
        Assert.Equal(SecsItemFormat.List, emptyList.RootItem.Format);
        Assert.Empty(emptyList.RootItem.Items);
    }

    [Fact]
    public void Decode_accepts_insignificant_whitespace_but_canonicalizes_output()
    {
        const string input = "  'S6F11' W < L [ 2 ] < U4 [1] 42 > < Boolean [2] True False > > .  ";
        var codec = new SmlMessageCodec(indentSize: 2);

        var message = codec.Decode(input);

        Assert.Equal(
            "'S6F11'W\n" +
            "<L [2]\n" +
            "  <U4 [1] 42>\n" +
            "  <Boolean [2] True False>\n" +
            ">\n" +
            ".\n",
            codec.Encode(message));
    }

    [Theory]
    [InlineData("'S128F1'\n.\n")]
    [InlineData("'S1F1'\n<A [2] 'A'>\n.\n")]
    [InlineData("'S1F1'\n<B [1] 0xff>\n.\n")]
    [InlineData("'S1F1'\n<Boolean [1] true>\n.\n")]
    [InlineData("'S1F1'\n<U1 [1] 256>\n.\n")]
    [InlineData("'S1F1'\n<L [0]>\n. trailing")]
    public void Decode_rejects_non_canonical_or_malformed_vectors(string text)
    {
        var error = Assert.Throws<SmlParseException>(() => new SmlMessageCodec().Decode(text));

        Assert.True(error.Line > 0);
        Assert.True(error.Column > 0);
        Assert.True(error.Offset >= 0);
    }

    [Fact]
    public void Decode_reports_the_source_location()
    {
        const string text = "'S1F1'\n<L [1]\n    <U1 [1] nope>\n>\n.\n";

        var error = Assert.Throws<SmlParseException>(() => new SmlMessageCodec().Decode(text));

        Assert.Equal(3, error.Line);
        Assert.Equal(13, error.Column);
        Assert.Contains("U1", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Decode_enforces_depth_item_value_and_text_limits()
    {
        const string nested = "'S1F1'\n<L [1]\n<L [0]\n>\n>\n.\n";
        const string twoItems = "'S1F1'\n<L [1]\n<U1 [0]>\n>\n.\n";
        const string twoValues = "'S1F1'\n<U1 [2] 1 2>\n.\n";

        Assert.Throws<SmlParseException>(() => new SmlMessageCodec(maxNestingDepth: 1).Decode(nested));
        Assert.Throws<SmlParseException>(() => new SmlMessageCodec(maxItemCount: 1).Decode(twoItems));
        Assert.Throws<SmlParseException>(() => new SmlMessageCodec(maxValueCount: 1).Decode(twoValues));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SmlMessageCodec(maxTextLength: 4).Decode("12345"));
    }

    [Fact]
    public void Encode_enforces_the_same_structural_limits()
    {
        var nested = new SecsMessage(1, 1, rootItem: SecsItem.List(SecsItem.List()));
        var values = new SecsMessage(1, 1, rootItem: SecsItem.U1(1, 2));

        Assert.Throws<InvalidOperationException>(() => new SmlMessageCodec(maxNestingDepth: 1).Encode(nested));
        Assert.Throws<InvalidOperationException>(() => new SmlMessageCodec(maxItemCount: 1).Encode(nested));
        Assert.Throws<InvalidOperationException>(() => new SmlMessageCodec(maxValueCount: 1).Encode(values));
        Assert.Throws<InvalidOperationException>(() => new SmlMessageCodec(maxTextLength: 4).Encode(values));
    }

    [Fact]
    public void Constructor_rejects_invalid_limits()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SmlMessageCodec(indentSize: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SmlMessageCodec(indentSize: 17));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SmlMessageCodec(maxNestingDepth: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SmlMessageCodec(maxItemCount: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SmlMessageCodec(maxValueCount: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SmlMessageCodec(maxTextLength: 0));
    }

    private static void AssertMessageEqual(SecsMessage expected, SecsMessage actual)
    {
        Assert.Equal(expected.Stream, actual.Stream);
        Assert.Equal(expected.Function, actual.Function);
        Assert.Equal(expected.ReplyExpected, actual.ReplyExpected);
        Assert.Equal(expected.RootItem, actual.RootItem);
    }
}
