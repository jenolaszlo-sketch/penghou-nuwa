using FluentAssertions;
using Penghou.Nuwa.Strategies;

namespace Penghou.Nuwa.Tests;

/// <summary>
/// Regression tests for adversarial-input robustness: bounded speculative
/// search, consistent escape decoding, and nested budget sharing.
/// </summary>
public sealed class RepairRobustnessTests
{
    [Fact]
    public async Task VerbatimStrategy_AdversarialInput_TerminatesWithinBudget()
    {
        // Many verbatim-string starts in value positions: each candidate
        // expansion is speculative. The shared budget must terminate the
        // search instead of exploring a 64-way branching tree.
        var starts = string.Concat(
            Enumerable.Range(0, 400)
                .Select(i => $"\"k{i}\": @\"value {i}\","));

        var strategy = new PseudoCSharpVerbatimStringRepairStrategy();

        // Must complete (bounded) rather than hang; outcome is either a repair
        // or not-applicable once the budget is exhausted.
        var attempt = await strategy.RepairAsync(
            "{\"" + starts,
            TestContext.Current.CancellationToken);

        attempt.Should().NotBeNull();
    }

    [Fact]
    public void RecoveryParser_NestedCorrectionsShareOneBudget()
    {
        var expectation = JsonSchemaExpectation.FromSchemaJson(
            """
            {
              "type": "object",
              "properties": { "value": { "type": "object" } }
            }
            """)!;
        var parser = new TolerantJsonRecoveryParser(
            "{\"value\":\"{broken: true}\"}",
            expectation,
            JsonRepairLimits.Default with { MaxCorrections = 1 },
            TestContext.Current.CancellationToken);

        var action = () => parser.Parse();

        action.Should().Throw<JsonRepairLimitException>(
            "the nested property repair and parent expansion must consume the same budget");
    }

    [Fact]
    public void RecoveryParser_HardDepthCap_IsEnforcedBehaviorally()
    {
        var input = new string('[', TolerantJsonRecoveryParser.HardMaxDepth + 1) +
            "null" +
            new string(']', TolerantJsonRecoveryParser.HardMaxDepth + 1);
        var parser = new TolerantJsonRecoveryParser(
            input,
            expectation: null,
            JsonRepairLimits.Default with
            {
                MaxDepth = TolerantJsonRecoveryParser.HardMaxDepth + 100
            },
            TestContext.Current.CancellationToken);

        var action = () => parser.Parse();

        action.Should().Throw<JsonRepairLimitException>()
            .WithMessage("*maximum nesting depth*");
    }

    [Fact]
    public async Task RecoveryParser_SingleQuoteEscapes_DecodeConsistently()
    {
        // A single-quoted pseudo-string containing \n must decode to a real
        // newline, not drop the backslash and keep "n".
        var parser = new TolerantJsonRecoveryParser(
            @"{""text"": 'line one\nline two'}",
            expectation: null,
            JsonRepairLimits.Default,
            TestContext.Current.CancellationToken);

        var result = parser.Parse();

        result.Root.Should().NotBeNull(
            string.Join(" | ", result.Repairs));
        var text = result.Root!["text"]!.GetValue<string>();
        text.Should().Contain("\n");
        text.Should().NotContain("\\n");
    }

    [Theory]
    [InlineData(@"{""text"":'\u0041'}", "A")]
    [InlineData(@"{""text"":'\q'}", @"\q")]
    [InlineData(@"{""text"":'it\'s'}", "it's")]
    public void RecoveryParser_SingleQuoteEscapes_PreserveMeaning(
        string input,
        string expected)
    {
        var parser = new TolerantJsonRecoveryParser(
            input,
            expectation: null,
            JsonRepairLimits.Default,
            TestContext.Current.CancellationToken);

        var result = parser.Parse();

        result.Root.Should().NotBeNull(string.Join(" | ", result.Repairs));
        result.Root!["text"]!.GetValue<string>().Should().Be(expected);
    }
}
