// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    internal static class ResourceIdBoundary
    {
        internal static readonly (ResourceIdProfile Profile, int Length, bool Accepted)[] Cases =
        {
            (ResourceIdProfile.Standard64, 64, true),
            (ResourceIdProfile.Standard64, 65, false),
            (ResourceIdProfile.Standard64, 128, false),
            (ResourceIdProfile.Long128, 64, true),
            (ResourceIdProfile.Long128, 65, true),
            (ResourceIdProfile.Long128, 128, true),
            (ResourceIdProfile.Long128, 129, false),
        };

        internal static string CaseId((ResourceIdProfile Profile, int Length, bool Accepted) c) => $"{c.Profile}_{c.Length}_{(c.Accepted ? "Accept" : "Reject")}";

        internal static async Task AssertAsync(HttpClient client, int length, bool valid)
        {
            string id = Guid.NewGuid().ToString("N") + new string('a', length - 32);
            string path = $"Patient/{id}";
            using var content = new StringContent($$"""{"resourceType":"Patient","id":"{{id}}","active":true}""", Encoding.UTF8, "application/fhir+json");
            bool created = false;
            bool deleted = false;
            try
            {
                using HttpResponseMessage write = await client.PutAsync(path, content);
                created = write.IsSuccessStatusCode;
                using HttpResponseMessage read = await client.GetAsync(path);
                if (valid)
                {
                    Assert.Equal(HttpStatusCode.Created, write.StatusCode);
                    Assert.Equal(HttpStatusCode.OK, read.StatusCode);
                    Assert.Equal(id, JsonNode.Parse(await read.Content.ReadAsStringAsync())["id"].GetValue<string>());

                    using HttpResponseMessage search = await client.GetAsync($"Patient?_id={id}");
                    Assert.Equal(HttpStatusCode.OK, search.StatusCode);
                    JsonNode bundle = JsonNode.Parse(await search.Content.ReadAsStringAsync());
                    Assert.Equal(id, Assert.Single(bundle["entry"].AsArray())["resource"]["id"].GetValue<string>());

                    using HttpResponseMessage delete = await client.DeleteAsync(path);
                    Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
                    deleted = true;
                    using HttpResponseMessage deletedRead = await client.GetAsync(path);
                    Assert.Equal(HttpStatusCode.Gone, deletedRead.StatusCode);
                }
                else
                {
                    Assert.Equal(HttpStatusCode.BadRequest, write.StatusCode);
                    Assert.Equal(HttpStatusCode.BadRequest, read.StatusCode);
                    using HttpResponseMessage delete = await client.DeleteAsync(path);
                    Assert.Equal(HttpStatusCode.BadRequest, delete.StatusCode);
                }
            }
            finally
            {
                if (created && !deleted)
                {
                    using HttpResponseMessage cleanup = await client.DeleteAsync(path);
                    Assert.Equal(HttpStatusCode.NoContent, cleanup.StatusCode);
                }
            }
        }
    }
}
