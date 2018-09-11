// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;

namespace Microsoft.Health.Fhir.Core.Features.Context
{
    public class FhirContextAccessor : IFhirContextAccessor
    {
        private readonly AsyncLocal<IFhirContext> _fhirContextCurrent = new AsyncLocal<IFhirContext>();

        public IFhirContext FhirContext
        {
            get => _fhirContextCurrent.Value;

            set => _fhirContextCurrent.Value = value;
        }
    }
}
