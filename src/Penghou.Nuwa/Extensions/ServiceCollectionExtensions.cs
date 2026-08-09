using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Penghou.Nuwa.Strategies;

namespace Penghou.Nuwa.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddJsonRepair(
        this IServiceCollection services) =>
        services.AddJsonRepair(_ => { });

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
            services.AddSingleton(type);
        }

        foreach (var type in options.NodeRepairs)
        {
            services.AddSingleton(type);
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
                    serviceProvider.GetRequiredService<
                        ILogger<JsonRepairPipeline>>(),
                    options.Limits);
            });

        return services;
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
