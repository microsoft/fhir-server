// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage
{
    public interface IFhirDataStoreContext
    {
        string DatabaseId { get; }

        string CollectionId { get; }

        Uri CollectionUri { get; }

        int? ContinuationTokenSizeLimitInKb { get; }
    }
}
