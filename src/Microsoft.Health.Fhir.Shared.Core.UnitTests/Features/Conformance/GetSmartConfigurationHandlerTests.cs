using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Conformance.Models;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Messages.Get;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Conformance
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    public class GetSmartConfigurationHandlerTests
    {
        public GetSmartConfigurationHandlerTests()
        {
        }

        [Fact]
        public async Task GivenASmartConfigurationHandler_WhenSecurityConfigurationNotEnabled_Then401ExceptionThrown()
        {
            var request = new GetSmartConfigurationRequest();

            var securityConfiguration = new SecurityConfiguration();
            securityConfiguration.Authorization.Enabled = false;

            var handler = new GetSmartConfigurationHandler(securityConfiguration, ModelInfoProvider.Instance);

            await Assert.ThrowsAsync<OperationFailedException>(() => handler.Handle(request, CancellationToken.None));
        }

        [Fact]
        public async Task GivenASmartConfigurationHandler_WhenSecurityConfigurationEnabled_ThenSmartConfigurationReturned()
        {
            var request = new GetSmartConfigurationRequest();

            var securityConfiguration = new SecurityConfiguration();
            securityConfiguration.Authorization.Enabled = true;
            securityConfiguration.Authentication.Authority = "http://base.endpoint";

            var handler = new GetSmartConfigurationHandler(securityConfiguration, ModelInfoProvider.Instance);

            GetSmartConfigurationResponse response = await handler.Handle(request, CancellationToken.None);
        }
    }
}
