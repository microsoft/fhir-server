// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Medino;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Api.Modules;
using Microsoft.Health.Fhir.Core.Features.Operations.BulkUpdate.Handlers;
using Microsoft.Health.Fhir.Core.Features.Operations.BulkUpdate.Messages;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Modules
{
    /// <summary>
    /// Tests that BulkUpdate handlers are properly registered in the DI container
    /// when using Medino, ensuring they can be resolved and executed via the Medino pipeline.
    /// </summary>
    [Trait("OwningTeam", "@microsoft/health/fhir")]
    [Trait("Category", "Unit")]
    public class BulkUpdateHandlerRegistrationTests
    {
        /// <summary>
        /// Verifies that CreateBulkUpdateHandler is registered as IRequestHandler for CreateBulkUpdateRequest.
        /// </summary>
        [Fact]
        public void CreateBulkUpdateHandler_IsRegistered_AsBulkUpdateRequestHandler()
        {
            // Arrange
            var services = new ServiceCollection();
            var module = new MediationModule();
            module.Load(services);

            var fhirModule = new FhirModule();
            fhirModule.Load(services);

            var serviceProvider = services.BuildServiceProvider();

            // Act
            var handler = serviceProvider.GetService(typeof(IRequestHandler<CreateBulkUpdateRequest, CreateBulkUpdateResponse>));

            // Assert
            Assert.NotNull(handler);
            Assert.IsType<CreateBulkUpdateHandler>(handler);
        }

        /// <summary>
        /// Verifies that GetBulkUpdateHandler is registered as IRequestHandler for GetBulkUpdateRequest.
        /// </summary>
        [Fact]
        public void GetBulkUpdateHandler_IsRegistered_AsBulkUpdateStatusHandler()
        {
            // Arrange
            var services = new ServiceCollection();
            var module = new MediationModule();
            module.Load(services);

            var fhirModule = new FhirModule();
            fhirModule.Load(services);

            var serviceProvider = services.BuildServiceProvider();

            // Act
            var handler = serviceProvider.GetService(typeof(IRequestHandler<GetBulkUpdateRequest, GetBulkUpdateResponse>));

            // Assert
            Assert.NotNull(handler);
            Assert.IsType<GetBulkUpdateHandler>(handler);
        }

        /// <summary>
        /// Verifies that CancelBulkUpdateHandler is registered as IRequestHandler for CancelBulkUpdateRequest.
        /// </summary>
        [Fact]
        public void CancelBulkUpdateHandler_IsRegistered_AsCancelBulkUpdateHandler()
        {
            // Arrange
            var services = new ServiceCollection();
            var module = new MediationModule();
            module.Load(services);

            var fhirModule = new FhirModule();
            fhirModule.Load(services);

            var serviceProvider = services.BuildServiceProvider();

            // Act
            var handler = serviceProvider.GetService(typeof(IRequestHandler<CancelBulkUpdateRequest, CancelBulkUpdateResponse>));

            // Assert
            Assert.NotNull(handler);
            Assert.IsType<CancelBulkUpdateHandler>(handler);
        }

        /// <summary>
        /// Verifies that all BulkUpdate request handlers are registered in the service collection.
        /// </summary>
        [Fact]
        public void AllBulkUpdateHandlers_AreRegisteredInServiceCollection()
        {
            // Arrange
            var services = new ServiceCollection();
            var module = new MediationModule();
            module.Load(services);

            var fhirModule = new FhirModule();
            fhirModule.Load(services);

            // Act - Count all IRequestHandler service descriptors
            var allHandlerDescriptors = services.Where(sd =>
                sd.ServiceType.IsGenericType &&
                sd.ServiceType.GetGenericTypeDefinition() == typeof(IRequestHandler<,>)).ToList();

            // Assert - should have at least the BulkUpdate handlers plus other handlers
            Assert.NotEmpty(allHandlerDescriptors);

            // Verify each BulkUpdate handler is explicitly in the list
            var createBulkUpdateDescriptor = allHandlerDescriptors.FirstOrDefault(sd =>
                sd.ServiceType == typeof(IRequestHandler<CreateBulkUpdateRequest, CreateBulkUpdateResponse>));
            Assert.NotNull(createBulkUpdateDescriptor);

            var getBulkUpdateDescriptor = allHandlerDescriptors.FirstOrDefault(sd =>
                sd.ServiceType == typeof(IRequestHandler<GetBulkUpdateRequest, GetBulkUpdateResponse>));
            Assert.NotNull(getBulkUpdateDescriptor);

            var cancelBulkUpdateDescriptor = allHandlerDescriptors.FirstOrDefault(sd =>
                sd.ServiceType == typeof(IRequestHandler<CancelBulkUpdateRequest, CancelBulkUpdateResponse>));
            Assert.NotNull(cancelBulkUpdateDescriptor);
        }
    }
}
