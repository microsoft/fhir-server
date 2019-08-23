// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.KeyVault;
using Microsoft.Azure.KeyVault.Models;
using Microsoft.Health.Fhir.Azure.KeyVault;
using Microsoft.Health.Fhir.Core.Features.SecretStore;
using Microsoft.Rest;
using Microsoft.Rest.Azure;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Azure.UnitTests.KeyVault
{
    public class KeyVaultSecretStoreTests
    {
        // Note on testing/mocking methodology:
        // The KeyVaultSecretStore class uses some extension methods of Azure KeyVaultClient for
        // managing the secrets. In order to unit test that, we are mocking the actual methods
        // that the extension methods are calling (the code is open source).
        // This is to avoid adding the overhead for mocking extension methods.

        private const string SecretName = "secretName";
        private const string SecretValue = "secretValue";
        private const string ResponseContent = "responseContent";

        private int _retryCount = 0;
        private CancellationToken _cancellationToken = CancellationToken.None;
        private Func<int, TimeSpan> _sleepDurationProvider = new Func<int, TimeSpan>(retryCount => TimeSpan.FromSeconds(0));
        private HttpStatusCode[] _statusCodesToRetry =
        {
            HttpStatusCode.RequestTimeout,
            HttpStatusCode.ServiceUnavailable,
        };

        private IKeyVaultClient _kvClient = Substitute.For<IKeyVaultClient>();
        private Uri _keyVaultUri = new Uri("https://localhost/keyVaultUri");

        private KeyVaultSecretStore _secretStore;

        [Theory]
        [InlineData(HttpStatusCode.InternalServerError)]
        [InlineData(HttpStatusCode.Unauthorized)]
        [InlineData(HttpStatusCode.BadRequest)]
        [InlineData(HttpStatusCode.Ambiguous)]
        public async Task GivenKeyVaultSecretStore_WhenGettingSecretThrowsKeyVaultErrorExceptionWithNonRetriableStatusCode_ThenItWillNotBeRetried(HttpStatusCode statusCode)
        {
            _retryCount = 3;
            _secretStore = GetSecretStore(_retryCount);

            AzureOperationResponse<SecretBundle> successfulResult = GetSuccessfulResult();
            var exception = GetKeyVaultError(statusCode);

            _kvClient.GetSecretWithHttpMessagesAsync(_keyVaultUri.AbsoluteUri, SecretName, secretVersion: string.Empty, customHeaders: null, _cancellationToken)
                .Returns<AzureOperationResponse<SecretBundle>>(
                    _ => throw exception,
                    _ => successfulResult);

            await Assert.ThrowsAsync<SecretStoreException>(() => _secretStore.GetSecretAsync(SecretName, _cancellationToken));
        }

        [Theory]
        [InlineData(HttpStatusCode.ServiceUnavailable)]
        [InlineData(HttpStatusCode.RequestTimeout)]
        public async Task GivenKeyVaultSecretStore_WhenGettingSecretThrowsKeyVaultErrorExceptionWithRetriableStatusCode_ThenItWillBeRetried(HttpStatusCode statusCode)
        {
            _retryCount = 3;
            _secretStore = GetSecretStore(_retryCount);

            var successfulResult = GetSuccessfulResult();
            KeyVaultErrorException exception = GetKeyVaultError(statusCode);

            _kvClient.GetSecretWithHttpMessagesAsync(_keyVaultUri.AbsoluteUri, SecretName, secretVersion: string.Empty, customHeaders: null, _cancellationToken)
                .Returns<AzureOperationResponse<SecretBundle>>(
                    _ => throw exception,
                    _ => successfulResult);

            var result = await _secretStore.GetSecretAsync(SecretName, _cancellationToken);
            Assert.Equal(SecretValue, result.SecretValue);
        }

        [Fact]
        public async Task GivenKeyVaultSecretStore_WhenGettingSecretThrowsKeyVaultErrorExceptionWithRetriableStatusCodeForMaxRetryCount_ThenExceptionWillBeThrown()
        {
            _retryCount = 1;
            _secretStore = GetSecretStore(_retryCount);

            var successfulResult = GetSuccessfulResult();
            KeyVaultErrorException exception = GetKeyVaultError(HttpStatusCode.ServiceUnavailable);

            _kvClient.GetSecretWithHttpMessagesAsync(_keyVaultUri.AbsoluteUri, SecretName, secretVersion: string.Empty, customHeaders: null, _cancellationToken)
                .Returns<AzureOperationResponse<SecretBundle>>(
                    _ => throw exception,
                    _ => throw exception,
                    _ => successfulResult);

            await Assert.ThrowsAsync<SecretStoreException>(() => _secretStore.GetSecretAsync(SecretName, _cancellationToken));
        }

        [Theory]
        [InlineData(HttpStatusCode.Gone, HttpStatusCode.InternalServerError)]
        [InlineData(HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized)]
        [InlineData(HttpStatusCode.Forbidden, HttpStatusCode.Forbidden)]
        [InlineData(HttpStatusCode.Ambiguous, HttpStatusCode.InternalServerError)]
        [InlineData(HttpStatusCode.ServiceUnavailable, HttpStatusCode.InternalServerError)]
        public async Task GivenKeyVaultSecretStore_WhenGettingSecretThrowsKeyVaultErrorException_ThenExceptionWillBeThrownWithAppropriateStatusCode(HttpStatusCode keyVaultStatusCode, HttpStatusCode expectedStatusCode)
        {
            _retryCount = 0;
            _secretStore = GetSecretStore(_retryCount);
            KeyVaultErrorException exception = GetKeyVaultError(keyVaultStatusCode);

            _kvClient.GetSecretWithHttpMessagesAsync(_keyVaultUri.AbsoluteUri, SecretName, secretVersion: string.Empty, customHeaders: null, _cancellationToken)
                .Returns<AzureOperationResponse<SecretBundle>>(
                    _ => throw exception);

            SecretStoreException sse = await Assert.ThrowsAsync<SecretStoreException>(() => _secretStore.GetSecretAsync(SecretName, _cancellationToken));

            Assert.NotNull(sse);
            Assert.Equal(expectedStatusCode, sse.ResponseStatusCode);
        }

        [Fact]
        public async Task GivenKeyVaultSecretStore_WhenGettingSecretThrowsKeyVaultErrorExceptionWithNoResponse_ThenExceptionWillBeThrownWithInternalServerErrorStatusCode()
        {
            _retryCount = 0;
            _secretStore = GetSecretStore(_retryCount);
            KeyVaultErrorException exception = new KeyVaultErrorException();

            _kvClient.GetSecretWithHttpMessagesAsync(_keyVaultUri.AbsoluteUri, SecretName, secretVersion: string.Empty, customHeaders: null, _cancellationToken)
                .Returns<AzureOperationResponse<SecretBundle>>(
                    _ => throw exception);

            SecretStoreException sse = await Assert.ThrowsAsync<SecretStoreException>(() => _secretStore.GetSecretAsync(SecretName, _cancellationToken));

            Assert.NotNull(sse);
            Assert.Equal(HttpStatusCode.InternalServerError, sse.ResponseStatusCode);
        }

        [Theory]
        [InlineData(HttpStatusCode.InternalServerError)]
        [InlineData(HttpStatusCode.Unauthorized)]
        [InlineData(HttpStatusCode.BadRequest)]
        [InlineData(HttpStatusCode.Ambiguous)]
        public async Task GivenKeyVaultSecretStore_WhenSettingSecretThrowsKeyVaultErrorExceptionWithNonRetriableStatusCode_ThenItWillNotBeRetried(HttpStatusCode statusCode)
        {
            _retryCount = 3;
            _secretStore = GetSecretStore(_retryCount);

            var successfulResult = GetSuccessfulResult();
            KeyVaultErrorException exception = GetKeyVaultError(statusCode);

            _kvClient.SetSecretWithHttpMessagesAsync(_keyVaultUri.AbsoluteUri, SecretName, SecretValue, tags: null, contentType: null, secretAttributes: null, customHeaders: null, _cancellationToken)
                .Returns<AzureOperationResponse<SecretBundle>>(
                    _ => throw exception,
                    _ => successfulResult);

            await Assert.ThrowsAsync<SecretStoreException>(() => _secretStore.SetSecretAsync(SecretName, SecretValue, _cancellationToken));
        }

        [Theory]
        [InlineData(HttpStatusCode.ServiceUnavailable)]
        [InlineData(HttpStatusCode.RequestTimeout)]
        public async Task GivenKeyVaultSecretStore_WhenSettingSecretThrowsKeyVaultErrorExceptionWithRetriableStatusCode_ThenItWillBeRetried(HttpStatusCode statusCode)
        {
            _retryCount = 3;
            _secretStore = GetSecretStore(_retryCount);

            var successfulResult = GetSuccessfulResult();
            KeyVaultErrorException exception = GetKeyVaultError(statusCode);

            _kvClient.SetSecretWithHttpMessagesAsync(_keyVaultUri.AbsoluteUri, SecretName, SecretValue, tags: null, contentType: null, secretAttributes: null, customHeaders: null, _cancellationToken)
                .Returns<AzureOperationResponse<SecretBundle>>(
                    _ => throw exception,
                    _ => throw exception,
                    _ => successfulResult);

            var result = await _secretStore.SetSecretAsync(SecretName, SecretValue, _cancellationToken);
            Assert.Equal(SecretValue, result.SecretValue);
        }

        [Fact]
        public async Task GivenKeyVaultSecretStore_WhenSettingSecretThrowsKeyVaultErrorExceptionWithRetriableStatusCodeForMaxRetryCount_ThenExceptionWillBeThrown()
        {
            _retryCount = 2;
            _secretStore = GetSecretStore(_retryCount);

            var successfulResult = GetSuccessfulResult();
            KeyVaultErrorException exception = GetKeyVaultError(HttpStatusCode.ServiceUnavailable);

            _kvClient.SetSecretWithHttpMessagesAsync(_keyVaultUri.AbsoluteUri, SecretName, SecretValue, tags: null, contentType: null, secretAttributes: null, customHeaders: null, _cancellationToken)
                .Returns<AzureOperationResponse<SecretBundle>>(
                    _ => throw exception,
                    _ => throw exception,
                    _ => throw exception,
                    _ => successfulResult);

            await Assert.ThrowsAsync<SecretStoreException>(() => _secretStore.SetSecretAsync(SecretName, SecretValue, _cancellationToken));
        }

        [Theory]
        [InlineData(HttpStatusCode.Gone, HttpStatusCode.InternalServerError)]
        [InlineData(HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized)]
        [InlineData(HttpStatusCode.Forbidden, HttpStatusCode.Forbidden)]
        [InlineData(HttpStatusCode.Ambiguous, HttpStatusCode.InternalServerError)]
        [InlineData(HttpStatusCode.ServiceUnavailable, HttpStatusCode.InternalServerError)]
        public async Task GivenKeyVaultSecretStore_WhenSettingSecretThrowsKeyVaultErrorException_ThenExceptionWillBeThrownWithAppropriateStatusCode(HttpStatusCode keyVaultStatusCode, HttpStatusCode expectedStatusCode)
        {
            _retryCount = 0;
            _secretStore = GetSecretStore(_retryCount);
            KeyVaultErrorException exception = GetKeyVaultError(keyVaultStatusCode);

            _kvClient.SetSecretWithHttpMessagesAsync(_keyVaultUri.AbsoluteUri, SecretName, SecretValue, tags: null, contentType: null, secretAttributes: null, customHeaders: null, _cancellationToken)
                .Returns<AzureOperationResponse<SecretBundle>>(
                    _ => throw exception);

            SecretStoreException sse = await Assert.ThrowsAsync<SecretStoreException>(() => _secretStore.SetSecretAsync(SecretName, SecretValue, _cancellationToken));

            Assert.NotNull(sse);
            Assert.Equal(expectedStatusCode, sse.ResponseStatusCode);
        }

        [Fact]
        public async Task GivenKeyVaultSecretStore_WhenSettingSecretThrowsKeyVaultErrorExceptionWithNoResponse_ThenExceptionWillBeThrownWithInternalServerErrorStatusCode()
        {
            _retryCount = 0;
            _secretStore = GetSecretStore(_retryCount);
            KeyVaultErrorException exception = new KeyVaultErrorException();

            _kvClient.SetSecretWithHttpMessagesAsync(_keyVaultUri.AbsoluteUri, SecretName, SecretValue, tags: null, contentType: null, secretAttributes: null, customHeaders: null, _cancellationToken)
                .Returns<AzureOperationResponse<SecretBundle>>(
                    _ => throw exception);

            SecretStoreException sse = await Assert.ThrowsAsync<SecretStoreException>(() => _secretStore.SetSecretAsync(SecretName, SecretValue, _cancellationToken));

            Assert.NotNull(sse);
            Assert.Equal(HttpStatusCode.InternalServerError, sse.ResponseStatusCode);
        }

        [Theory]
        [InlineData(HttpStatusCode.InternalServerError)]
        [InlineData(HttpStatusCode.Unauthorized)]
        [InlineData(HttpStatusCode.BadRequest)]
        [InlineData(HttpStatusCode.Ambiguous)]
        public async Task GivenKeyVaultSecretStore_WhenDeletingSecretThrowsKeyVaultErrorExceptionWithNonRetriableStatusCode_ThenItWillNotBeRetried(HttpStatusCode statusCode)
        {
            _retryCount = 3;
            _secretStore = GetSecretStore(_retryCount);

            var successfulResult = GetSuccessfulDeletedResult();
            KeyVaultErrorException exception = GetKeyVaultError(statusCode);

            _kvClient.DeleteSecretWithHttpMessagesAsync(_keyVaultUri.AbsoluteUri, SecretName, customHeaders: null, _cancellationToken)
                .Returns<AzureOperationResponse<DeletedSecretBundle>>(
                    _ => throw exception,
                    _ => successfulResult);

            await Assert.ThrowsAsync<SecretStoreException>(() => _secretStore.DeleteSecretAsync(SecretName, _cancellationToken));
        }

        [Theory]
        [InlineData(HttpStatusCode.ServiceUnavailable)]
        [InlineData(HttpStatusCode.RequestTimeout)]
        public async Task GivenKeyVaultSecretStore_WhenDeletingSecretThrowsKeyVaultErrorExceptionWithRetriableStatusCode_ThenItWillBeRetried(HttpStatusCode statusCode)
        {
            _retryCount = 3;
            _secretStore = GetSecretStore(_retryCount);

            var successfulResult = GetSuccessfulDeletedResult();
            KeyVaultErrorException exception = GetKeyVaultError(statusCode);

            _kvClient.DeleteSecretWithHttpMessagesAsync(_keyVaultUri.AbsoluteUri, SecretName, customHeaders: null, _cancellationToken)
                .Returns<AzureOperationResponse<DeletedSecretBundle>>(
                    _ => throw exception,
                    _ => throw exception,
                    _ => successfulResult);

            var result = await _secretStore.DeleteSecretAsync(SecretName, _cancellationToken);
            Assert.Null(result.SecretValue);
        }

        [Fact]
        public async Task GivenKeyVaultSecretStore_WhenDeletingSecretThrowsKeyVaultErrorExceptionWithRetriableStatusCodeForMaxRetryCount_ThenExceptionWillBeThrown()
        {
            _retryCount = 2;
            _secretStore = GetSecretStore(_retryCount);

            var successfulResult = GetSuccessfulDeletedResult();
            KeyVaultErrorException exception = GetKeyVaultError(HttpStatusCode.ServiceUnavailable);

            _kvClient.DeleteSecretWithHttpMessagesAsync(_keyVaultUri.AbsoluteUri, SecretName, customHeaders: null, _cancellationToken)
                .Returns<AzureOperationResponse<DeletedSecretBundle>>(
                    _ => throw exception,
                    _ => throw exception,
                    _ => throw exception,
                    _ => successfulResult);

            await Assert.ThrowsAsync<SecretStoreException>(() => _secretStore.DeleteSecretAsync(SecretName, _cancellationToken));
        }

        [Theory]
        [InlineData(HttpStatusCode.Gone, HttpStatusCode.InternalServerError)]
        [InlineData(HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized)]
        [InlineData(HttpStatusCode.Forbidden, HttpStatusCode.Forbidden)]
        [InlineData(HttpStatusCode.Ambiguous, HttpStatusCode.InternalServerError)]
        [InlineData(HttpStatusCode.ServiceUnavailable, HttpStatusCode.InternalServerError)]
        public async Task GivenKeyVaultSecretStore_WhenDeletingSecretThrowsKeyVaultErrorException_ThenExceptionWillBeThrownWithAppropriateStatusCode(HttpStatusCode keyVaultStatusCode, HttpStatusCode expectedStatusCode)
        {
            _retryCount = 0;
            _secretStore = GetSecretStore(_retryCount);
            KeyVaultErrorException exception = GetKeyVaultError(keyVaultStatusCode);

            _kvClient.DeleteSecretWithHttpMessagesAsync(_keyVaultUri.AbsoluteUri, SecretName, customHeaders: null, _cancellationToken)
                .Returns<AzureOperationResponse<DeletedSecretBundle>>(
                    _ => throw exception);

            SecretStoreException sse = await Assert.ThrowsAsync<SecretStoreException>(() => _secretStore.DeleteSecretAsync(SecretName, _cancellationToken));

            Assert.NotNull(sse);
            Assert.Equal(expectedStatusCode, sse.ResponseStatusCode);
        }

        [Fact]
        public async Task GivenKeyVaultSecretStore_WhenDeletingSecretThrowsKeyVaultErrorExceptionWithNoResponse_ThenExceptionWillBeThrownWithInternalServerErrorStatusCode()
        {
            _retryCount = 0;
            _secretStore = GetSecretStore(_retryCount);
            KeyVaultErrorException exception = new KeyVaultErrorException();

            _kvClient.DeleteSecretWithHttpMessagesAsync(_keyVaultUri.AbsoluteUri, SecretName, customHeaders: null, _cancellationToken)
                .Returns<AzureOperationResponse<DeletedSecretBundle>>(
                    _ => throw exception);

            SecretStoreException sse = await Assert.ThrowsAsync<SecretStoreException>(() => _secretStore.DeleteSecretAsync(SecretName, _cancellationToken));

            Assert.NotNull(sse);
            Assert.Equal(HttpStatusCode.InternalServerError, sse.ResponseStatusCode);
        }

        // Helper methods
        private KeyVaultSecretStore GetSecretStore(int retryCount)
        {
            return new KeyVaultSecretStore(_kvClient, _keyVaultUri, retryCount, _sleepDurationProvider, _statusCodesToRetry);
        }

        private KeyVaultErrorException GetKeyVaultError(HttpStatusCode statusCode)
        {
            var responseMessage = new HttpResponseMessage(statusCode);

            var exception = new KeyVaultErrorException();
            exception.Response = new HttpResponseMessageWrapper(responseMessage, content: ResponseContent);

            return exception;
        }

        private AzureOperationResponse<SecretBundle> GetSuccessfulResult()
        {
            var secretBundle = new SecretBundle(value: SecretValue, id: SecretName);
            return new AzureOperationResponse<SecretBundle>()
            {
                Body = secretBundle,
            };
        }

        private AzureOperationResponse<DeletedSecretBundle> GetSuccessfulDeletedResult()
        {
            var deletedSecretBundle = new DeletedSecretBundle(value: null, id: SecretName);
            return new AzureOperationResponse<DeletedSecretBundle>()
            {
                Body = deletedSecretBundle,
            };
        }
    }
}
