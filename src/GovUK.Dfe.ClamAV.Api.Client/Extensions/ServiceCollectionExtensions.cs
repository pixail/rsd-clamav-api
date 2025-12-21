using GovUK.Dfe.ClamAV.Api.Client.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics.CodeAnalysis;

namespace GovUK.Dfe.ClamAV.Api.Client.Extensions
{
    [ExcludeFromCodeCoverage]
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddClamAvApiClient<TClientInterface, TClientImplementation>(
            this IServiceCollection services,
            IConfiguration configuration,
            HttpClient? existingHttpClient = null)
            where TClientInterface : class
            where TClientImplementation : class, TClientInterface
        {
            var apiSettings = new ClamAvApiClientSettings();
            configuration.GetSection("ClamAvApiClient").Bind(apiSettings);

            services.AddSingleton(apiSettings);

            // Remove Azure AD token acquisition wiring
            // services.AddSingleton<ITokenAcquisitionService, TokenAcquisitionService>();

            if (existingHttpClient != null)
            {
                services.AddSingleton(existingHttpClient);
                services.AddTransient<TClientInterface, TClientImplementation>(serviceProvider =>
                {
                    return ActivatorUtilities.CreateInstance<TClientImplementation>(
                        serviceProvider, existingHttpClient, apiSettings.BaseUrl!);
                });
            }
            else
            {
                services.AddHttpClient<TClientInterface, TClientImplementation>((httpClient, serviceProvider) =>
                {
                    httpClient.BaseAddress = new Uri(apiSettings.BaseUrl!);

                    return ActivatorUtilities.CreateInstance<TClientImplementation>(
                        serviceProvider, httpClient, apiSettings.BaseUrl!);
                });

                // Remove Bearer token handler that injected Authorization headers
                // .AddHttpMessageHandler(serviceProvider =>
                // {
                //     var tokenService = serviceProvider.GetRequiredService<ITokenAcquisitionService>();
                //     var logger = serviceProvider.GetRequiredService<ILogger<BearerTokenHandler>>();
                //     return new BearerTokenHandler(tokenService, logger);
                // });
            }

            return services;
        }
    }
}