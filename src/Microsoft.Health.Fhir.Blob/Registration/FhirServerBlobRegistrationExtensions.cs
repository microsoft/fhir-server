// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Blob.Configs;
using Microsoft.Health.Blob.Features.Health;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Blob.Features.Common;
using Microsoft.Health.Fhir.Blob.Features.Health;
using Microsoft.Health.Fhir.Blob.Features.Storage;
using Microsoft.Health.Fhir.Core.Registration;

namespace Microsoft.Health.Fhir.Blob.Registration;

public static class FhirServerBlobRegistrationExtensions
{
    public static IFhirServerBuilder AddBlobStore(this IFhirServerBuilder fhirServerBuilder, IConfiguration configuration)
    {
        EnsureArg.IsNotNull(fhirServerBuilder, nameof(fhirServerBuilder));
        EnsureArg.IsNotNull(configuration, nameof(configuration));
        IServiceCollection services = fhirServerBuilder.Services;

        var config = new BlobStoreConfigurationSection();

        IConfigurationSection blobConfig = configuration.GetSection(BlobServiceClientOptions.DefaultSectionName);
        services
            .AddOptions<BlobOperationOptions>()
            .Bind(blobConfig.GetSection(nameof(BlobServiceClientOptions.Operations)));

        services.AddSingleton<BlobStoreConfigurationSection>()
            .AddBlobContainerInitialization(x => blobConfig
                .GetSection(BlobInitializerOptions.DefaultSectionName)
                .Bind(x))
            .ConfigureContainer(config.ContainerConfigurationName, x => configuration
                .GetSection(config.ConfigurationSectionName)
                .Bind(x));

        services.Add<BlobStoreClient>()
            .Singleton()
            .AsSelf()
            .AsImplementedInterfaces();

        services
            .AddBlobServiceClient(blobConfig)
            .Add<BlobRawResourceStore>()
                .Scoped()
                .AsSelf()
                .AsImplementedInterfaces();

        services.AddBlobContainerInitialization(x => blobConfig
                .GetSection(BlobInitializerOptions.DefaultSectionName)
                .Bind(x));

        services
            .AddBlobHealthCheck<FhirBlobHealthCheck>("FhirBlobHealthCheck");

        return fhirServerBuilder;
    }

    internal static IServiceCollection AddBlobHealthCheck<TBlobHealthCheck>(this IServiceCollection services, string name)
        where TBlobHealthCheck : BlobHealthCheck
    {
        services
            .AddHealthChecks()
            .AddCheck<TBlobHealthCheck>(name: name);

        return services;
    }
}
