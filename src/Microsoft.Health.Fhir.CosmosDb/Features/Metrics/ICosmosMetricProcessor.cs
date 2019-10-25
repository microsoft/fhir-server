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
            where T : IResourceResponseBase;

        void ProcessResponse<T>(IFeedResponse<T> feedResponse);

        void ProcessResponse<T>(IStoredProcedureResponse<T> storedProcedureResponse);

        void ProcessResponse(string sessionToken, double responseRequestCharge, long? collectionSizeUsageKilobytes, HttpStatusCode? statusCode);
    }
}
