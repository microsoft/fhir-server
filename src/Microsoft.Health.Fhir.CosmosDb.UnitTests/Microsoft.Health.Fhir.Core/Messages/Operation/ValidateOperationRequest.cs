// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using MediatR;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Messages.Operation
{
    public class ValidateOperationRequest : IRequest<ValidateOperationResponse>, IRequest
    {
        public ValidateOperationRequest(ResourceElement resourceElement, Uri profile)
        {
            EnsureArg.IsNotNull(resourceElement, nameof(resourceElement));

            Resource = resourceElement;
            Profile = profile;
        }

        public ResourceElement Resource { get; }

        public Uri Profile { get; }
    }
}
