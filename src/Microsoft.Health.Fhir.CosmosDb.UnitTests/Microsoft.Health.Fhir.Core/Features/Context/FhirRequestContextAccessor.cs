// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using Microsoft.Health.Core.Features.Context;

namespace Microsoft.Health.Fhir.Core.Features.Context
{
    public class FhirRequestContextAccessor : RequestContextAccessor<IFhirRequestContext>
    {
        private readonly AsyncLocal<IFhirRequestContext> _fhirRequestContextCurrent = new AsyncLocal<IFhirRequestContext>();

        public override IFhirRequestContext RequestContext
        {
            get => _fhirRequestContextCurrent.Value;

            set => _fhirRequestContextCurrent.Value = value;
        }
    }
}
