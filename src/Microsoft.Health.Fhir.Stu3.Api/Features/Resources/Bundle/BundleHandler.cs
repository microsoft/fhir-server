// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Routing;

namespace Microsoft.Health.Fhir.Api.Features.Resources.Bundle
{
    /// <summary>
    /// FHIR STU3 specific portions of the bundle handler.
    /// </summary>
    public partial class BundleHandler
    {
        private Dictionary<Hl7.Fhir.Model.Bundle.HTTPVerb, List<RouteContext>> GenerateRequestDictionary()
        {
            return new Dictionary<Hl7.Fhir.Model.Bundle.HTTPVerb, List<RouteContext>>
            {
                { Hl7.Fhir.Model.Bundle.HTTPVerb.DELETE, new List<RouteContext>() },
                { Hl7.Fhir.Model.Bundle.HTTPVerb.GET, new List<RouteContext>() },
                { Hl7.Fhir.Model.Bundle.HTTPVerb.POST, new List<RouteContext>() },
                { Hl7.Fhir.Model.Bundle.HTTPVerb.PUT, new List<RouteContext>() },
            };
        }

        private async Task ExecuteAllRequests(Hl7.Fhir.Model.Bundle responseBundle)
        {
            await ExecuteRequests(responseBundle, Hl7.Fhir.Model.Bundle.HTTPVerb.DELETE);
            await ExecuteRequests(responseBundle, Hl7.Fhir.Model.Bundle.HTTPVerb.POST);
            await ExecuteRequests(responseBundle, Hl7.Fhir.Model.Bundle.HTTPVerb.PUT);
            await ExecuteRequests(responseBundle, Hl7.Fhir.Model.Bundle.HTTPVerb.GET);
        }
    }
}
