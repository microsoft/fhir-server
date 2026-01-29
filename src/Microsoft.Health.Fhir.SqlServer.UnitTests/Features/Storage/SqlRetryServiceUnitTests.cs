// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.SqlServer;
using Microsoft.Health.SqlServer.Configs;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Storage
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DataSourceValidation)]
    public class SqlRetryServiceUnitTests
    {
        private readonly ISqlConnectionBuilder _sqlConnectionBuilder;
        private readonly IOptions<SqlServerDataStoreConfiguration> _sqlServerDataStoreConfiguration;
        private readonly IOptions<CoreFeatureConfiguration> _coreFeatureConfiguration;

        public SqlRetryServiceUnitTests()
        {
            _sqlConnectionBuilder = Substitute.For<ISqlConnectionBuilder>();
            _sqlServerDataStoreConfiguration = Options.Create(new SqlServerDataStoreConfiguration
            {
                CommandTimeout = TimeSpan.FromSeconds(300),
            });
            _coreFeatureConfiguration = Options.Create(new CoreFeatureConfiguration());
        }

        [Fact]
        public void Constructor_WithNullConnectionBuilder_ShouldThrowArgumentNullException()
        {
            // Arrange
            var options = Options.Create(new SqlRetryServiceOptions());
            var delegateOptions = new SqlRetryServiceDelegateOptions();

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new SqlRetryService(
                null,
                _sqlServerDataStoreConfiguration,
                options,
                delegateOptions,
                _coreFeatureConfiguration));
        }

        [Fact]
        public void Constructor_WithNullOptions_ShouldThrowArgumentNullException()
        {
            // Arrange
            var delegateOptions = new SqlRetryServiceDelegateOptions();

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new SqlRetryService(
                _sqlConnectionBuilder,
                _sqlServerDataStoreConfiguration,
                null,
                delegateOptions,
                _coreFeatureConfiguration));
        }

        [Fact]
        public void Constructor_WithAddTransientErrors_ShouldAddToSet()
        {
            // Arrange
            var customErrorCode = 99999;
            var options = Options.Create(new SqlRetryServiceOptions());
            options.Value.AddTransientErrors.Add(customErrorCode);
            var delegateOptions = new SqlRetryServiceDelegateOptions();

            // Act
            var service = new SqlRetryService(
                _sqlConnectionBuilder,
                _sqlServerDataStoreConfiguration,
                options,
                delegateOptions,
                _coreFeatureConfiguration);

            // Assert
            var transientErrors = GetPrivateFieldValue<HashSet<int>>(service, "_transientErrors");
            Assert.Contains(customErrorCode, transientErrors);
        }

        [Fact]
        public void Constructor_WithRemoveTransientErrors_ShouldRemoveFromSet()
        {
            // Arrange
            var errorCodeToRemove = 1205;
            var options = Options.Create(new SqlRetryServiceOptions());
            options.Value.RemoveTransientErrors.Add(errorCodeToRemove);
            var delegateOptions = new SqlRetryServiceDelegateOptions();

            // Act
            var service = new SqlRetryService(
                _sqlConnectionBuilder,
                _sqlServerDataStoreConfiguration,
                options,
                delegateOptions,
                _coreFeatureConfiguration);

            // Assert
            var transientErrors = GetPrivateFieldValue<HashSet<int>>(service, "_transientErrors");
            Assert.DoesNotContain(errorCodeToRemove, transientErrors);
        }

        [Fact]
        public void Constructor_WithBothAddAndRemoveTransientErrors_ShouldApplyBoth()
        {
            // Arrange
            var customErrorCode = 99999;
            var errorCodeToRemove = 1205;
            var options = Options.Create(new SqlRetryServiceOptions());
            options.Value.AddTransientErrors.Add(customErrorCode);
            options.Value.RemoveTransientErrors.Add(errorCodeToRemove);
            var delegateOptions = new SqlRetryServiceDelegateOptions();

            // Act
            var service = new SqlRetryService(
                _sqlConnectionBuilder,
                _sqlServerDataStoreConfiguration,
                options,
                delegateOptions,
                _coreFeatureConfiguration);

            // Assert
            var transientErrors = GetPrivateFieldValue<HashSet<int>>(service, "_transientErrors");
            Assert.Contains(customErrorCode, transientErrors);
            Assert.DoesNotContain(errorCodeToRemove, transientErrors);
        }

        [Fact]
        public void DefaultIsExceptionRetriable_WithError121AndHandshakeMessage_ShouldReturnTrue()
        {
            // Arrange
            var sqlEx = CreateSqlException(121, "an error occurred during the pre-login handshake with the server");

            // Act
            var result = InvokeDefaultIsExceptionRetriable(sqlEx);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void DefaultIsExceptionRetriable_WithError121NoHandshakeMessage_ShouldReturnFalse()
        {
            // Arrange
            var sqlEx = CreateSqlException(121, "some other error message");

            // Act
            var result = InvokeDefaultIsExceptionRetriable(sqlEx);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void DefaultIsExceptionRetriable_WithLoginFailedMessage_ShouldReturnTrue()
        {
            // Arrange
            var sqlEx = CreateSqlException(18456, "Login failed for user 'test'");

            // Act
            var result = InvokeDefaultIsExceptionRetriable(sqlEx);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void DefaultIsExceptionRetriable_WithNonSqlException_ShouldReturnFalse()
        {
            // Arrange
            var ex = new InvalidOperationException("Test exception");

            // Act
            var result = InvokeDefaultIsExceptionRetriable(ex);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void DefaultIsExceptionRetriable_WithHandshakeMessageDifferentCase_ShouldReturnTrue()
        {
            // Arrange
            var sqlEx = CreateSqlException(121, "An Error Occurred During The Pre-Login Handshake");

            // Act
            var result = InvokeDefaultIsExceptionRetriable(sqlEx);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void IsRetriable_WithTransientSqlException_ShouldReturnTrue()
        {
            // Arrange
            var options = Options.Create(new SqlRetryServiceOptions());
            options.Value.AddTransientErrors.Add(99999);
            var delegateOptions = new SqlRetryServiceDelegateOptions();
            var service = new SqlRetryService(
                _sqlConnectionBuilder,
                _sqlServerDataStoreConfiguration,
                options,
                delegateOptions,
                _coreFeatureConfiguration);

            var sqlEx = CreateSqlException(99999, "Custom transient error");

            // Act
            var result = InvokeIsRetriable(service, sqlEx);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void IsRetriable_WithDefaultRetriableException_ShouldReturnTrue()
        {
            // Arrange
            var options = Options.Create(new SqlRetryServiceOptions());
            var delegateOptions = new SqlRetryServiceDelegateOptions();
            var service = new SqlRetryService(
                _sqlConnectionBuilder,
                _sqlServerDataStoreConfiguration,
                options,
                delegateOptions,
                _coreFeatureConfiguration);

            var sqlEx = CreateSqlException(121, "an error occurred during the pre-login handshake");

            // Act
            var result = InvokeIsRetriable(service, sqlEx);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void IsRetriable_WithDefaultRetriableOff_ShouldIgnoreDefaultCheck()
        {
            // Arrange
            var options = Options.Create(new SqlRetryServiceOptions());
            var delegateOptions = new SqlRetryServiceDelegateOptions
            {
                DefaultIsExceptionRetriableOff = true,
            };
            var service = new SqlRetryService(
                _sqlConnectionBuilder,
                _sqlServerDataStoreConfiguration,
                options,
                delegateOptions,
                _coreFeatureConfiguration);

            var sqlEx = CreateSqlException(121, "an error occurred during the pre-login handshake");

            // Act
            var result = InvokeIsRetriable(service, sqlEx);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void IsRetriable_WithCustomDelegateReturningTrue_ShouldReturnTrue()
        {
            // Arrange
            var options = Options.Create(new SqlRetryServiceOptions());
            var delegateOptions = new SqlRetryServiceDelegateOptions
            {
                CustomIsExceptionRetriable = ex => ex.Message.Contains("CustomRetriable"),
            };
            var service = new SqlRetryService(
                _sqlConnectionBuilder,
                _sqlServerDataStoreConfiguration,
                options,
                delegateOptions,
                _coreFeatureConfiguration);

            var ex = new InvalidOperationException("CustomRetriable error");

            // Act
            var result = InvokeIsRetriable(service, ex);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void IsRetriable_WithCustomDelegateReturningFalse_ShouldCheckOtherConditions()
        {
            // Arrange
            var options = Options.Create(new SqlRetryServiceOptions());
            var delegateOptions = new SqlRetryServiceDelegateOptions
            {
                CustomIsExceptionRetriable = ex => false,
            };
            var service = new SqlRetryService(
                _sqlConnectionBuilder,
                _sqlServerDataStoreConfiguration,
                options,
                delegateOptions,
                _coreFeatureConfiguration);

            var sqlEx = CreateSqlException(121, "an error occurred during the pre-login handshake");

            // Act
            var result = InvokeIsRetriable(service, sqlEx);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void IsRetriable_WithNonRetriableException_ShouldReturnFalse()
        {
            // Arrange
            var options = Options.Create(new SqlRetryServiceOptions());
            var delegateOptions = new SqlRetryServiceDelegateOptions();
            var service = new SqlRetryService(
                _sqlConnectionBuilder,
                _sqlServerDataStoreConfiguration,
                options,
                delegateOptions,
                _coreFeatureConfiguration);

            var ex = new ArgumentNullException("test");

            // Act
            var result = InvokeIsRetriable(service, ex);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void IsRetriable_WithStandardDeadlockError_ShouldReturnTrue()
        {
            // Arrange
            var options = Options.Create(new SqlRetryServiceOptions());
            var delegateOptions = new SqlRetryServiceDelegateOptions();
            var service = new SqlRetryService(
                _sqlConnectionBuilder,
                _sqlServerDataStoreConfiguration,
                options,
                delegateOptions,
                _coreFeatureConfiguration);

            var sqlEx = CreateSqlException(1205, "Transaction was deadlocked");

            // Act
            var result = InvokeIsRetriable(service, sqlEx);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void GetInstance_WithValidParameters_ShouldSetProperties()
        {
            // Arrange
            int expectedCommandTimeout = 100;
            int expectedMaxRetries = 3;
            int expectedRetryDelay = 1000;

            // Act
            var service = SqlRetryService.GetInstance(
                _sqlConnectionBuilder,
                expectedCommandTimeout,
                expectedMaxRetries,
                expectedRetryDelay);

            // Assert
            Assert.NotNull(service);
            var commandTimeout = GetPrivateFieldValue<int>(service, "_commandTimeout");
            var maxRetries = GetPrivateFieldValue<int>(service, "_maxRetries");
            var retryDelay = GetPrivateFieldValue<int>(service, "_retryMillisecondsDelay");

            Assert.Equal(expectedCommandTimeout, commandTimeout);
            Assert.Equal(expectedMaxRetries, maxRetries);
            Assert.Equal(expectedRetryDelay, retryDelay);
        }

        [Fact]
        public void GetInstance_WithDefaultParameters_ShouldSetDefaultValues()
        {
            // Act
            var service = SqlRetryService.GetInstance(_sqlConnectionBuilder);

            // Assert
            Assert.NotNull(service);
            var commandTimeout = GetPrivateFieldValue<int>(service, "_commandTimeout");
            var maxRetries = GetPrivateFieldValue<int>(service, "_maxRetries");
            var retryDelay = GetPrivateFieldValue<int>(service, "_retryMillisecondsDelay");

            Assert.Equal(300, commandTimeout);
            Assert.Equal(5, maxRetries);
            Assert.Equal(5000, retryDelay);
        }

        [Fact]
        public void GetInstance_WithNullConnectionBuilder_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => SqlRetryService.GetInstance(null));
        }

        private static T GetPrivateFieldValue<T>(object obj, string fieldName)
        {
            var field = obj.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null)
            {
                throw new ArgumentException($"Field '{fieldName}' not found");
            }

            return (T)field.GetValue(obj);
        }

        private static bool InvokeDefaultIsExceptionRetriable(Exception ex)
        {
            var method = typeof(SqlRetryService).GetMethod(
                "DefaultIsExceptionRetriable",
                BindingFlags.NonPublic | BindingFlags.Static);

            if (method == null)
            {
                throw new InvalidOperationException("Method 'DefaultIsExceptionRetriable' not found");
            }

            return (bool)method.Invoke(null, new object[] { ex });
        }

        private static bool InvokeIsRetriable(SqlRetryService service, Exception ex)
        {
            var method = typeof(SqlRetryService).GetMethod(
                "IsRetriable",
                BindingFlags.NonPublic | BindingFlags.Instance);

            if (method == null)
            {
                throw new InvalidOperationException("Method 'IsRetriable' not found");
            }

            return (bool)method.Invoke(service, new object[] { ex });
        }

        private static SqlException CreateSqlException(int errorNumber, string message)
        {
            var error = Create<SqlError>(
                errorNumber,
                (byte)0,
                (byte)0,
                string.Empty,
                message,
                string.Empty,
                0,
                null);

            var errorCollection = Create<SqlErrorCollection>();
            typeof(SqlErrorCollection)
                .GetMethod("Add", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(errorCollection, new object[] { error });

            var exception = typeof(SqlException)
                .GetMethod(
                    "CreateException",
                    BindingFlags.NonPublic | BindingFlags.Static,
                    null,
                    CallingConventions.ExplicitThis,
                    new[] { typeof(SqlErrorCollection), typeof(string) },
                    new ParameterModifier[] { })
                .Invoke(null, new object[] { errorCollection, "7.0.0" }) as SqlException;

            return exception;
        }

        private static T Create<T>(params object[] p)
        {
            var ctors = typeof(T).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance);
            return (T)ctors.First(ctor => ctor.GetParameters().Length == p.Length).Invoke(p);
        }
    }
}
