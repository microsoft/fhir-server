// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Medino;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Resources.Delete;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.Fhir.Core.Registration;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Resources.Delete
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DomainLogicValidation)]
    public class ConditionalDeleteResourceHandlerTests
    {
        [Fact]
        public async Task GivenMultipleDeleteFailsWithPartialResults_WhenHandled_ThenItemsDeletedHeaderContainsTotal()
        {
            // Arrange
            var authorizationService = Substitute.For<IAuthorizationService<DataActions>>();
            authorizationService.CheckAccess(Arg.Any<DataActions>(), Arg.Any<CancellationToken>()).Returns(call => call.Arg<DataActions>());

            var deleter = Substitute.For<IDeletionService>();
            var partialResults = new Dictionary<string, long>
            {
                { "Patient", 2 },
                { "Observation", 3 },
            };
            var incompleteOperationException = new IncompleteOperationException<IDictionary<string, long>>(
                new InvalidOperationException("Delete failed after partial completion."),
                partialResults);
            deleter.DeleteMultipleAsync(
                Arg.Any<ConditionalDeleteResourceRequest>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<IList<string>>()).Returns<Task<IDictionary<string, long>>>(_ => throw incompleteOperationException);

            var contextAccessor = new FhirRequestContextAccessor
            {
                RequestContext = new FhirRequestContext(
                    "DELETE",
                    "https://localhost/Patient",
                    "https://localhost/",
                    Guid.NewGuid().ToString(),
                    new Dictionary<string, StringValues>(),
                    new Dictionary<string, StringValues>()),
            };

            var handler = new ConditionalDeleteResourceHandler(
                Substitute.For<IFhirDataStore>(),
                new Lazy<IConformanceProvider>(() => Substitute.For<IConformanceProvider>()),
                Substitute.For<IResourceWrapperFactory>(),
                Substitute.For<ISearchService>(),
                Substitute.For<IMediator>(),
                new ResourceIdProvider(),
                authorizationService,
                deleter,
                contextAccessor,
                Options.Create(new CoreFeatureConfiguration()),
                Substitute.For<ILogger<ConditionalDeleteResourceHandler>>());

            var request = new ConditionalDeleteResourceRequest(
                "Patient",
                new List<Tuple<string, string>> { Tuple.Create("active", "false") },
                DeleteOperation.HardDelete,
                maxDeleteCount: 100);

            // Act
            await Assert.ThrowsAsync<IncompleteOperationException<IDictionary<string, long>>>(
                () => handler.HandleAsync(request, CancellationToken.None));

            // Assert
            Assert.Equal("5", contextAccessor.RequestContext.ResponseHeaders[KnownHeaders.ItemsDeleted]);
        }
    }
}
