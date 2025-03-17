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
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Web;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Microsoft.Health.Fhir.Shared.Web
{
    internal class OpenIddictApplicationCreater : IHostedService
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

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await using var scope = _serviceProvider.CreateAsyncScope();

            var context = scope.ServiceProvider.GetRequiredService<ApplicationAuthDbContext>();
            await context.Database.EnsureCreatedAsync(cancellationToken);

            var developmentIdentityProviderConfiguration = scope.ServiceProvider.GetRequiredService<IOptions<DevelopmentIdentityProviderConfiguration>>();
            if (developmentIdentityProviderConfiguration?.Value?.ClientApplications?.Any() ?? false)
            {
                var applicationManager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
                await RegisterApplicationsAsync(
                    applicationManager,
                    developmentIdentityProviderConfiguration.Value.ClientApplications,
                    cancellationToken);
            }

            var scopeManager = scope.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>();
            await RegisterScopesAsync(
                scopeManager,
                cancellationToken);
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        private Task RegisterApplicationsAsync(
            IOpenIddictApplicationManager applicationManager,
            IList<DevelopmentIdentityProviderApplicationConfiguration> applications,
            CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(applicationManager, nameof(applicationManager));
            EnsureArg.IsNotNull(applications, nameof(applications));

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
                            Permissions =
                            {
                                Permissions.Endpoints.Authorization,
                                Permissions.Endpoints.Token,
                                Permissions.Scopes.Roles,
                            },
                        };

                        foreach (var grantType in DevelopmentIdentityProviderRegistrationExtensions.AllowedGrantTypes)
                        {
                            applicationDescriptor.Permissions.Add($"{Permissions.Prefixes.GrantType}{grantType}");
                        }

                        foreach (var scope in DevelopmentIdentityProviderRegistrationExtensions.AllowedScopes.Concat(DevelopmentIdentityProviderRegistrationExtensions.GenerateSmartClinicalScopes()))
                        {
                            applicationDescriptor.Permissions.Add($"{Permissions.Prefixes.Scope}{scope}");
                        }

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

        private static Task RegisterScopesAsync(
            IOpenIddictScopeManager scopeManager,
            CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(scopeManager, nameof(scopeManager));

            var scopes = DevelopmentIdentityProviderRegistrationExtensions.AllowedScopes
                .Concat(DevelopmentIdentityProviderRegistrationExtensions.GenerateSmartClinicalScopes())
                .ToList();
            scopes.ForEach(
                async scope =>
                {
                    if (await scopeManager.FindByNameAsync(scope) is null)
                    {
                        await scopeManager.CreateAsync(
                            new OpenIddictScopeDescriptor
                            {
                                Name = scope,
                                Resources =
                                {
                                    scope,
                                },
                            },
                            cancellationToken);
                    }
                });

            return Task.CompletedTask;
        }
    }
}
