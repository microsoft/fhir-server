// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using Microsoft.Azure.Documents.Client;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Metrics
{
    public interface ICosmosMetricProcessor
    {
        void ProcessResponse<T>(T resourceResponseBase)
            where T : ResourceResponseBase;

        void ProcessResponse<T>(FeedResponse<T> feedResponse);

        void ProcessResponse<T>(StoredProcedureResponse<T> storedProcedureResponse);

        void ProcessResponse(string sessionToken, double responseRequestCharge, long? collectionSizeUsageKilobytes, HttpStatusCode? statusCode);
    }
}
