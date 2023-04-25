// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Hl7.Fhir.Model;
using Microsoft.Health.Core;
using Microsoft.Health.Core.Extensions;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Security;

namespace Microsoft.Health.Fhir.Core.Features.Resources
{
    public abstract class BaseResourceHandler
    {
        private readonly IResourceWrapperFactory _resourceWrapperFactory;
        private readonly ResourceIdProvider _resourceIdProvider;

        protected BaseResourceHandler(
            IFhirDataStore fhirDataStore,
            Lazy<IConformanceProvider> conformanceProvider,
            IResourceWrapperFactory resourceWrapperFactory,
            ResourceIdProvider resourceIdProvider,
            IAuthorizationService<DataActions> authorizationService)
        {
            EnsureArg.IsNotNull(fhirDataStore, nameof(fhirDataStore));
            EnsureArg.IsNotNull(conformanceProvider, nameof(conformanceProvider));
            EnsureArg.IsNotNull(resourceWrapperFactory, nameof(resourceWrapperFactory));
            EnsureArg.IsNotNull(resourceIdProvider, nameof(resourceIdProvider));
            EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));

            ConformanceProvider = conformanceProvider;
            AuthorizationService = authorizationService;
            FhirDataStore = fhirDataStore;
            _resourceWrapperFactory = resourceWrapperFactory;
            _resourceIdProvider = resourceIdProvider;
        }

        protected Lazy<IConformanceProvider> ConformanceProvider { get; }

        protected IFhirDataStore FhirDataStore { get; }

        protected IAuthorizationService<DataActions> AuthorizationService { get; }

        protected ResourceWrapper CreateResourceWrapper(Resource resource, bool deleted, bool keepMeta)
        {
            if (resource.Meta == null)
            {
                resource.Meta = new Meta();
            }

            if (string.IsNullOrEmpty(resource.Id))
            {
                resource.Id = _resourceIdProvider.Create();
                resource.Meta.LastUpdated = Guid.Parse(resource.Id).SequentialGuidToDateTime();
            }
            else
            {
                // store with millisecond precision
                resource.Meta.LastUpdated = Clock.UtcNow.UtcDateTime.TruncateToMillisecond();
            }

            return _resourceWrapperFactory.Create(resource.ToResourceElement(), deleted, keepMeta);
        }
    }
}
