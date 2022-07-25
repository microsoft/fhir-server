// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Core.Messages.Operation
{
    public class ExpandOperationResponse
    {
        public ExpandOperationResponse(Resource valueSetOutcome)
        {
            EnsureArg.IsNotNull(valueSetOutcome, nameof(valueSetOutcome));

            ValueSetOutcome = valueSetOutcome;
        }

        public Resource ValueSetOutcome { get; }
    }
}
