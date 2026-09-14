using Microsoft.Extensions.AI;
using System.Diagnostics.CodeAnalysis;

namespace Penghou.Nuwa.Extensions.AI;

/// <summary>
/// Extension methods for wiring <see cref="JsonRepairChatClient"/> into a
/// Microsoft.Extensions.AI chat-client pipeline.
/// </summary>
public static class JsonRepairChatClientBuilderExtensions
{
    private const string ReflectionRegistrationWarning =
        "Building the default repair pipeline activates strategies by type. " +
        "For trimmed or Native AOT applications, pass an explicit " +
        "IJsonRepairPipeline resolved from dependency injection instead.";

    /// <summary>
    /// Adds a <see cref="JsonRepairChatClient"/> stage to the pipeline.
    /// </summary>
    /// <param name="builder">The builder to extend.</param>
    /// <param name="configure">
    /// Configures the underlying Nuwa repair pipeline. When null, the default
    /// strategy set is used.
    /// </param>
    /// <returns>The builder, for chaining.</returns>
    /// <remarks>
    /// Building the default pipeline activates strategies by type, so this
    /// overload is not trim- or Native AOT-safe. Pass an explicit
    /// <see cref="IJsonRepairPipeline"/> for ahead-of-time compiled apps.
    /// </remarks>
    [RequiresUnreferencedCode(ReflectionRegistrationWarning)]
    [RequiresDynamicCode(ReflectionRegistrationWarning)]
    public static ChatClientBuilder UseJsonRepair(
        this ChatClientBuilder builder,
        Action<JsonRepairOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.Use(
            innerClient => new JsonRepairChatClient(
                innerClient,
                configure));
    }

    /// <summary>
    /// Adds a <see cref="JsonRepairChatClient"/> stage to the pipeline.
    /// </summary>
    /// <param name="builder">The builder to extend.</param>
    /// <param name="options">Repair configuration.</param>
    /// <returns>The builder, for chaining.</returns>
    [RequiresUnreferencedCode(ReflectionRegistrationWarning)]
    [RequiresDynamicCode(ReflectionRegistrationWarning)]
    public static ChatClientBuilder UseJsonRepair(
        this ChatClientBuilder builder,
        JsonRepairChatClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(options);

        return builder.Use(
            innerClient => new JsonRepairChatClient(
                innerClient,
                options));
    }

    /// <summary>
    /// Wraps <paramref name="innerClient"/> in a <see cref="JsonRepairChatClient"/>.
    /// </summary>
    /// <param name="innerClient">The client to wrap.</param>
    /// <param name="configure">
    /// Configures the underlying Nuwa repair pipeline. When null, the default
    /// strategy set is used.
    /// </param>
    /// <returns>A repaired-wrapping client.</returns>
    [RequiresUnreferencedCode(ReflectionRegistrationWarning)]
    [RequiresDynamicCode(ReflectionRegistrationWarning)]
    public static IChatClient UseJsonRepair(
        this IChatClient innerClient,
        Action<JsonRepairOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(innerClient);

        return new JsonRepairChatClient(
            innerClient,
            configure);
    }

    /// <summary>
    /// Wraps <paramref name="innerClient"/> in a <see cref="JsonRepairChatClient"/>.
    /// </summary>
    /// <param name="innerClient">The client to wrap.</param>
    /// <param name="options">Repair configuration.</param>
    /// <returns>A repaired-wrapping client.</returns>
    [RequiresUnreferencedCode(ReflectionRegistrationWarning)]
    [RequiresDynamicCode(ReflectionRegistrationWarning)]
    public static IChatClient UseJsonRepair(
        this IChatClient innerClient,
        JsonRepairChatClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(innerClient);
        ArgumentNullException.ThrowIfNull(options);

        return new JsonRepairChatClient(
            innerClient,
            options);
    }

    /// <summary>Wraps a client using an existing, DI-configured repair pipeline.</summary>
    public static IChatClient UseJsonRepair(
        this IChatClient innerClient,
        IJsonRepairPipeline pipeline,
        JsonRepairChatClientOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(innerClient);
        ArgumentNullException.ThrowIfNull(pipeline);

        return new JsonRepairChatClient(innerClient, pipeline, options);
    }

    /// <summary>Adds an existing, DI-configured repair pipeline as a client stage.</summary>
    public static ChatClientBuilder UseJsonRepair(
        this ChatClientBuilder builder,
        IJsonRepairPipeline pipeline,
        JsonRepairChatClientOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(pipeline);

        return builder.Use(
            innerClient => new JsonRepairChatClient(
                innerClient,
                pipeline,
                options));
    }
}
