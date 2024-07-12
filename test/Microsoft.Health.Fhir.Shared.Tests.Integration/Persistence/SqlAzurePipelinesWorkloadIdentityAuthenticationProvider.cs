// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.Identity.Client;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence;

public class SqlAzurePipelinesWorkloadIdentityAuthenticationProvider : SqlAuthenticationProvider
{
    private AzurePipelinesCredential _azurePipelinesCredential;

    public override async Task<SqlAuthenticationToken> AcquireTokenAsync(SqlAuthenticationParameters parameters)
    {
        string clientId = Environment.GetEnvironmentVariable("AZURESUBSCRIPTION_CLIENT_ID");
        string tenantId = Environment.GetEnvironmentVariable("AZURESUBSCRIPTION_TENANT_ID");
        string serviceConnectionId = Environment.GetEnvironmentVariable("AZURESUBSCRIPTION_SERVICE_CONNECTION_ID");
        string systemAccessToken = Environment.GetEnvironmentVariable("SYSTEM_ACCESSTOKEN");

        if (_azurePipelinesCredential == null)
        {
            _azurePipelinesCredential = new AzurePipelinesCredential(tenantId, clientId, serviceConnectionId, systemAccessToken);
        }

        var tokenContext = new TokenRequestContext(["https://database.windows.net/.default"]);
        var token = await _azurePipelinesCredential.GetTokenAsync(tokenContext, CancellationToken.None);

        return new SqlAuthenticationToken(token.Token, token.ExpiresOn);
    }

    public override bool IsSupported(SqlAuthenticationMethod authenticationMethod)
    {
        if (!authenticationMethod.Equals(SqlAuthenticationMethod.ActiveDirectoryWorkloadIdentity))
        {
            return false;
        }

        string[] variableNames = [
                "AZURESUBSCRIPTION_CLIENT_ID",
                "AZURESUBSCRIPTION_TENANT_ID",
                "AZURESUBSCRIPTION_SERVICE_CONNECTION_ID",
                "SYSTEM_ACCESSTOKEN",
            ];

        foreach (var variableName in variableNames)
        {
            string variableValue = Environment.GetEnvironmentVariable(variableName);
            if (string.IsNullOrEmpty(variableValue))
            {
                return false;
            }
        }

        return true;
    }
}
