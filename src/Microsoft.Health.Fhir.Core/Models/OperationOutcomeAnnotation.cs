// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Models
{
    public sealed class OperationOutcomeAnnotation
    {
        public OperationOutcomeAnnotation(string code, string detailsText)
        {
            Code = EnsureArg.IsNotNullOrWhiteSpace(code, nameof(code));
            DetailsText = EnsureArg.IsNotNullOrWhiteSpace(detailsText, nameof(detailsText));
        }

        public string Code { get; }

        public string DetailsText { get; }
    }
}
