using CurlImpersonate.Http;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for wiring <see cref="CurlHandler"/> into
/// <c>IHttpClientFactory</c> pipelines.
/// </summary>
public static class CurlImpersonateHttpClientBuilderExtensions
{
    /// <param name="builder">The client builder to configure.</param>
    extension(IHttpClientBuilder builder)
    {
        /// <summary>
        /// Uses <see cref="CurlHandler"/> as the primary message handler for this
        /// client. Delegating handlers registered on the same builder run before
        /// the curl transport, so resilience and auth middleware compose normally.
        /// </summary>
        /// <remarks>
        /// The factory rotates primary handlers per its handler lifetime, and each
        /// rotation creates a new curl transfer loop and connection pool. curl
        /// re-resolves DNS per connection and honors
        /// <see cref="CurlHandlerOptions.PooledConnectionLifetime"/>, so the DNS
        /// rationale for short handler lifetimes does not apply; consider a longer
        /// lifetime via <c>SetHandlerLifetime</c> for busy clients.
        /// </remarks>
        /// <param name="configure">Optional callback that adjusts the handler options.</param>
        public IHttpClientBuilder AddCurlImpersonate(Action<CurlHandlerOptions>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(builder);
            return builder.ConfigurePrimaryHttpMessageHandler(_ => CreateHandler(configure));
        }

        /// <summary>
        /// Uses <see cref="CurlHandler"/> as the primary message handler for this
        /// client, with access to the service provider while configuring options.
        /// </summary>
        /// <param name="configure">Callback that adjusts the handler options using registered services.</param>
        public IHttpClientBuilder AddCurlImpersonate(Action<IServiceProvider, CurlHandlerOptions> configure)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentNullException.ThrowIfNull(configure);
            return builder.ConfigurePrimaryHttpMessageHandler(services =>
            {
                var options = new CurlHandlerOptions();
                configure(services, options);
                return new CurlHandler(options);
            });
        }
    }

    /// <summary>
    /// Registers a named <c>HttpClient</c> whose primary handler is
    /// <see cref="CurlHandler"/>. Equivalent to
    /// <c>services.AddHttpClient(name).AddCurlImpersonate(configure)</c>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="name">The logical client name used with <c>IHttpClientFactory.CreateClient</c>.</param>
    /// <param name="configure">Optional callback that adjusts the handler options.</param>
    public static IHttpClientBuilder AddCurlImpersonateClient(
        this IServiceCollection services,
        string name,
        Action<CurlHandlerOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(name);
        return services.AddHttpClient(name).AddCurlImpersonate(configure);
    }

    private static CurlHandler CreateHandler(Action<CurlHandlerOptions>? configure)
    {
        var options = new CurlHandlerOptions();
        configure?.Invoke(options);
        return new CurlHandler(options);
    }
}
