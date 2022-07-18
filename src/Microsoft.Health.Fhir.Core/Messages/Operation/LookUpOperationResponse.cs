// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Core.Messages.Operation
{
    public class LookUpOperationResponse
    {
        public LookUpOperationResponse(Parameters parameterOutcome)
        {
            EnsureArg.IsNotNull(parameterOutcome, nameof(parameterOutcome));

            ParameterOutcome = parameterOutcome;
        }

        public Parameters ParameterOutcome { get; }
    }
}
