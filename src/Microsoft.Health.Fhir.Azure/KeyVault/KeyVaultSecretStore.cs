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
        private static readonly HttpStatusCode[] _defaultStatusCodesToRetry =
        {
            HttpStatusCode.RequestTimeout,
            HttpStatusCode.ServiceUnavailable,
        };

        private readonly AsyncRetryPolicy _retryPolicy;

        public KeyVaultSecretStore(IKeyVaultClient keyVaultClient, Uri keyVaultUri)
            : this(keyVaultClient, keyVaultUri, DefaultRetryCount, DefaultSleepDurationProvider, _defaultStatusCodesToRetry)
        {
        }

        public KeyVaultSecretStore(IKeyVaultClient keyVaultClient, Uri keyVaultUri, int retryCount, Func<int, TimeSpan> sleepDurationProvider)
            : this(keyVaultClient, keyVaultUri, retryCount, sleepDurationProvider, _defaultStatusCodesToRetry)
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
            EnsureArg.IsGt(statusCodesToRetry.Length, 0, nameof(statusCodesToRetry));

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
                result = await _retryPolicy.ExecuteAsync(async () =>
                {
                    return await _keyVaultClient.GetSecretAsync(_keyVaultUri.AbsoluteUri, secretName, cancellationToken);
                });
            }
            catch (Exception ex)
            {
                throw new SecretStoreException(SecretStoreErrors.GetSecretError, ex);
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
                result = await _retryPolicy.ExecuteAsync(async () =>
                {
                    return await _keyVaultClient.SetSecretAsync(
                        _keyVaultUri.AbsoluteUri,
                        secretName,
                        secretValue,
                        tags: null,
                        contentType: null,
                        secretAttributes: null,
                        cancellationToken);
                });
            }
            catch (Exception ex)
            {
                throw new SecretStoreException(SecretStoreErrors.SetSecretError, ex);
            }

            return new SecretWrapper(result.Id, result.Value);
        }

        public async Task<SecretWrapper> DeleteSecretAsync(string secretName, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNullOrWhiteSpace(secretName, nameof(secretName));

            DeletedSecretBundle result;
            try
            {
                result = await _retryPolicy.ExecuteAsync(async () =>
                {
                    return await _keyVaultClient.DeleteSecretAsync(_keyVaultUri.AbsoluteUri, secretName, cancellationToken);
                });
            }
            catch (Exception ex)
            {
                throw new SecretStoreException(SecretStoreErrors.DeleteSecretError, ex);
            }

            return new SecretWrapper(result.Id, result.Value);
        }
    }
}
