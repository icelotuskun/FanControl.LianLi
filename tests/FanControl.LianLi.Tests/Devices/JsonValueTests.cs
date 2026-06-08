#if ENABLE_LIGHTING
using System;
using FanControl.LianLi.Devices;
using Xunit;

namespace FanControl.LianLi.Tests.Devices;

public sealed class JsonValueTests
{
    [Fact]
    public void Parse_ReadsObjectMembersStringsAndNumbers()
    {
        JsonValue root = JsonValue.Parse(
            "{ \"DeviceID\": \"abc\", \"Type\": \"LightingPort2\", \"Data\": { \"Port\": 2, \"Mode\": 46 } }");

        Assert.Equal("abc", root.Member("DeviceID")!.AsString());
        Assert.Equal("LightingPort2", root.Member("Type")!.AsString());

        JsonValue data = root.Member("Data")!;
        Assert.Equal(2, data.Member("Port")!.AsInt());
        Assert.Equal(46, data.Member("Mode")!.AsInt());
    }

    [Fact]
    public void Parse_ReadsArraysOfObjectsAndScalars()
    {
        JsonValue root = JsonValue.Parse(
            "{ \"Colors\": [ { \"R\": 255, \"G\": 8, \"B\": 0 }, { \"R\": 0, \"G\": 215, \"B\": 255 } ] }");

        var colors = root.Member("Colors")!.Elements;
        Assert.Equal(2, colors.Count);
        Assert.Equal(255, colors[0].Member("R")!.AsInt());
        Assert.Equal(215, colors[1].Member("G")!.AsInt());

        JsonValue quantity = JsonValue.Parse("[4, 4, 3, 0]");
        Assert.Equal(4, quantity.Elements.Count);
        Assert.Equal(3, quantity.Elements[2].AsInt());
    }

    [Fact]
    public void Parse_HandlesEscapesNegativesBoolsAndNull()
    {
        JsonValue root = JsonValue.Parse("{ \"s\": \"a\\\"b\\\\c\", \"n\": -12.5, \"t\": true, \"z\": null }");

        Assert.Equal("a\"b\\c", root.Member("s")!.AsString());
        Assert.Equal(-12, root.Member("n")!.AsInt()); // number truncated toward zero
        Assert.Null(root.Member("absent"));
    }

    [Fact]
    public void TypeMismatchedAccessors_ReturnNullOrEmpty()
    {
        JsonValue root = JsonValue.Parse("{ \"Mode\": 46, \"Name\": \"x\" }");

        Assert.Null(root.Member("Mode")!.AsString()); // a number is not a string
        Assert.Null(root.Member("Name")!.AsInt());    // a string is not a number
        Assert.Empty(root.Member("Mode")!.Elements);  // a number has no array elements
    }

    [Theory]
    [InlineData("{ \"a\": }")]      // missing value
    [InlineData("{ \"a\": 1 ")]     // unterminated object
    [InlineData("[1, 2")]            // unterminated array
    [InlineData("{ a: 1 }")]         // unquoted key
    [InlineData("nul")]              // bad literal
    [InlineData("1 2")]              // trailing content
    public void Parse_Malformed_Throws(string text)
    {
        Assert.Throws<FormatException>(() => JsonValue.Parse(text));
    }

    [Fact]
    public void Parse_DeeplyNested_ThrowsInsteadOfOverflowingTheStack()
    {
        // Far deeper than the parser's nesting cap; it must throw a catchable FormatException
        // rather than recurse into an (uncatchable) StackOverflowException.
        string json = new string('[', 5000) + new string(']', 5000);
        Assert.Throws<FormatException>(() => JsonValue.Parse(json));
    }
}
#endif
