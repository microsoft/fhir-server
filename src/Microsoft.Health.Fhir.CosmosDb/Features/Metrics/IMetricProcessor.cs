// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using Microsoft.Azure.Documents.Client;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Metrics
{
    public interface IMetricProcessor
    {
        void UpdateFhirRequestContext<T>(T resourceResponseBase)
            where T : ResourceResponseBase;

        void UpdateFhirRequestContext<T>(FeedResponse<T> feedResponse);

        void UpdateFhirRequestContext<T>(StoredProcedureResponse<T> storedProcedureResponse);

        void UpdateFhirRequestContext(string sessionToken, double responseRequestCharge, long? collectionSizeUsageKilobytes, HttpStatusCode? statusCode);
    }
}
