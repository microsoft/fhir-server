// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.OpenIddict.Configuration;
using Microsoft.Health.Fhir.Api.OpenIddict.Data;
using Microsoft.Health.Fhir.Api.OpenIddict.Extensions;
using Microsoft.Health.Fhir.Core.Configs;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Microsoft.Health.Fhir.Api.OpenIddict.Services
{
    public class OpenIddictApplicationCreater : BackgroundService
    {
        private readonly AuthorizationConfiguration _authorizationConfiguration;
        private readonly IServiceProvider _serviceProvider;

        public OpenIddictApplicationCreater(
            AuthorizationConfiguration authorizationConfiguration,
            IServiceProvider serviceProvider)
        {
            EnsureArg.IsNotNull(authorizationConfiguration, nameof(authorizationConfiguration));
            EnsureArg.IsNotNull(serviceProvider, nameof(serviceProvider));

            _authorizationConfiguration = authorizationConfiguration;
            _serviceProvider = serviceProvider;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await using var scope = _serviceProvider.CreateAsyncScope();

            var context = scope.ServiceProvider.GetRequiredService<ApplicationAuthDbContext>();
            await context.Database.EnsureCreatedAsync(stoppingToken);

            var developmentIdentityProviderConfiguration = scope.ServiceProvider.GetRequiredService<IOptions<DevelopmentIdentityProviderConfiguration>>();
            if (developmentIdentityProviderConfiguration?.Value?.ClientApplications?.Any() ?? false)
            {
                var applicationManager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
                await RegisterApplicationsAsync(
                    applicationManager,
                    developmentIdentityProviderConfiguration.Value.ClientApplications,
                    stoppingToken);
            }

            var scopeManager = scope.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>();
            await RegisterScopesAsync(
                scopeManager,
                stoppingToken);
        }

        private Task RegisterApplicationsAsync(
            IOpenIddictApplicationManager applicationManager,
            IList<DevelopmentIdentityProviderApplicationConfiguration> applications,
            CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(applicationManager, nameof(applicationManager));
            EnsureArg.IsNotNull(applications, nameof(applications));

            var permissionsSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            permissionsSet.Add(Permissions.Endpoints.Authorization);
            permissionsSet.Add(Permissions.Endpoints.Token);
            permissionsSet.Add(Permissions.ResponseTypes.Code);
            permissionsSet.Add(Permissions.Scopes.Roles);

            foreach (var grantType in DevelopmentIdentityProviderRegistrationExtensions.AllowedGrantTypes)
            {
                permissionsSet.Add($"{Permissions.Prefixes.GrantType}{grantType}");
            }

            foreach (var scope in DevelopmentIdentityProviderRegistrationExtensions.AllowedScopes.Concat(DevelopmentIdentityProviderRegistrationExtensions.GenerateSmartClinicalScopes()))
            {
                permissionsSet.Add($"{Permissions.Prefixes.Scope}{scope}");
            }

            applications.ToList().ForEach(
                async application =>
                {
                    if (await applicationManager.FindByClientIdAsync(application.Id, cancellationToken) == null)
                    {
                        var applicationDescriptor = new OpenIddictApplicationDescriptor
                        {
                            // TODO: encoding the client secret will cause the token validator to fail, need to investigate why...
                            ClientId = application.Id,
                            ClientSecret = application.Id,
                            RedirectUris = { new Uri("http://localhost") },
                        };

                        applicationDescriptor.Permissions.UnionWith(permissionsSet);

                        foreach (var role in application.Roles)
                        {
                            applicationDescriptor.Permissions.Add($"{_authorizationConfiguration.RolesClaim}:{role}");
                        }

                        await applicationManager.CreateAsync(
                            applicationDescriptor,
                            cancellationToken);
                    }
                });

            return Task.CompletedTask;
        }

        private static async Task RegisterScopesAsync(
            IOpenIddictScopeManager scopeManager,
            CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(scopeManager, nameof(scopeManager));

            // Only register the base allowed scopes, not the 6000+ SMART clinical scopes
            // SMART scopes will be validated dynamically via event handlers
            var baseScopes = DevelopmentIdentityProviderRegistrationExtensions.AllowedScopes.ToList();

            foreach (var scope in baseScopes)
            {
                if (await scopeManager.FindByNameAsync(scope, cancellationToken) is null)
                {
                    await scopeManager.CreateAsync(
                        new OpenIddictScopeDescriptor
                        {
                            Name = scope,
                            Resources = { scope },
                        },
                        cancellationToken);
                }
            }
        }
    }
}
