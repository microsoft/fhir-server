// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.CosmosDb.Features.Storage;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.CosmosDb.Exceptions
{
    public class CustomerManagedKeyException : FhirException
    {
        public CustomerManagedKeyException(int? subStatusCode)
        {
            string errorMessage = Resources.CmkDefaultError;
            if (subStatusCode.HasValue && Enum.IsDefined(typeof(KnownCosmosDbCmkSubStatusValue), subStatusCode))
            {
                switch ((KnownCosmosDbCmkSubStatusValue)subStatusCode.Value)
                {
                    case KnownCosmosDbCmkSubStatusValue.AadClientCredentialsGrantFailure:
                        errorMessage = Resources.AadClientCredentialsGrantFailure;
                        break;
                    case KnownCosmosDbCmkSubStatusValue.AadServiceUnavailable:
                        errorMessage = Resources.AadServiceUnavailable;
                        break;
                    case KnownCosmosDbCmkSubStatusValue.KeyVaultAuthenticationFailure:
                        errorMessage = Resources.KeyVaultAuthenticationFailure;
                        break;
                    case KnownCosmosDbCmkSubStatusValue.KeyVaultKeyNotFound:
                        errorMessage = Resources.KeyVaultKeyNotFound;
                        break;
                    case KnownCosmosDbCmkSubStatusValue.KeyVaultServiceUnavailable:
                        errorMessage = Resources.KeyVaultServiceUnavailable;
                        break;
                    case KnownCosmosDbCmkSubStatusValue.KeyVaultWrapUnwrapFailure:
                        errorMessage = Resources.KeyVaultWrapUnwrapFailure;
                        break;
                    case KnownCosmosDbCmkSubStatusValue.InvalidKeyVaultKeyUri:
                        errorMessage = Resources.InvalidKeyVaultKeyUri;
                        break;
                    case KnownCosmosDbCmkSubStatusValue.InvalidInputBytes:
                        errorMessage = Resources.InvalidInputBytes;
                        break;
                    case KnownCosmosDbCmkSubStatusValue.KeyVaultInternalServerError:
                        errorMessage = Resources.KeyVaultInternalServerError;
                        break;
                    case KnownCosmosDbCmkSubStatusValue.KeyVaultDnsNotResolved:
                        errorMessage = Resources.KeyVaultDnsNotResolved;
                        break;
                }
            }

            Issues.Add(new OperationOutcomeIssue(
                    OperationOutcomeConstants.IssueSeverity.Error,
                    OperationOutcomeConstants.IssueType.Forbidden,
                    errorMessage));
        }
    }
}
