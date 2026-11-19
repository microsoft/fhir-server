// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.AspNetCore.Routing;
using static Hl7.Fhir.Model.Bundle;

namespace Microsoft.Health.Fhir.Api.Features.Resources.Bundle
{
    internal class BundleHandlerInternalOperationExecutionContext
    {
        public BundleHandlerInternalOperationExecutionContext(HTTPVerb httpVerb, string resourceType, RouteContext context, int index, string persistedId, bool isConditionalOperation, long resourceSizeInBytes = 0)
        {
            HttpVerb = httpVerb;
            ResourceType = resourceType; // Resource type can be null in case HTTP GET is used.
            Context = context;
            Index = index;
            PersistedId = persistedId; // PersistedId is only generated in case of POST requests in a transaction bundle, when the entry full URL is provided. It can be null.
            IsConditionalOperation = isConditionalOperation;
            ResourceSizeInBytes = resourceSizeInBytes;
        }

        public HTTPVerb HttpVerb { get; private set; }

        public string ResourceType { get; private set; }

        public RouteContext Context { get; private set; }

        public int Index { get; private set; }

        public string PersistedId { get; private set; }

        public bool IsConditionalOperation { get; private set; }

        public long ResourceSizeInBytes { get; private set; }
    }
}
