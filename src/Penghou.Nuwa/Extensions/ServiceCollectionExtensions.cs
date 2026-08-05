using Microsoft.Extensions.DependencyInjection;
using Penghou.Nuwa.Strategies;

namespace Penghou.Nuwa.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddJsonRepair(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<
            IReadOnlyList<ITextRepairStrategy>>(
            [
                new MarkdownJsonFenceRepairStrategy(),
                new PseudoCSharpVerbatimStringRepairStrategy(),
                new PseudoJavaScriptTemplateStringRepairStrategy()
            ]);

        services.AddSingleton<
            ITolerantJsonSyntaxTreeParser,
            TolerantJsonSyntaxTreeParser>();
        services.AddSingleton<
            IReadOnlyList<INodeRepairStrategy>>(
            serviceProvider =>
            [
                new SchemaGuidedOptionalNullRemovalStrategy(),
                new SchemaGuidedJsonStringExpansionStrategy(
                    serviceProvider.GetRequiredService<
                        ITolerantJsonSyntaxTreeParser>())
            ]);
        services.AddSingleton<
            IJsonRepairPipeline,
            JsonRepairPipeline>();

        return services;
    }
}
