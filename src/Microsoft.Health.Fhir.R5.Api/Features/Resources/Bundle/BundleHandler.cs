// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;

namespace Microsoft.Health.Fhir.Api.Features.Resources.Bundle
{
    /// <summary>
    /// FHIR R5 specific portions of the bundle handler.
    /// </summary>
    public partial class BundleHandler
    {
        private BundleHandler(
            IFhirDataStore fhirDataStore,
            Lazy<IConformanceProvider> conformanceProvider,
            IResourceWrapperFactory resourceWrapperFactory,
            ResourceIdProvider resourceIdProvider,
            IFhirAuthorizationService authorizationService)
            : base(fhirDataStore, conformanceProvider, resourceWrapperFactory, resourceIdProvider, authorizationService)
        {
            _verbExecutionOrder = new[]
            {
                Hl7.Fhir.Model.Bundle.HTTPVerb.DELETE,
                Hl7.Fhir.Model.Bundle.HTTPVerb.POST,
                Hl7.Fhir.Model.Bundle.HTTPVerb.PUT,
                Hl7.Fhir.Model.Bundle.HTTPVerb.PATCH,
                Hl7.Fhir.Model.Bundle.HTTPVerb.GET,
                Hl7.Fhir.Model.Bundle.HTTPVerb.HEAD,
            };
        }
    }
}
