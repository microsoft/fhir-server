// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Persistence;

namespace Microsoft.Health.Fhir.Core.Features.Resources
{
    public abstract class BaseResourceHandler : IProvideCapability
    {
        private readonly IResourceWrapperFactory _resourceWrapperFactory;

        protected BaseResourceHandler(
            IFhirDataStore fhirDataStore,
            Lazy<IConformanceProvider> conformanceProvider,
            IResourceWrapperFactory resourceWrapperFactory)
        {
            EnsureArg.IsNotNull(fhirDataStore, nameof(fhirDataStore));
            EnsureArg.IsNotNull(conformanceProvider, nameof(conformanceProvider));
            EnsureArg.IsNotNull(resourceWrapperFactory, nameof(resourceWrapperFactory));

            ConformanceProvider = conformanceProvider;
            FhirDataStore = fhirDataStore;
            _resourceWrapperFactory = resourceWrapperFactory;
        }

        protected Lazy<IConformanceProvider> ConformanceProvider { get; }

        protected IFhirDataStore FhirDataStore { get; }

        protected ResourceWrapper CreateResourceWrapper(Resource resource, bool deleted)
        {
            if (string.IsNullOrEmpty(resource.Id))
            {
                resource.Id = Guid.NewGuid().ToString();
            }

            if (resource.Meta == null)
            {
                resource.Meta = new Meta();
            }

            // store with millisecond precision
            resource.Meta.LastUpdated = Clock.UtcNow.UtcDateTime.TruncateToMillisecond();

            ResourceWrapper resourceWrapper = _resourceWrapperFactory.Create(resource.ToResourceElement(), deleted);

            return resourceWrapper;
        }

        public void Build(IListedCapabilityStatement statement)
        {
            foreach (var resource in ModelInfo.SupportedResources)
            {
                AddResourceCapability(statement, resource);
            }
        }

        protected virtual void AddResourceCapability(IListedCapabilityStatement statement, string resourceType)
        {
        }
    }
}
