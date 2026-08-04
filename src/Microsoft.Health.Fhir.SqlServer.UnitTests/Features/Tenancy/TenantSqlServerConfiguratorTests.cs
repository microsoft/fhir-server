// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Features.Tenancy;
using Microsoft.Health.Fhir.Core.Registration;
using Microsoft.Health.Fhir.SqlServer.Features.Tenancy;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.SqlServer;
using Microsoft.Health.SqlServer.Configs;
using Microsoft.Health.SqlServer.Features.Client;
using Microsoft.Health.Test.Utilities;
using Xunit;

// SqlServerDataStoreConfiguration.AuthenticationType and ManagedIdentityClientId are marked [Obsolete] by the
// SQL package, but they remain public writable clone targets. Finding #2 requires the tenant clone to copy
// them, so these deprecated members are exercised deliberately.
#pragma warning disable CS0618

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Tenancy
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class TenantSqlServerConfiguratorTests
    {
        private static readonly TenantDescriptor Alpha = new(new TenantId("alpha"));
        private static readonly TenantDescriptor Beta = new(new TenantId("beta"));

        private const string RootConnectionString =
            "Server=tcp:root.database.windows.net;Database=root;Authentication=Active Directory Workload Identity";

        private const string AlphaConnectionString =
            "Server=tcp:pool01.database.windows.net;Database=alpha;Authentication=Active Directory Workload Identity";

        private const string AlphaConnectionStringVariant =
            "Authentication=Active Directory Workload Identity;Database=alpha;Server=tcp:pool01.database.windows.net;Max Pool Size=99;Min Pool Size=7";

        private const string BetaConnectionString =
            "Server=tcp:pool01.database.windows.net;Database=beta;Authentication=Active Directory Workload Identity";

        [Fact]
        public async Task GivenRealSqlServerRegistrations_WhenATenantContainerIsBuilt_ThenTheTenantSpecificConfigurationReplacesTheRootRegistrations()
        {
            IServiceCollection services = CreateRootServices(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [Alpha.TenantId.ToString()] = AlphaConnectionString,
                });

            Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IConfigureOptions<SqlServerDataStoreConfiguration>));
            Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(SqlServerDataStoreConfiguration));

            await using ServiceProvider rootProvider = BuildRootProvider(services);
            await using ITenantContainer container = await rootProvider
                .GetRequiredService<ITenantContainerFactory>()
                .CreateAsync(Alpha, CancellationToken.None);

            using ITenantLease lease = AcquireLease(container);

            SqlServerDataStoreConfiguration configuration = lease.Services.GetRequiredService<SqlServerDataStoreConfiguration>();
            IOptions<SqlServerDataStoreConfiguration> options = lease.Services.GetRequiredService<IOptions<SqlServerDataStoreConfiguration>>();
            IOptionsSnapshot<SqlServerDataStoreConfiguration> snapshot = lease.Services.GetRequiredService<IOptionsSnapshot<SqlServerDataStoreConfiguration>>();
            IOptionsMonitor<SqlServerDataStoreConfiguration> monitor = lease.Services.GetRequiredService<IOptionsMonitor<SqlServerDataStoreConfiguration>>();
            IOptionsFactory<SqlServerDataStoreConfiguration> factory = lease.Services.GetRequiredService<IOptionsFactory<SqlServerDataStoreConfiguration>>();

            Assert.Same(configuration, options.Value);
            Assert.Same(configuration, snapshot.Value);
            Assert.Same(configuration, monitor.CurrentValue);
            Assert.Same(configuration, factory.Create(Options.DefaultName));
            Assert.Equal(BuildExpectedConnectionString(AlphaConnectionString), configuration.ConnectionString);
            Assert.Equal(TimeSpan.FromMinutes(3), configuration.CommandTimeout);
            Assert.Equal(TimeSpan.FromMinutes(4), configuration.StatementTimeout);
            Assert.True(configuration.AllowDatabaseCreation);
            Assert.True(configuration.Initialize);
            Assert.Equal(SqlServerAuthenticationType.ManagedIdentity, configuration.AuthenticationType);
            Assert.Equal("11111111-1111-1111-1111-111111111111", configuration.ManagedIdentityClientId);
            Assert.Equal(42, configuration.TerminateWhenSchemaVersionUpdatedTo);
            Assert.Equal(20, configuration.MaxPoolSize);
            Assert.Equal(SqlRetryMode.Fixed, configuration.Retry.Mode);
            Assert.Equal(7, configuration.Retry.Settings.NumberOfTries);
            Assert.Equal(TimeSpan.FromSeconds(3), configuration.Retry.Settings.DeltaTime);
            Assert.Equal(TimeSpan.FromSeconds(1), configuration.Retry.Settings.MinTimeInterval);
            Assert.Equal(TimeSpan.FromSeconds(30), configuration.Retry.Settings.MaxTimeInterval);
            Assert.Equal(new[] { 1205, 40613 }, configuration.Retry.Settings.TransientErrors);
            Assert.NotNull(configuration.SchemaOptions);
            Assert.True(configuration.SchemaOptions.AutomaticUpdatesEnabled);
            Assert.Equal(13, configuration.SchemaOptions.JobPollingFrequencyInSeconds);
            Assert.Equal(77, configuration.SchemaOptions.InstanceRecordExpirationTimeInMinutes);

            // Non-vacuous fence: AddSqlServer registers exactly one IConfigureOptions at the root (see the
            // registration fence test), so an empty child here proves the configurator removed the root
            // configure descriptor. The root registers no IPostConfigureOptions/IOptionsChangeTokenSource,
            // so asserting their removal in the child would be vacuous and is deliberately omitted.
            Assert.Empty(lease.Services.GetServices<IConfigureOptions<SqlServerDataStoreConfiguration>>());
        }

        [Fact]
        public async Task GivenTenantConnectionStringVariants_WhenConfigured_ThenCanonicalPoolKeysAreBoundedPerTenant()
        {
            string alphaPoolKey = await ResolveTenantConnectionStringAsync(Alpha, AlphaConnectionString);
            string alphaVariantPoolKey = await ResolveTenantConnectionStringAsync(Alpha, AlphaConnectionStringVariant);
            string betaPoolKey = await ResolveTenantConnectionStringAsync(Beta, BetaConnectionString);

            var alphaBuilder = new SqlConnectionStringBuilder(alphaPoolKey);
            var betaBuilder = new SqlConnectionStringBuilder(betaPoolKey);

            Assert.Equal(alphaPoolKey, alphaVariantPoolKey);
            Assert.NotEqual(alphaPoolKey, betaPoolKey);
            Assert.Equal(20, alphaBuilder.MaxPoolSize);
            Assert.Equal(0, alphaBuilder.MinPoolSize);
            Assert.Equal(SqlAuthenticationMethod.ActiveDirectoryWorkloadIdentity, alphaBuilder.Authentication);
            Assert.Equal("alpha", alphaBuilder.InitialCatalog);
            Assert.Equal("beta", betaBuilder.InitialCatalog);
        }

        [Fact]
        public void GivenATenantWithNoOverride_WhenConfigured_ThenTheRootConnectionStringIsUsed()
        {
            var provider = new RootTenantConnectionStringProvider(
                Options.Create(new SqlServerDataStoreConfiguration { ConnectionString = RootConnectionString }));

            Assert.Equal(RootConnectionString, provider.GetConnectionString(Alpha));
        }

        [Fact]
        public void GivenATenantConnectionString_WhenBounded_ThenTheCanonicalConnectionStringPinsMinPoolSizeToZero()
        {
            (_, SqlServerDataStoreConfiguration clone) = BuildRootAndCloneConfiguration();

            // Min Pool Size defaults to zero, so the canonical string only contains it when the configurator
            // explicitly pins it. Asserting its presence catches a regression that drops the pin.
            Assert.Contains("Min Pool Size=0", clone.ConnectionString, StringComparison.Ordinal);
            Assert.Contains("Max Pool Size=20", clone.ConnectionString, StringComparison.Ordinal);
        }

        [Fact]
        public void GivenRootConfiguration_WhenClonedForATenant_ThenScalarsAreCopiedAndTheConnectionShapeIsReplaced()
        {
            (SqlServerDataStoreConfiguration root, SqlServerDataStoreConfiguration clone) = BuildRootAndCloneConfiguration();

            Assert.NotSame(root, clone);
            Assert.Equal(SqlServerAuthenticationType.ManagedIdentity, clone.AuthenticationType);
            Assert.Equal(root.ManagedIdentityClientId, clone.ManagedIdentityClientId);
            Assert.Equal(42, clone.TerminateWhenSchemaVersionUpdatedTo);
            Assert.Equal(TimeSpan.FromMinutes(3), clone.CommandTimeout);
            Assert.Equal(TimeSpan.FromMinutes(4), clone.StatementTimeout);
            Assert.True(clone.AllowDatabaseCreation);
            Assert.True(clone.Initialize);

            Assert.NotSame(root.Retry, clone.Retry);
            Assert.Equal(SqlRetryMode.Fixed, clone.Retry.Mode);

            Assert.NotSame(root.SchemaOptions, clone.SchemaOptions);
            Assert.Equal(77, clone.SchemaOptions.InstanceRecordExpirationTimeInMinutes);
            Assert.Equal(13, clone.SchemaOptions.JobPollingFrequencyInSeconds);
            Assert.True(clone.SchemaOptions.AutomaticUpdatesEnabled);

            Assert.Equal(20, clone.MaxPoolSize);
            Assert.NotEqual(root.ConnectionString, clone.ConnectionString);
        }

        [Fact]
        public void GivenRootRetrySettings_WhenTheConfigurationIsClonedForATenant_ThenTheNestedRetrySettingsAndCollectionsAreDistinctInstances()
        {
            (SqlServerDataStoreConfiguration root, SqlServerDataStoreConfiguration clone) = BuildRootAndCloneConfiguration();

            Assert.NotSame(root.Retry, clone.Retry);
            Assert.NotSame(root.Retry.Settings, clone.Retry.Settings);
            Assert.NotSame(root.Retry.Settings.TransientErrors, clone.Retry.Settings.TransientErrors);
        }

        [Fact]
        public void GivenRootRetrySettings_WhenMutatedAfterCloning_ThenTheTenantCloneIsUnaffected()
        {
            (SqlServerDataStoreConfiguration root, SqlServerDataStoreConfiguration clone) = BuildRootAndCloneConfiguration();

            root.Retry.Settings.NumberOfTries = 999;
            ((List<int>)root.Retry.Settings.TransientErrors).Add(4060);

            int[] expectedTransientErrors = { 1205, 40613 };

            Assert.Equal(7, clone.Retry.Settings.NumberOfTries);
            Assert.Equal(expectedTransientErrors, clone.Retry.Settings.TransientErrors);
        }

        [Fact]
        public void GivenTheTenantConfigurationClone_WhenEveryPublicPropertyIsClassified_ThenNestedStateIsClonedAndNoPropertyIsLeftOnDefaults()
        {
            (SqlServerDataStoreConfiguration root, SqlServerDataStoreConfiguration clone) = BuildRootAndCloneConfiguration();

            var configurationEquivalent = new HashSet<string>(StringComparer.Ordinal)
            {
                nameof(SqlServerDataStoreConfiguration.AuthenticationType),
                nameof(SqlServerDataStoreConfiguration.ManagedIdentityClientId),
                nameof(SqlServerDataStoreConfiguration.TerminateWhenSchemaVersionUpdatedTo),
                nameof(SqlServerDataStoreConfiguration.CommandTimeout),
                nameof(SqlServerDataStoreConfiguration.StatementTimeout),
                nameof(SqlServerDataStoreConfiguration.Initialize),
                nameof(SqlServerDataStoreConfiguration.AllowDatabaseCreation),
            };
            var configurationCloned = new HashSet<string>(StringComparer.Ordinal)
            {
                nameof(SqlServerDataStoreConfiguration.Retry),
                nameof(SqlServerDataStoreConfiguration.SchemaOptions),
            };
            var configurationReplaced = new HashSet<string>(StringComparer.Ordinal)
            {
                nameof(SqlServerDataStoreConfiguration.ConnectionString),
                nameof(SqlServerDataStoreConfiguration.MaxPoolSize),
            };

            var retryEquivalent = new HashSet<string>(StringComparer.Ordinal)
            {
                nameof(SqlClientRetryOptions.Mode),
            };
            var retryCloned = new HashSet<string>(StringComparer.Ordinal)
            {
                nameof(SqlClientRetryOptions.Settings),
            };
            var retryReplaced = new HashSet<string>(StringComparer.Ordinal);

            var schemaEquivalent = new HashSet<string>(StringComparer.Ordinal)
            {
                nameof(SqlServerSchemaOptions.AutomaticUpdatesEnabled),
                nameof(SqlServerSchemaOptions.InstanceRecordExpirationTimeInMinutes),
                nameof(SqlServerSchemaOptions.JobPollingFrequencyInSeconds),
            };
            var schemaCloned = new HashSet<string>(StringComparer.Ordinal);
            var schemaReplaced = new HashSet<string>(StringComparer.Ordinal);

            var settingsEquivalent = new HashSet<string>(StringComparer.Ordinal)
            {
                nameof(SqlRetryLogicOption.AuthorizedSqlCondition),
                nameof(SqlRetryLogicOption.DeltaTime),
                nameof(SqlRetryLogicOption.MaxTimeInterval),
                nameof(SqlRetryLogicOption.MinTimeInterval),
                nameof(SqlRetryLogicOption.NumberOfTries),
            };
            var settingsCloned = new HashSet<string>(StringComparer.Ordinal)
            {
                nameof(SqlRetryLogicOption.TransientErrors),
            };
            var settingsReplaced = new HashSet<string>(StringComparer.Ordinal);

            var problems = new List<string>();
            problems.AddRange(FindClassificationProblems(typeof(SqlServerDataStoreConfiguration), configurationEquivalent, configurationCloned, configurationReplaced));
            problems.AddRange(FindClassificationProblems(typeof(SqlClientRetryOptions), retryEquivalent, retryCloned, retryReplaced));
            problems.AddRange(FindClassificationProblems(typeof(SqlServerSchemaOptions), schemaEquivalent, schemaCloned, schemaReplaced));
            problems.AddRange(FindClassificationProblems(typeof(SqlRetryLogicOption), settingsEquivalent, settingsCloned, settingsReplaced));

            Assert.True(problems.Count == 0, "Unclassified or invalid clone properties: " + string.Join("; ", problems));

            AssertClassificationBehavior(typeof(SqlServerDataStoreConfiguration), root, clone, configurationEquivalent, configurationCloned, configurationReplaced);
            AssertClassificationBehavior(typeof(SqlServerSchemaOptions), root.SchemaOptions, clone.SchemaOptions, schemaEquivalent, schemaCloned, schemaReplaced);
            AssertClassificationBehavior(typeof(SqlClientRetryOptions), root.Retry, clone.Retry, retryEquivalent, retryCloned, retryReplaced);
            AssertClassificationBehavior(typeof(SqlRetryLogicOption), root.Retry.Settings, clone.Retry.Settings, settingsEquivalent, settingsCloned, settingsReplaced);
        }

        [Theory]
        [InlineData(null, "required")]
        [InlineData("", "required")]
        [InlineData("   ", "required")]
        [InlineData("Max Pool Size=20;Min Pool Size=0", "data source")]
        [InlineData("Database=alpha", "data source")]
        [InlineData("Server=tcp:pool01.database.windows.net", "initial catalog")]
        public void GivenAnInvalidTenantConnectionString_WhenTheTenantConfigurationIsBuilt_ThenAClearArgumentExceptionIsThrown(
            string tenantConnectionString,
            string expectedMessageFragment)
        {
            var configurator = new TenantSqlServerConfigurator(
                new StubTenantConnectionStringProvider(new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [Alpha.TenantId.ToString()] = tenantConnectionString,
                }),
                Options.Create(new SqlServerDataStoreConfiguration { ConnectionString = RootConnectionString }));

            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => configurator.Configure(new ServiceCollection(), Alpha));

            Assert.Equal("connectionString", exception.ParamName);
            Assert.Contains(expectedMessageFragment, exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("pool01.database.windows.net", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task GivenAHostCreatedDisposableConnectionStringProvider_WhenATenantContainerResolvesIt_ThenTheRootInstanceIsForwarded()
        {
            IServiceCollection services = CreateRootServices(
                registrar => registrar.AddSingleton<ITenantConnectionStringProvider, DisposableHostConnectionStringProvider>());

            await using ServiceProvider rootProvider = BuildRootProvider(services);
            ITenantConnectionStringProvider rootInstance = rootProvider.GetRequiredService<ITenantConnectionStringProvider>();

            await using ITenantContainer container = await rootProvider
                .GetRequiredService<ITenantContainerFactory>()
                .CreateAsync(Alpha, CancellationToken.None);

            using ITenantLease lease = AcquireLease(container);
            ITenantConnectionStringProvider tenantInstance = lease.Services.GetRequiredService<ITenantConnectionStringProvider>();

            Assert.Same(rootInstance, tenantInstance);
        }

        [Fact]
        public async Task GivenAHostCreatedDisposableConnectionStringProvider_WhenATenantContainerIsDisposed_ThenTheProviderIsNotDisposed()
        {
            IServiceCollection services = CreateRootServices(
                registrar => registrar.AddSingleton<ITenantConnectionStringProvider, DisposableHostConnectionStringProvider>());

            await using ServiceProvider rootProvider = BuildRootProvider(services);

            ITenantContainer container = await rootProvider
                .GetRequiredService<ITenantContainerFactory>()
                .CreateAsync(Alpha, CancellationToken.None);

            ITenantConnectionStringProvider tenantInstance;
            using (ITenantLease lease = AcquireLease(container))
            {
                tenantInstance = lease.Services.GetRequiredService<ITenantConnectionStringProvider>();
            }

            await container.DisposeAsync();

            DisposableHostConnectionStringProvider disposable = Assert.IsType<DisposableHostConnectionStringProvider>(tenantInstance);
            Assert.False(disposable.Disposed);
        }

        [Fact]
        public async Task GivenAHostCreatedConnectionStringProvider_WhenResolvedFromATenantContainer_ThenItReturnsEachTenantConnectionString()
        {
            IServiceCollection services = CreateRootServices(
                registrar => registrar.AddSingleton<ITenantConnectionStringProvider, DisposableHostConnectionStringProvider>());

            await using ServiceProvider rootProvider = BuildRootProvider(services);
            await using ITenantContainer container = await rootProvider
                .GetRequiredService<ITenantContainerFactory>()
                .CreateAsync(Alpha, CancellationToken.None);

            using ITenantLease lease = AcquireLease(container);
            ITenantConnectionStringProvider provider = lease.Services.GetRequiredService<ITenantConnectionStringProvider>();

            Assert.Equal(AlphaConnectionString, provider.GetConnectionString(Alpha));
            Assert.Equal(BetaConnectionString, provider.GetConnectionString(Beta));
        }

        [Fact]
        public async Task GivenARealTenantContainer_WhenTheSqlConnectionBuilderIsResolved_ThenItTargetsTheTenantDatabaseWithABoundedPool()
        {
            IServiceCollection services = CreateRootServices(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [Alpha.TenantId.ToString()] = AlphaConnectionString,
                });

            await using ServiceProvider rootProvider = BuildRootProvider(services);
            await using ITenantContainer container = await rootProvider
                .GetRequiredService<ITenantContainerFactory>()
                .CreateAsync(Alpha, CancellationToken.None);

            using ITenantLease lease = AcquireLease(container);

            ISqlConnectionBuilder connectionBuilder = lease.Services.GetRequiredService<ISqlConnectionBuilder>();
            Assert.Equal("alpha", connectionBuilder.DefaultDatabase);

            using SqlConnection connection = connectionBuilder.CreateConnection(_ => { });
            var parsed = new SqlConnectionStringBuilder(connection.ConnectionString);
            Assert.Equal("alpha", parsed.InitialCatalog);
            Assert.Equal("tcp:pool01.database.windows.net", parsed.DataSource);
            Assert.Equal(20, parsed.MaxPoolSize);
            Assert.Equal(0, parsed.MinPoolSize);

            using IServiceScope scope = lease.Services.CreateScope();
            SqlConnectionWrapperFactory connectionWrapperFactory = scope.ServiceProvider.GetRequiredService<SqlConnectionWrapperFactory>();
            Assert.NotNull(connectionWrapperFactory);
        }

        private static string BuildExpectedConnectionString(string connectionString)
        {
            var builder = new SqlConnectionStringBuilder(connectionString)
            {
                MaxPoolSize = 20,
                MinPoolSize = 0,
            };

            return builder.ConnectionString;
        }

        private static (SqlServerDataStoreConfiguration Root, SqlServerDataStoreConfiguration Clone) BuildRootAndCloneConfiguration()
        {
            var root = new SqlServerDataStoreConfiguration
            {
                ConnectionString = RootConnectionString,
                CommandTimeout = TimeSpan.FromMinutes(3),
                StatementTimeout = TimeSpan.FromMinutes(4),
                AllowDatabaseCreation = true,
                Initialize = true,
                MaxPoolSize = 99,
                AuthenticationType = SqlServerAuthenticationType.ManagedIdentity,
                ManagedIdentityClientId = "11111111-1111-1111-1111-111111111111",
                TerminateWhenSchemaVersionUpdatedTo = 42,
                SchemaOptions = new SqlServerSchemaOptions
                {
                    AutomaticUpdatesEnabled = true,
                    JobPollingFrequencyInSeconds = 13,
                    InstanceRecordExpirationTimeInMinutes = 77,
                },
                Retry = new SqlClientRetryOptions
                {
                    Mode = SqlRetryMode.Fixed,
                    Settings = new SqlRetryLogicOption
                    {
                        NumberOfTries = 7,
                        DeltaTime = TimeSpan.FromSeconds(3),
                        MinTimeInterval = TimeSpan.FromSeconds(1),
                        MaxTimeInterval = TimeSpan.FromSeconds(30),
                        TransientErrors = new List<int> { 1205, 40613 },
                        AuthorizedSqlCondition = _ => true,
                    },
                },
            };

            var configurator = new TenantSqlServerConfigurator(
                new StubTenantConnectionStringProvider(new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [Alpha.TenantId.ToString()] = AlphaConnectionString,
                }),
                Options.Create(root));

            var services = new ServiceCollection();
            configurator.Configure(services, Alpha);

            using ServiceProvider provider = services.BuildServiceProvider();
            SqlServerDataStoreConfiguration clone = provider.GetRequiredService<SqlServerDataStoreConfiguration>();

            Assert.Same(clone, provider.GetRequiredService<IOptions<SqlServerDataStoreConfiguration>>().Value);

            return (root, clone);
        }

        private static List<string> FindClassificationProblems(
            Type type,
            ISet<string> equivalent,
            ISet<string> clonedReferences,
            ISet<string> replaced)
        {
            var problems = new List<string>();

            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (property.GetIndexParameters().Length > 0)
                {
                    problems.Add($"{type.Name}.{property.Name} is an indexer and is not classified.");
                    continue;
                }

                if (!property.CanRead)
                {
                    problems.Add($"{type.Name}.{property.Name} is not readable and cannot be verified.");
                    continue;
                }

                if (!property.CanWrite)
                {
                    problems.Add($"{type.Name}.{property.Name} is not writable and cannot be round-tripped.");
                    continue;
                }

                int classifications =
                    (equivalent.Contains(property.Name) ? 1 : 0) +
                    (clonedReferences.Contains(property.Name) ? 1 : 0) +
                    (replaced.Contains(property.Name) ? 1 : 0);

                if (classifications != 1)
                {
                    problems.Add($"{type.Name}.{property.Name} must be classified exactly once as cloned, equivalent, or replaced (was {classifications}).");
                }
            }

            return problems;
        }

        private static void AssertClassificationBehavior(
            Type type,
            object root,
            object clone,
            ISet<string> equivalent,
            ISet<string> clonedReferences,
            ISet<string> replaced)
        {
            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (property.GetIndexParameters().Length > 0 || !property.CanRead)
                {
                    continue;
                }

                object rootValue = property.GetValue(root);
                object cloneValue = property.GetValue(clone);

                if (equivalent.Contains(property.Name))
                {
                    Assert.Equal(rootValue, cloneValue);
                }
                else if (clonedReferences.Contains(property.Name))
                {
                    Assert.NotNull(rootValue);
                    Assert.NotNull(cloneValue);
                    Assert.NotSame(rootValue, cloneValue);
                }
                else if (replaced.Contains(property.Name))
                {
                    Assert.NotEqual(rootValue, cloneValue);
                }
            }
        }

        private static ITenantLease AcquireLease(ITenantContainer container)
        {
            Assert.True(container.TryAcquire(out ITenantLease lease));
            return lease;
        }

        private static ServiceProvider BuildRootProvider(IServiceCollection services) =>
            services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = false });

        private static IServiceCollection CreateRootServices(IReadOnlyDictionary<string, string> connectionStrings) =>
            CreateRootServices(registrar => registrar.AddSingleton<ITenantConnectionStringProvider>(
                new StubTenantConnectionStringProvider(connectionStrings)));

        private static IServiceCollection CreateRootServices(Action<IServiceCollection> registerConnectionStringProvider)
        {
            var services = new ServiceCollection();
            registerConnectionStringProvider(services);

            IFhirServerBuilder builder = new TestFhirServerBuilder(services);
            builder.AddSqlServer(ConfigureRoot);

            services.RemoveAll<IHostedService>();
            services.AddSingleton(new TenantSharedServiceRegistry());
            services.AddSingleton<ITenantHostedServicePolicy>(new TenantHostedServicePolicy());
            services.AddSingleton<ITenantServiceBlueprint>(new TenantServiceBlueprint(services));
            services.AddSingleton(TimeProvider.System);
            services.AddSingleton<ITenantContainerFactory, TenantContainerFactory>();

            return services;
        }

        private static void ConfigureRoot(SqlServerDataStoreConfiguration configuration)
        {
            configuration.ConnectionString = RootConnectionString;
            configuration.CommandTimeout = TimeSpan.FromMinutes(3);
            configuration.StatementTimeout = TimeSpan.FromMinutes(4);
            configuration.AllowDatabaseCreation = true;
            configuration.Initialize = true;
            configuration.AuthenticationType = SqlServerAuthenticationType.ManagedIdentity;
            configuration.ManagedIdentityClientId = "11111111-1111-1111-1111-111111111111";
            configuration.TerminateWhenSchemaVersionUpdatedTo = 42;
            configuration.Retry = new SqlClientRetryOptions
            {
                Mode = SqlRetryMode.Fixed,
                Settings = new SqlRetryLogicOption
                {
                    NumberOfTries = 7,
                    DeltaTime = TimeSpan.FromSeconds(3),
                    MinTimeInterval = TimeSpan.FromSeconds(1),
                    MaxTimeInterval = TimeSpan.FromSeconds(30),
                    TransientErrors = new List<int> { 1205, 40613 },
                    AuthorizedSqlCondition = static _ => true,
                },
            };
            configuration.SchemaOptions = new SqlServerSchemaOptions
            {
                AutomaticUpdatesEnabled = true,
                JobPollingFrequencyInSeconds = 13,
                InstanceRecordExpirationTimeInMinutes = 77,
            };
        }

        private static async Task<string> ResolveTenantConnectionStringAsync(TenantDescriptor tenant, string connectionString)
        {
            IServiceCollection services = CreateRootServices(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [tenant.TenantId.ToString()] = connectionString,
                });

            await using ServiceProvider rootProvider = BuildRootProvider(services);
            await using ITenantContainer container = await rootProvider
                .GetRequiredService<ITenantContainerFactory>()
                .CreateAsync(tenant, CancellationToken.None);

            using ITenantLease lease = AcquireLease(container);

            return lease.Services.GetRequiredService<SqlServerDataStoreConfiguration>().ConnectionString;
        }

        private sealed class StubTenantConnectionStringProvider : ITenantConnectionStringProvider
        {
            private readonly IReadOnlyDictionary<string, string> _connectionStrings;

            public StubTenantConnectionStringProvider(IReadOnlyDictionary<string, string> connectionStrings)
            {
                _connectionStrings = connectionStrings;
            }

            public string GetConnectionString(TenantDescriptor tenant) => _connectionStrings[tenant.TenantId.ToString()];
        }

        private sealed class DisposableHostConnectionStringProvider : ITenantConnectionStringProvider, IDisposable
        {
            public bool Disposed { get; private set; }

            public string GetConnectionString(TenantDescriptor tenant) => tenant.TenantId.ToString() switch
            {
                "alpha" => AlphaConnectionString,
                "beta" => BetaConnectionString,
                _ => throw new KeyNotFoundException($"No connection string is configured for tenant '{tenant.TenantId}'."),
            };

            public void Dispose() => Disposed = true;
        }

        private sealed class TestFhirServerBuilder : IFhirServerBuilder
        {
            public TestFhirServerBuilder(IServiceCollection services)
            {
                Services = services;
            }

            public IServiceCollection Services { get; }
        }
    }
}
