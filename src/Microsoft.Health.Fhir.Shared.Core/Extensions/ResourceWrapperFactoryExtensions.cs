// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Model;
using Microsoft.Health.Core;
using Microsoft.Health.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;

namespace Microsoft.Health.Fhir.Core.Extensions
{
    public static class ResourceWrapperFactoryExtensions
    {
        public static ResourceWrapper CreateResourceWrapper(this IResourceWrapperFactory factory, Resource resource, bool deleted, bool keepMeta)
        {
            if (string.IsNullOrEmpty(resource.Id))
            {
                resource.Id = factory.ResourceIdProvider.Create();
            }

            if (resource.Meta == null)
            {
                resource.Meta = new Meta();
            }

            // store with millisecond precision
            resource.Meta.LastUpdated = Clock.UtcNow.UtcDateTime.TruncateToMillisecond();

            return factory.Create(resource.ToResourceElement(), deleted, keepMeta);
        }
    }
}
