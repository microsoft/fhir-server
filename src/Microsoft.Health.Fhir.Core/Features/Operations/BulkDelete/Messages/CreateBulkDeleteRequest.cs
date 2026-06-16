// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Medino;
using Microsoft.Health.Fhir.Core.Messages.Delete;

namespace Microsoft.Health.Fhir.Core.Features.Operations.BulkDelete.Messages
{
    public class CreateBulkDeleteRequest : IRequest<CreateBulkDeleteResponse>
    {
        public CreateBulkDeleteRequest(
            DeleteOperation deleteOperation,
            string resourceType,
            IList<Tuple<string, string>> conditionalParameters,
            bool includeSoftDeleted,
            IList<string> excludedResourceTypes,
            bool removeReferences)
        {
            DeleteOperation = deleteOperation;
            ResourceType = resourceType;
            ConditionalParameters = conditionalParameters;
            IncludeSoftDeleted = includeSoftDeleted;
            ExcludedResourceTypes = excludedResourceTypes;
            RemoveReferences = removeReferences;
        }

        public DeleteOperation DeleteOperation { get; }

        public string ResourceType { get; }

        public IList<Tuple<string, string>> ConditionalParameters { get; }

        public bool IncludeSoftDeleted { get; }

        public IList<string> ExcludedResourceTypes { get; }

        public bool RemoveReferences { get; }
    }
}
