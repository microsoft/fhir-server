// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Hl7.Fhir.Model;
using MediatR;

namespace Microsoft.Health.Fhir.Core.Messages.Operation
{
    public class ExpandOperationRequest : IRequest<ExpandOperationResponse>, IRequest
    {
        public ExpandOperationRequest(
            Resource valueSet,
            string canonicalURL = null,
            int offset = 0,
            int count = 0)
        {
            ValueSet = valueSet;
            if (!string.IsNullOrEmpty(canonicalURL))
            {
                CanonicalURL = new FhirUri(canonicalURL);
            }

            Offset = offset;
            Count = count;
        }

        public ExpandOperationRequest(Parameters parameter)
        {
            EnsureArg.IsNotNull(parameter, nameof(parameter));

            Parameter = parameter;
        }

        public Parameters Parameter { get; }

        public Resource ValueSet { get; }

        public FhirUri CanonicalURL { get; }

        public int Offset { get; }

        public int Count { get; }
    }
}
