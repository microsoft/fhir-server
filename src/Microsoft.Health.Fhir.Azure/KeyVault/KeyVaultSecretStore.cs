// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Azure.KeyVault;
using Microsoft.Azure.KeyVault.Models;
using Microsoft.Health.Fhir.Core.Features.SecretStore;
using Polly;
using Polly.Retry;

namespace Microsoft.Health.Fhir.Azure.KeyVault
{
    /// <summary>
    /// Implementation of <see cref="ISecretStore"/> that uses Azure Key Vault underneath.
    /// </summary>
    public class KeyVaultSecretStore : ISecretStore
    {
        private IKeyVaultClient _keyVaultClient;
        private Uri _keyVaultUri;

        private const int DefaultRetryCount = 3;
        private static readonly Func<int, TimeSpan> DefaultSleepDurationProvider = new Func<int, TimeSpan>(retryCount => TimeSpan.FromSeconds(Math.Pow(2, retryCount)));
        private static readonly HttpStatusCode[] DefaultStatusCodesToRetry =
        {
            HttpStatusCode.RequestTimeout,
            HttpStatusCode.ServiceUnavailable,
        };

        private readonly AsyncRetryPolicy _retryPolicy;

        public KeyVaultSecretStore(IKeyVaultClient keyVaultClient, Uri keyVaultUri)
            : this(keyVaultClient, keyVaultUri, DefaultRetryCount, DefaultSleepDurationProvider, DefaultStatusCodesToRetry)
        {
        }

        public KeyVaultSecretStore(
            IKeyVaultClient keyVaultClient,
            Uri keyVaultUri,
            int retryCount,
            Func<int, TimeSpan> sleepDurationProvider,
            HttpStatusCode[] statusCodesToRetry)
        {
            EnsureArg.IsNotNull(keyVaultClient, nameof(keyVaultClient));
            EnsureArg.IsNotNull(keyVaultUri, nameof(keyVaultUri));
            EnsureArg.IsGte(retryCount, 0, nameof(retryCount));
            EnsureArg.IsNotNull(sleepDurationProvider, nameof(sleepDurationProvider));
            EnsureArg.IsNotNull(statusCodesToRetry, nameof(statusCodesToRetry));

            _keyVaultClient = keyVaultClient;
            _keyVaultUri = keyVaultUri;

            _retryPolicy = Policy.Handle<KeyVaultErrorException>(kve => statusCodesToRetry.Contains(kve.Response.StatusCode))
                .WaitAndRetryAsync(retryCount, sleepDurationProvider);
        }

        public async Task<SecretWrapper> GetSecretAsync(string secretName, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNullOrWhiteSpace(secretName);

            SecretBundle result;
            try
            {
                result = await _retryPolicy.ExecuteAsync(() => _keyVaultClient.GetSecretAsync(_keyVaultUri.AbsoluteUri, secretName, cancellationToken));
            }
            catch (KeyVaultErrorException kve)
            {
                HttpStatusCode statusCode = GetResponseStatusCode(kve);
                throw new SecretStoreException(SecretStoreErrors.GetSecretError, kve, statusCode);
            }
            catch (Exception ex)
            {
                throw new SecretStoreException(SecretStoreErrors.GetSecretError, ex, HttpStatusCode.InternalServerError);
            }

            return new SecretWrapper(result.Id, result.Value);
        }

        public async Task<SecretWrapper> SetSecretAsync(string secretName, string secretValue, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNullOrWhiteSpace(secretName, nameof(secretName));
            EnsureArg.IsNotNullOrWhiteSpace(secretValue, nameof(secretValue));

            SecretBundle result;
            try
            {
                result = await _retryPolicy.ExecuteAsync(() =>
                    _keyVaultClient.SetSecretAsync(
                        _keyVaultUri.AbsoluteUri,
                        secretName,
                        secretValue,
                        tags: null,
                        contentType: null,
                        secretAttributes: null,
                        cancellationToken));
            }
            catch (KeyVaultErrorException kve)
            {
                HttpStatusCode statusCode = GetResponseStatusCode(kve);
                throw new SecretStoreException(SecretStoreErrors.SetSecretError, kve, statusCode);
            }
            catch (Exception ex)
            {
                throw new SecretStoreException(SecretStoreErrors.SetSecretError, ex, HttpStatusCode.InternalServerError);
            }

            return new SecretWrapper(result.Id, result.Value);
        }

        public async Task<SecretWrapper> DeleteSecretAsync(string secretName, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNullOrWhiteSpace(secretName, nameof(secretName));

            DeletedSecretBundle result;
            try
            {
                result = await _retryPolicy.ExecuteAsync(() => _keyVaultClient.DeleteSecretAsync(_keyVaultUri.AbsoluteUri, secretName, cancellationToken));
            }
            catch (KeyVaultErrorException kve)
            {
                HttpStatusCode statusCode = GetResponseStatusCode(kve);
                throw new SecretStoreException(SecretStoreErrors.DeleteSecretError, kve, statusCode);
            }
            catch (Exception ex)
            {
                throw new SecretStoreException(SecretStoreErrors.DeleteSecretError, ex, HttpStatusCode.InternalServerError);
            }

            return new SecretWrapper(result.Id, result.Value);
        }

        // Given a KeyVaultErrorException, this will determine what status code we will
        // return to the caller. We will forward status codes related to unauhtorized
        // and return 500 for the rest.
        private HttpStatusCode GetResponseStatusCode(KeyVaultErrorException kve)
        {
            if (kve.Response == null)
            {
                return HttpStatusCode.InternalServerError;
            }

            if (kve.Response.StatusCode == HttpStatusCode.Unauthorized || kve.Response.StatusCode == HttpStatusCode.Forbidden)
            {
                return kve.Response.StatusCode;
            }

            // return 500 by default.
            return HttpStatusCode.InternalServerError;
        }
    }
}
