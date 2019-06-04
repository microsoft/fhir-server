// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

////using System;
////using System.Linq;
////using System.Threading.Tasks;
////using Hl7.Fhir.Model;
////using Task = System.Threading.Tasks.Task;

using System;
using System.Linq;
using System.Threading.Tasks;
using Hl7.Fhir.ElementModel;
using Hl7.FhirPath;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;

namespace Microsoft.Health.Fhir.Tests.E2E.Common
{
    public static class FhirClientExtensions
    {
        public static async Task DeleteAllResources(this ICustomFhirClient client, string resourceType)
        {
            await DeleteAllResources(client, resourceType, null);
        }

        public static async Task DeleteAllResources(this ICustomFhirClient client, string resourceType, string searchUrl)
        {
            while (true)
            {
                ResourceElement bundle = await client.SearchAsync(resourceType, searchUrl, count: 100);

                var bundleEntries = bundle.Select("Resource.entry").ToList();
                if (!bundleEntries.Any())
                {
                    break;
                }

                foreach (ITypedElement entry in bundleEntries)
                {
                    await client.DeleteAsync(entry.Scalar("fullUrl").ToString());
                }
            }
        }

        public static async Task<ResourceElement[]> CreateResourcesAsync(this ICustomFhirClient client, Func<ResourceElement> baseResourceGetter, int count)
        {
            var resources = new ResourceElement[count];

            for (int i = 0; i < resources.Length; i++)
            {
                var resource = baseResourceGetter.Invoke();

                var createResponse = await client.CreateAsync(resource);
                resources[i] = createResponse.Resource;
            }

            return resources;
        }

        public static async Task<ResourceElement> CreateResourcesAsync(this ICustomFhirClient client, Func<ResourceElement> resourceFactory)
        {
            ResourceElement resource = resourceFactory();

            return await client.CreateAsync(resource);
        }

        public static async Task<ResourceElement[]> CreateResourcesAsync(this ICustomFhirClient client, Func<ResourceElement> baseResourceGetter, params Func<ResourceElement, ResourceElement>[] resourceCustomizer)
        {
            var resources = new ResourceElement[resourceCustomizer.Length];

            for (int i = 0; i < resources.Length; i++)
            {
                var resource = baseResourceGetter.Invoke();

                resource = resourceCustomizer[i](resource);

                var resourceResponse = await client.CreateAsync(resource);
                resources[i] = resourceResponse.Resource;
            }

            return resources;
        }
    }
}
