using System;
using System.Text.Json;
using FluentAssertions;
using Penghou.Nuwa.Strategies;

namespace Penghou.Nuwa.Tests.Strategies;

public sealed class MarkdownJsonFenceRepairStrategyTests
{
    private readonly MarkdownJsonFenceRepairStrategy _strategy =
        new();

    [Fact]
    public void RepairAsync_RemovesJsonFenceFromContentToolCall()
    {
        const string input =
            """
            ```json
            {
              "name": "emit_files",
              "arguments": {
                "files": [
                  {
                    "path": "Solo.Generated/Program.cs",
                    "content": "using System;\n\nConsole.WriteLine(\"Hello\");"
                  }
                ]
              }
            }
            ```
            """;

        var attempt = Repair(_strategy, input);
        var repaired = attempt.Repaired!;

        attempt.Outcome.Should()
            .Be(RepairOutcome.Repaired);

        repaired.Should()
            .NotStartWith("```");

        repaired.Should()
            .NotEndWith("```");

        using var document = ParseStrict(repaired);

        var root = document.RootElement;

        root.GetProperty("name")
            .GetString()
            .Should()
            .Be("emit_files");

        var files =
            root.GetProperty("arguments")
                .GetProperty("files");

        files.GetArrayLength()
            .Should()
            .Be(1);

        files[0]
            .GetProperty("path")
            .GetString()
            .Should()
            .Be("Solo.Generated/Program.cs");

        files[0]
            .GetProperty("content")
            .GetString()
            .Should()
            .Contain(
                "Console.WriteLine(\"Hello\");");
    }

    [Fact]
    public void RepairAsync_RemovesOpeningFenceWhenClosingFenceIsMissing()
    {
        const string input =
            """
            ```json
            {
              "name": "emit_files",
              "arguments": {
                "files": []
              }
            }
            """;

        var attempt = Repair(_strategy, input);
        var repaired = attempt.Repaired!;

        attempt.Outcome.Should()
            .Be(RepairOutcome.Repaired);

        repaired.Should()
            .NotContain("```");

        using var document = ParseStrict(repaired);

        document.RootElement
            .GetProperty("name")
            .GetString()
            .Should()
            .Be("emit_files");

        document.RootElement
            .GetProperty("arguments")
            .GetProperty("files")
            .GetArrayLength()
            .Should()
            .Be(0);
    }

    [Fact]
    public void RepairAsync_RemovesTildeJsonFence()
    {
        const string input =
            """
            ~~~JSON
            {
              "name": "emit_files",
              "arguments": {
                "files": []
              }
            }
            ~~~
            """;

        var attempt = Repair(_strategy, input);
        var repaired = attempt.Repaired!;

        attempt.Outcome.Should()
            .Be(RepairOutcome.Repaired);

        using var document = ParseStrict(repaired);

        document.RootElement
            .GetProperty("name")
            .GetString()
            .Should()
            .Be("emit_files");
    }

    [Theory]
    [InlineData(
        """
        {
          "name": "emit_files",
          "arguments": {
            "files": []
          }
        }
        """)]
    [InlineData("")]
    [InlineData("plain text")]
    [InlineData(
        """
        ```csharp
        Console.WriteLine("Hello");
        ```
        """)]
    public void RepairAsync_DoesNotModifyUnsupportedInput(
        string input)
    {
        var attempt = Repair(_strategy, input);

        attempt.Outcome.Should()
            .Be(RepairOutcome.NotApplicable);
        attempt.Repaired.Should().BeNull();
    }

    private static TextRepairAttempt Repair(
        ITextRepair strategy,
        string input) =>
        strategy.RepairAsync(input).GetAwaiter().GetResult();

    private static JsonDocument ParseStrict(
        string json)
    {
        JsonDocument? document = null;

        var act = () =>
            document = JsonDocument.Parse(json);

        act.Should().NotThrow(
            """
            the repaired output should be valid JSON.

            Repaired JSON:

            {0}
            """,
            json);

        return document!;
    }
}
