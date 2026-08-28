namespace SecsFrame.Tests;

public sealed class SecsItemTests
{
    [Fact]
    public void Factories_copy_caller_owned_arrays()
    {
        var bytes = new byte[] { 1, 2, 3 };
        var children = new[] { SecsItem.Ascii("original") };

        var binary = SecsItem.Binary(bytes);
        var list = SecsItem.List(children);
        bytes[0] = 99;
        children[0] = SecsItem.Ascii("changed");

        Assert.Equal(new byte[] { 1, 2, 3 }, binary.GetValues<byte>().ToArray());
        Assert.Equal("original", list[0].GetString());
    }

    [Fact]
    public void List_and_primitive_access_are_format_specific()
    {
        var list = SecsItem.List(SecsItem.U2(1));
        var value = SecsItem.U2(1);

        Assert.Equal(SecsItemFormat.List, list.Format);
        Assert.Single(list.Items);
        Assert.Equal((ushort)1, value.GetValues<ushort>()[0]);
        Assert.Throws<InvalidOperationException>(() => value.Items);
        Assert.Throws<InvalidOperationException>(() => list.GetValues<int>());
        Assert.Throws<InvalidOperationException>(() => value.GetString());
    }

    [Fact]
    public void Items_use_value_equality()
    {
        var left = SecsItem.List(
            SecsItem.Ascii("A"),
            SecsItem.I4(int.MinValue, int.MaxValue),
            SecsItem.List(SecsItem.Boolean(false, true)));
        var right = SecsItem.List(
            SecsItem.Ascii("A"),
            SecsItem.I4(int.MinValue, int.MaxValue),
            SecsItem.List(SecsItem.Boolean(false, true)));

        Assert.Equal(left, right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
        Assert.NotEqual(left, SecsItem.List(SecsItem.Ascii("B")));
    }

    [Fact]
    public void Ascii_factory_rejects_non_ascii_characters()
    {
        Assert.Throws<ArgumentException>(() => SecsItem.Ascii("A\u00E9"));
    }

    [Fact]
    public void List_factory_rejects_null_elements()
    {
        Assert.Throws<ArgumentException>(() => SecsItem.List(new SecsItem[] { null! }));
    }
}
