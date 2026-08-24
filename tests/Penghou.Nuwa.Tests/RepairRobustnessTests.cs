using System.Reflection;
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
    public void VerbatimStrategy_BudgetIsSharedAcrossRecursion()
    {
        // The recursion must consume one shared budget, not per-depth budgets:
        // verify by reflection that TryRepairFrom threads a mutable budget.
        var method = typeof(PseudoCSharpVerbatimStringRepairStrategy)
            .GetMethod("TryRepairFrom", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        method!.GetParameters()
            .Any(p => p.ParameterType == typeof(int).MakeByRefType())
            .Should().BeTrue("nested expansions must share one depleting budget");
    }

    [Fact]
    public void RecoveryParser_HardDepthCap_IsBounded()
    {
        TolerantJsonRecoveryParser.HardMaxDepth
            .Should().BeLessThanOrEqualTo(512);
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
}
