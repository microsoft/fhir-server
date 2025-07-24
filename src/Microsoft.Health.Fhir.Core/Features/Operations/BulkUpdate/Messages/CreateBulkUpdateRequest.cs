﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using MediatR;

namespace Microsoft.Health.Fhir.Core.Features.Operations.BulkUpdate.Messages
{
    public class CreateBulkUpdateRequest : IRequest<CreateBulkUpdateResponse>
    {
        public CreateBulkUpdateRequest(
            string resourceType,
            IList<Tuple<string, string>> conditionalParameters,
            Hl7.Fhir.Model.Parameters parameters,
            bool isParallel)
        {
            ResourceType = resourceType;
            ConditionalParameters = conditionalParameters;
            Parameters = parameters;
            IsParallel = isParallel;
        }

        public string ResourceType { get; }

        public IList<Tuple<string, string>> ConditionalParameters { get; }

        public Hl7.Fhir.Model.Parameters Parameters { get; }

        public bool IsParallel { get; }
    }
}
