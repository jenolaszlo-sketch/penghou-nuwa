using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Penghou.Nuwa.Strategies;
using System.Diagnostics.CodeAnalysis;

namespace Penghou.Nuwa.Extensions;

public static class ServiceCollectionExtensions
{
    private const string ReflectionRegistrationWarning =
        "Registering repair strategies by type uses runtime activation. " +
        "Register strategies by instance or factory for trimmed or Native AOT " +
        "applications.";

    /// <inheritdoc cref="AddJsonRepair(IServiceCollection, Action{JsonRepairOptions})" />
    [RequiresUnreferencedCode(ReflectionRegistrationWarning)]
    public static IServiceCollection AddJsonRepair(
        this IServiceCollection services) =>
        services.AddJsonRepair(_ => { });

    /// <summary>
    /// Registers the JSON repair pipeline and its default strategies. Strategy
    /// types are activated at runtime, so this overload is not trim- or Native
    /// AOT-safe; register strategies by instance or factory for ahead-of-time
    /// compiled applications.
    /// </summary>
    [RequiresUnreferencedCode(ReflectionRegistrationWarning)]
    public static IServiceCollection AddJsonRepair(
        this IServiceCollection services,
        Action<JsonRepairOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new JsonRepairOptions();
        configure(options);
        options.Validate();

        foreach (var type in options
                     .TextRepairs
                     .Concat(options.SalvageRepairs))
        {
            RegisterStrategy(services, options, type);
        }

        foreach (var type in options.NodeRepairs)
        {
            RegisterStrategy(services, options, type);
        }

        services.AddSingleton<IJsonRepairPipeline>(
            serviceProvider =>
            {
                var textRepairs =
                    ResolveRepairs<ITextRepair>(
                        serviceProvider,
                        options.TextRepairs);
                var salvageRepairs =
                    ResolveRepairs<ITextRepair>(
                        serviceProvider,
                        options.SalvageRepairs);
                var nodeRepairs =
                    ResolveRepairs<INodeRepair>(
                        serviceProvider,
                        options.NodeRepairs);

                return new JsonRepairPipeline(
                    textRepairs,
                    salvageRepairs,
                    nodeRepairs,
                    serviceProvider.GetService<ILogger<JsonRepairPipeline>>() ??
                        NullLogger<JsonRepairPipeline>.Instance,
                    options.Limits);
            });

        return services;
    }

    [RequiresUnreferencedCode(ReflectionRegistrationWarning)]
    private static void RegisterStrategy(
        IServiceCollection services,
        JsonRepairOptions options,
        Type type)
    {
        if (options.TryCreateStrategy(type, out var configured))
            services.AddSingleton(type, configured);
        else
            services.AddSingleton(type);
    }

    private static IReadOnlyList<T> ResolveRepairs<T>(
        IServiceProvider serviceProvider,
        IReadOnlyList<Type> types)
        where T : class
    {
        var repairs = new T[types.Count];

        for (var index = 0; index < types.Count; index++)
        {
            repairs[index] =
                (T)serviceProvider.GetRequiredService(
                    types[index]);
        }

        return repairs;
    }
}
