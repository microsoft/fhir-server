// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.Options;
using Microsoft.Health.ControlPlane.Core.Configs;
using Microsoft.Health.ControlPlane.Core.Features.Rbac;
using Microsoft.Health.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.ControlPlane.Core.UnitTests.Features.Rbac
{
    public class RbacServiceBootstrapperTests
    {
        private readonly IRbacService _rbacServices = Substitute.For<IRbacService>();
        private readonly IOptions<ControlPlaneConfiguration> _controlPlaneConfigurationOptions = Substitute.For<IOptions<ControlPlaneConfiguration>>();
        private readonly ControlPlaneConfiguration _controlPlaneConfiguration = Substitute.For<ControlPlaneConfiguration>();
        private readonly RbacServiceBootstrapper _rbacServiceBootstrapper;

        public RbacServiceBootstrapperTests()
        {
            var owned = Substitute.For<IScoped<IRbacService>>();
            owned.Value.Returns(_rbacServices);

            _controlPlaneConfigurationOptions.Value.Returns(_controlPlaneConfiguration);

            _rbacServiceBootstrapper = new RbacServiceBootstrapper(() => owned, _controlPlaneConfigurationOptions);
        }

        [Fact]
        public void WhenBootstrapping_GivenABootstrapSection_ThenIsBootstrappedIsCalled()
        {
            var identityProvider = new IdentityProvider("test", "https://localhost", new List<string> { "fhir-api" }, null);
            _controlPlaneConfiguration.Bootstrap = new Bootstrap
            {
                IdentityProviders = new List<IdentityProvider>
                {
                    identityProvider,
                },
            };

            _rbacServiceBootstrapper.Start();

            _rbacServices.Received(1).IsBootstrappedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
            _rbacServices.Received(1).UpsertIdentityProviderAsync(identityProvider, Arg.Any<CancellationToken>());
        }

        [Fact]
        public void WhenBootstrapping_GivenAnEmptyIdentityProviderBootstrapSection_ThenIsBootstrappedIsCalledAndIdentityProviderUpsertIsNotBeCalled()
        {
            _rbacServiceBootstrapper.Start();

            _rbacServices.Received(1).IsBootstrappedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
            _rbacServices.DidNotReceiveWithAnyArgs().UpsertIdentityProviderAsync(Arg.Any<IdentityProvider>(), Arg.Any<CancellationToken>());
        }
    }
}
