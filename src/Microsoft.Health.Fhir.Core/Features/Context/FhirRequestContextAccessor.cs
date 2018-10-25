// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;

namespace Microsoft.Health.Fhir.Core.Features.Context
{
    public class FhirRequestContextAccessor : IFhirRequestContextAccessor
    {
        private readonly AsyncLocal<IFhirRequestContext> _fhirRequestContextCurrent = new AsyncLocal<IFhirRequestContext>();

        public IFhirRequestContext FhirRequestContext
        {
            get => _fhirRequestContextCurrent.Value;

            set => _fhirRequestContextCurrent.Value = value;
        }
    }
}
