// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Persistence;

namespace Microsoft.Health.Fhir.Core.Features.Resources
{
    public abstract class BaseResourceHandler : IProvideCapability
    {
        private readonly IResourceWrapperFactory _resourceWrapperFactory;

        protected BaseResourceHandler(
            IDataStore dataStore,
            Lazy<IConformanceProvider> conformanceProvider,
            IResourceWrapperFactory resourceWrapperFactory)
        {
            EnsureArg.IsNotNull(dataStore, nameof(dataStore));
            EnsureArg.IsNotNull(conformanceProvider, nameof(conformanceProvider));
            EnsureArg.IsNotNull(resourceWrapperFactory, nameof(resourceWrapperFactory));

            ConformanceProvider = conformanceProvider;
            DataStore = dataStore;
            _resourceWrapperFactory = resourceWrapperFactory;
        }

        protected Lazy<IConformanceProvider> ConformanceProvider { get; }

        protected IDataStore DataStore { get; }

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

            resource.Meta.LastUpdated = Clock.UtcNow;

            ResourceWrapper resourceWrapper = _resourceWrapperFactory.Create(resource, deleted);

            return resourceWrapper;
        }

        public void Build(ListedCapabilityStatement statement)
        {
            foreach (var resource in ModelInfo.SupportedResources)
            {
                var resourceType = (ResourceType)Enum.Parse(typeof(ResourceType), resource);
                AddResourceCapability(statement, resourceType);
            }
        }

        protected virtual void AddResourceCapability(ListedCapabilityStatement statement, ResourceType resourceType)
        {
        }
    }
}
