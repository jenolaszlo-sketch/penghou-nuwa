using FluentAssertions;

namespace Penghou.Nuwa.Tests;

public sealed class TolerantJsonTokenReaderTests
{
    [Fact]
    public void RepeatedLookaheadFromSameOffsets_ReusesTokenization()
    {
        var reader = new TolerantJsonTokenReader(
            "  /* comment */ { \"value\": 42 }");

        var first = reader.PeekFrom(0, distance: 3);
        var tokenizations = reader.TokenizationCount;
        var second = reader.PeekFrom(0, distance: 3);

        second.Should().Be(first);
        reader.TokenizationCount.Should().Be(tokenizations);
    }

    [Fact]
    public void Read_ReusesTokenPreviouslyProducedByPeek()
    {
        var reader = new TolerantJsonTokenReader("{\"value\":true}");

        var peeked = reader.Peek();
        var tokenizations = reader.TokenizationCount;
        var read = reader.Read();

        read.Should().Be(peeked);
        reader.TokenizationCount.Should().Be(tokenizations);
    }
}
