// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Context
{
    public static class FhirUserClaimParser
    {
        public static void ParseFhirUserClaim(
            AccessControlContext accessControlContext,
            bool errorOnInvalidFhirUserClaim)
        {
            EnsureArg.IsNotNull(accessControlContext, nameof(accessControlContext));

            // The fhirUser claim is a URI that points to the FHIR resource that represents the user.
            // The URI is of the form <base URL>/<resource type>/<resource id>.
            // The resource type must be one of Patient, Practitioner.  In the future it may be extended to include RelatedPerson.

            if (accessControlContext.FhirUserClaim == null ||
                accessControlContext.FhirUserClaim.Segments.Length < 2)
            {
                if (errorOnInvalidFhirUserClaim)
                {
                    throw new BadRequestException(Core.Resources.FhirUserClaimInvalidFormat);
                }
                else
                {
                    return;
                }
            }

            string fhirUserId = accessControlContext.FhirUserClaim.Segments.Last().TrimEnd('/');
            string fhirUserResourceType = accessControlContext.FhirUserClaim.Segments[accessControlContext.FhirUserClaim.Segments.Length - 2].TrimEnd('/');

            if (fhirUserResourceType != KnownResourceTypes.Patient &&
                fhirUserResourceType != KnownResourceTypes.Practitioner)
            {
                if (errorOnInvalidFhirUserClaim)
                {
                    throw new BadRequestException(string.Format(Core.Resources.FhirUserClaimMustHaveResourceTypeAndId, accessControlContext.FhirUserClaim.ToString()));
                }
                else
                {
                    return;
                }
            }

            accessControlContext.CompartmentId = fhirUserId;
            accessControlContext.CompartmentResourceType = fhirUserResourceType;
        }
    }
}
