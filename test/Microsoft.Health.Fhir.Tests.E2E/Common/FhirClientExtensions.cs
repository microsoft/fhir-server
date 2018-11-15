// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Common
{
    public static class FhirClientExtensions
    {
        public static async Task DeleteAllResources(this FhirClient client, ResourceType resourceType)
        {
            await DeleteAllResources(client, resourceType, null);
        }

        public static async Task DeleteAllResources(this FhirClient client, ResourceType resourceType, string searchUrl)
        {
            while (true)
            {
                Bundle bundle = await client.SearchAsync(resourceType, searchUrl, count: 100);

                if (!bundle.Entry.Any())
                {
                    break;
                }

                foreach (Bundle.EntryComponent entry in bundle.Entry)
                {
                    await client.DeleteAsync(entry.FullUrl);
                }
            }
        }

        public static async Task<TResource[]> CreateResourcesAsync<TResource>(this FhirClient client, int count)
           where TResource : Resource, new()
        {
            TResource[] resources = new TResource[count];

            for (int i = 0; i < resources.Length; i++)
            {
                TResource resource = new TResource();

                resources[i] = await client.CreateAsync(resource);
            }

            return resources;
        }

        public static async Task<TResource> CreateResourcesAsync<TResource>(this FhirClient client, Func<TResource> resourceFactory)
            where TResource : Resource
        {
            TResource resource = resourceFactory();

            return await client.CreateAsync(resource);
        }

        public static async Task<TResource[]> CreateResourcesAsync<TResource>(this FhirClient client, params Action<TResource>[] resourceCustomizer)
            where TResource : Resource, new()
        {
            TResource[] resources = new TResource[resourceCustomizer.Length];

            for (int i = 0; i < resources.Length; i++)
            {
                TResource resource = new TResource();

                resourceCustomizer[i](resource);

                resources[i] = await client.CreateAsync(resource);
            }

            return resources;
        }
    }
}
