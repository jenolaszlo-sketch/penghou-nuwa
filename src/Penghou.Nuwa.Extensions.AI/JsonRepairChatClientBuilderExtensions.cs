using Microsoft.Extensions.AI;

namespace Penghou.Nuwa.Extensions.AI;

/// <summary>
/// Extension methods for wiring <see cref="JsonRepairChatClient"/> into a
/// Microsoft.Extensions.AI chat-client pipeline.
/// </summary>
public static class JsonRepairChatClientBuilderExtensions
{
    /// <summary>
    /// Adds a <see cref="JsonRepairChatClient"/> stage to the pipeline.
    /// </summary>
    /// <param name="builder">The builder to extend.</param>
    /// <param name="configure">
    /// Configures the underlying Nuwa repair pipeline. When null, the default
    /// strategy set is used.
    /// </param>
    /// <returns>The builder, for chaining.</returns>
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
}
