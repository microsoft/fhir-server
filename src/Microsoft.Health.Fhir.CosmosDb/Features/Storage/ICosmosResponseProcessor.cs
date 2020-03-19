// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Azure.Documents.Client;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage
{
    public interface ICosmosResponseProcessor
    {
        Task ProcessException(Exception ex);

        Task ProcessResponse<T>(T resourceResponseBase)
            where T : IResourceResponseBase;

        Task ProcessResponse<T>(IFeedResponse<T> feedResponse);

        Task ProcessResponse<T>(IStoredProcedureResponse<T> storedProcedureResponse);

        Task ProcessResponse(string sessionToken, double responseRequestCharge, HttpStatusCode? statusCode);
    }
}
