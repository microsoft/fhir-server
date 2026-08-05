// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EnsureThat;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Features.Tenancy;
using Microsoft.Health.SqlServer.Configs;

namespace Microsoft.Health.Fhir.SqlServer.Features.Tenancy
{
    /// <summary>
    /// Rebinds the SQL configuration for a tenant container so every SQL component targets the tenant's
    /// own database and uses a bounded client pool.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The tenant container receives a single, stable <see cref="SqlServerDataStoreConfiguration"/>
    /// instance. The named-options, snapshot, monitor, and factory surfaces are all deliberately collapsed
    /// onto that one value: every <c>Get(name)</c>, <c>CurrentValue</c>, <c>Value</c>, and
    /// <c>Create(name)</c> call returns the exact same configuration regardless of the option name, so
    /// option names never create per-name variants inside a tenant container.
    /// </para>
    /// <para>
    /// <see cref="IOptionsMonitor{TOptions}.OnChange"/> intentionally never fires: a tenant container is
    /// built from a point-in-time snapshot of the root registrations, so changes to the root options after
    /// the container is built are not observed. Applying a root option change to running tenants requires
    /// rebuilding (recycling) the tenant container, not reacting to a monitor callback.
    /// </para>
    /// <para>
    /// The connection string is bounded to <see cref="MaxPoolSizePerTenant"/> connections
    /// (<c>Max Pool Size=20</c>) so a single tenant cannot exhaust SQL sessions and starve its peers; this
    /// caps per-tenant fan-out across a shared pool. <c>Min Pool Size=0</c> lets an idle tenant release its
    /// connections back to SQL rather than pinning a warm pool for a tenant that receives no traffic.
    /// </para>
    /// <para>
    /// The value <c>20</c> is bounded by <c>MaxPoolSizePerTenant * N * R &lt;= 30,000</c> per
    /// elastic pool, where <c>N</c> is the pool's database count and <c>R</c> is the maximum replica
    /// count. The SQL load balancer caps <c>N</c> at four databases per pool vCore, yielding 48
    /// databases at 12 vCore and 128 at 32 vCore. At <c>R=10</c>, those pools consume at most 9,600
    /// and 25,600 sessions, respectively; the next supported size, 40 vCore, would consume 32,000
    /// and violate the SQL session ceiling. This configurator cannot observe pool identity or replica
    /// count, so P3 must fail startup unless
    /// <c>MaxPoolSizePerTenant * (4 * SkuCapacity) * R_max &lt;= 30,000</c>. If fixed-pool capacity
    /// ever becomes dynamic, the same invariant must be checked before admitting tenants.
    /// </para>
    /// </remarks>
    public sealed class TenantSqlServerConfigurator : ITenantServiceConfigurator
    {
        private const int MaxPoolSizePerTenant = 20;
        private const int MinPoolSizePerTenant = 0;

        // Values are cloned explicitly; reflection only verifies that the package shape still matches these sets.
        private static readonly IReadOnlyDictionary<Type, IReadOnlySet<string>> SupportedCloneProperties =
            new Dictionary<Type, IReadOnlySet<string>>
            {
                [typeof(SqlServerDataStoreConfiguration)] = new HashSet<string>(StringComparer.Ordinal)
                {
                    nameof(SqlServerDataStoreConfiguration.AllowDatabaseCreation),
#pragma warning disable CS0618 // Type or member is obsolete -- nameof references only; no obsolete member is invoked.
                    nameof(SqlServerDataStoreConfiguration.AuthenticationType),
                    nameof(SqlServerDataStoreConfiguration.CommandTimeout),
                    nameof(SqlServerDataStoreConfiguration.ConnectionString),
                    nameof(SqlServerDataStoreConfiguration.Initialize),
                    nameof(SqlServerDataStoreConfiguration.ManagedIdentityClientId),
#pragma warning restore CS0618 // Type or member is obsolete
                    nameof(SqlServerDataStoreConfiguration.MaxPoolSize),
                    nameof(SqlServerDataStoreConfiguration.Retry),
                    nameof(SqlServerDataStoreConfiguration.SchemaOptions),
                    nameof(SqlServerDataStoreConfiguration.StatementTimeout),
                    nameof(SqlServerDataStoreConfiguration.TerminateWhenSchemaVersionUpdatedTo),
                },
                [typeof(SqlClientRetryOptions)] = new HashSet<string>(StringComparer.Ordinal)
                {
                    nameof(SqlClientRetryOptions.Mode),
                    nameof(SqlClientRetryOptions.Settings),
                },
                [typeof(SqlServerSchemaOptions)] = new HashSet<string>(StringComparer.Ordinal)
                {
                    nameof(SqlServerSchemaOptions.AutomaticUpdatesEnabled),
                    nameof(SqlServerSchemaOptions.InstanceRecordExpirationTimeInMinutes),
                    nameof(SqlServerSchemaOptions.JobPollingFrequencyInSeconds),
                },
                [typeof(SqlRetryLogicOption)] = new HashSet<string>(StringComparer.Ordinal)
                {
                    nameof(SqlRetryLogicOption.AuthorizedSqlCondition),
                    nameof(SqlRetryLogicOption.DeltaTime),
                    nameof(SqlRetryLogicOption.MaxTimeInterval),
                    nameof(SqlRetryLogicOption.MinTimeInterval),
                    nameof(SqlRetryLogicOption.NumberOfTries),
                    nameof(SqlRetryLogicOption.TransientErrors),
                },
            };

        private readonly ITenantConnectionStringProvider _connectionStringProvider;
        private readonly SqlServerDataStoreConfiguration _rootConfiguration;

        static TenantSqlServerConfigurator()
        {
            ValidateSupportedShape();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TenantSqlServerConfigurator"/> class.
        /// </summary>
        /// <param name="connectionStringProvider">Supplies the per-tenant connection string.</param>
        /// <param name="rootConfiguration">The root SQL configuration to clone for each tenant.</param>
        public TenantSqlServerConfigurator(
            ITenantConnectionStringProvider connectionStringProvider,
            IOptions<SqlServerDataStoreConfiguration> rootConfiguration)
        {
            EnsureArg.IsNotNull(connectionStringProvider, nameof(connectionStringProvider));
            EnsureArg.IsNotNull(rootConfiguration?.Value, nameof(rootConfiguration));

            _connectionStringProvider = connectionStringProvider;
            _rootConfiguration = rootConfiguration.Value;
        }

        /// <inheritdoc />
        public void Configure(IServiceCollection services, TenantDescriptor tenant)
        {
            EnsureArg.IsNotNull(services, nameof(services));
            EnsureArg.IsNotNull(tenant, nameof(tenant));

            SqlServerDataStoreConfiguration configuration = CloneConfiguration(_rootConfiguration);
            configuration.ConnectionString = BuildBoundedConnectionString(_connectionStringProvider.GetConnectionString(tenant));
            configuration.MaxPoolSize = MaxPoolSizePerTenant;

            ForwardRootConnectionStringProvider(services);

            services.RemoveAll<SqlServerDataStoreConfiguration>();
            services.RemoveAll<IConfigureOptions<SqlServerDataStoreConfiguration>>();
            services.RemoveAll<IPostConfigureOptions<SqlServerDataStoreConfiguration>>();
            services.RemoveAll<IOptions<SqlServerDataStoreConfiguration>>();
            services.RemoveAll<IOptionsSnapshot<SqlServerDataStoreConfiguration>>();
            services.RemoveAll<IOptionsMonitor<SqlServerDataStoreConfiguration>>();
            services.RemoveAll<IOptionsFactory<SqlServerDataStoreConfiguration>>();
            services.RemoveAll<IOptionsChangeTokenSource<SqlServerDataStoreConfiguration>>();

            services.AddSingleton(configuration);
            services.AddSingleton<IOptions<SqlServerDataStoreConfiguration>>(Options.Create(configuration));
            services.AddSingleton<IOptionsSnapshot<SqlServerDataStoreConfiguration>>(
                new SingletonOptionsSnapshot<SqlServerDataStoreConfiguration>(configuration));
            services.AddSingleton<IOptionsMonitor<SqlServerDataStoreConfiguration>>(
                new SingletonOptionsMonitor<SqlServerDataStoreConfiguration>(configuration));
            services.AddSingleton<IOptionsFactory<SqlServerDataStoreConfiguration>>(
                new SingletonOptionsFactory<SqlServerDataStoreConfiguration>(configuration));
        }

        /// <summary>
        /// Replaces any cloned <see cref="ITenantConnectionStringProvider"/> registration with the exact
        /// root-owned instance held by this configurator.
        /// </summary>
        /// <remarks>
        /// The provider is conceptually root-owned: it is resolved once from the root container (as an
        /// instance a host may have created through its own DI) and shared by every tenant. Cloning the
        /// root's type or factory registration into a tenant container would build a redundant child-owned
        /// provider that the child container would dispose. Registering the captured root instance as an
        /// <c>ImplementationInstance</c> means the child container never owns and therefore never disposes it.
        /// This is done here, rather than by adding
        /// <see cref="ITenantConnectionStringProvider"/> to the Core tenancy infrastructure forward list,
        /// to avoid a Core-to-SQL dependency.
        /// </remarks>
        private void ForwardRootConnectionStringProvider(IServiceCollection services)
        {
            services.RemoveAll<ITenantConnectionStringProvider>();
            services.AddSingleton(_connectionStringProvider);
        }

        private static string BuildBoundedConnectionString(string connectionString)
        {
            // Reject an unusable connection string before parsing so the value is never surfaced (for
            // example in a SqlConnectionStringBuilder parse error) and the failure names the offending
            // argument rather than a raw string that may contain a secret.
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new ArgumentException(
                    "A tenant connection string is required but was null, empty, or whitespace.",
                    nameof(connectionString));
            }

            // A malformed keyword throws SqlConnectionStringBuilder's own ArgumentException; let it flow to
            // keep the failure mode consistent with the rest of the SQL data store.
            var builder = new SqlConnectionStringBuilder(connectionString);

            if (string.IsNullOrWhiteSpace(builder.DataSource) || string.IsNullOrWhiteSpace(builder.InitialCatalog))
            {
                throw new ArgumentException(
                    "A tenant connection string must specify both a data source (Server) and an initial catalog (Database).",
                    nameof(connectionString));
            }

            builder.MaxPoolSize = MaxPoolSizePerTenant;
            builder.MinPoolSize = MinPoolSizePerTenant;

            return builder.ConnectionString;
        }

        private static SqlServerDataStoreConfiguration CloneConfiguration(SqlServerDataStoreConfiguration source)
        {
            EnsureArg.IsNotNull(source, nameof(source));

            var clone = new SqlServerDataStoreConfiguration
            {
                AllowDatabaseCreation = source.AllowDatabaseCreation,
                CommandTimeout = source.CommandTimeout,
                ConnectionString = source.ConnectionString,
                Initialize = source.Initialize,
                MaxPoolSize = source.MaxPoolSize,
                StatementTimeout = source.StatementTimeout,
                TerminateWhenSchemaVersionUpdatedTo = source.TerminateWhenSchemaVersionUpdatedTo,
                Retry = CloneRetryOptions(source.Retry),
                SchemaOptions = CloneSchemaOptions(source.SchemaOptions),
            };

            // These obsolete properties remain public writable state and must be cloned.
#pragma warning disable CS0618 // Type or member is obsolete
            clone.AuthenticationType = source.AuthenticationType;
            clone.ManagedIdentityClientId = source.ManagedIdentityClientId;
#pragma warning restore CS0618 // Type or member is obsolete

            return clone;
        }

        private static SqlClientRetryOptions CloneRetryOptions(SqlClientRetryOptions source)
        {
            if (source == null)
            {
                return null;
            }

            return new SqlClientRetryOptions
            {
                Mode = source.Mode,
                Settings = CloneRetryLogicOption(source.Settings),
            };
        }

        private static SqlRetryLogicOption CloneRetryLogicOption(SqlRetryLogicOption source)
        {
            if (source == null)
            {
                return null;
            }

            return new SqlRetryLogicOption
            {
                NumberOfTries = source.NumberOfTries,
                DeltaTime = source.DeltaTime,
                MinTimeInterval = source.MinTimeInterval,
                MaxTimeInterval = source.MaxTimeInterval,
                AuthorizedSqlCondition = source.AuthorizedSqlCondition,

                // A distinct, mutation-safe collection so mutating the root's transient errors cannot leak
                // into a tenant clone. Preserve null so an unset collection stays unset.
                TransientErrors = source.TransientErrors == null ? null : new List<int>(source.TransientErrors),
            };
        }

        private static SqlServerSchemaOptions CloneSchemaOptions(SqlServerSchemaOptions source)
        {
            if (source == null)
            {
                return null;
            }

            return new SqlServerSchemaOptions
            {
                AutomaticUpdatesEnabled = source.AutomaticUpdatesEnabled,
                InstanceRecordExpirationTimeInMinutes = source.InstanceRecordExpirationTimeInMinutes,
                JobPollingFrequencyInSeconds = source.JobPollingFrequencyInSeconds,
            };
        }

        /// <summary>
        /// Verifies that every cloned type still exposes exactly the public instance properties the explicit
        /// clone copies, so a package upgrade that adds, removes, or reshapes a property fails loudly instead
        /// of silently leaving new state on its default.
        /// </summary>
        private static void ValidateSupportedShape()
        {
            foreach (KeyValuePair<Type, IReadOnlySet<string>> cloneShape in SupportedCloneProperties)
            {
                ValidateShape(cloneShape.Key, cloneShape.Value);
            }
        }

        private static void ValidateShape(Type type, IReadOnlySet<string> supportedProperties)
        {
            var actualProperties = new HashSet<string>(StringComparer.Ordinal);

            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (property.GetIndexParameters().Length > 0)
                {
                    throw new InvalidOperationException(
                        $"The tenant SQL clone cannot copy indexer '{type.FullName}.{property.Name}'. " +
                        "The clone must be updated to handle the new Microsoft.Health.SqlServer shape.");
                }

                if (!property.CanRead || !property.CanWrite)
                {
                    throw new InvalidOperationException(
                        $"The tenant SQL clone requires '{type.FullName}.{property.Name}' to be both readable " +
                        "and writable. The clone must be updated to handle the new Microsoft.Health.SqlServer shape.");
                }

                if (!supportedProperties.Contains(property.Name))
                {
                    throw new InvalidOperationException(
                        $"'{type.FullName}.{property.Name}' is a new public property the tenant SQL clone does " +
                        "not copy. Add it to the explicit clone so tenant configuration is not silently dropped.");
                }

                actualProperties.Add(property.Name);
            }

            if (!actualProperties.SetEquals(supportedProperties))
            {
                IEnumerable<string> missingProperties = supportedProperties.Where(name => !actualProperties.Contains(name));
                throw new InvalidOperationException(
                    $"'{type.FullName}' no longer exposes the expected clone properties: " +
                    string.Join(", ", missingProperties) + ". The tenant SQL clone must be updated to match the new shape.");
            }
        }

        private sealed class SingletonOptionsFactory<TOptions> : IOptionsFactory<TOptions>
            where TOptions : class
        {
            private readonly TOptions _value;

            public SingletonOptionsFactory(TOptions value)
            {
                _value = value;
            }

            public TOptions Create(string name) => _value;
        }

        private sealed class SingletonOptionsMonitor<TOptions> : IOptionsMonitor<TOptions>
            where TOptions : class
        {
            private readonly TOptions _value;

            public SingletonOptionsMonitor(TOptions value)
            {
                _value = value;
            }

            public TOptions CurrentValue => _value;

            public TOptions Get(string name) => _value;

            // A tenant container is a point-in-time snapshot: change callbacks never fire, and a root option
            // change is applied by rebuilding the tenant container rather than by notifying listeners.
            public IDisposable OnChange(Action<TOptions, string> listener) => EmptyDisposable.Instance;
        }

        private sealed class SingletonOptionsSnapshot<TOptions> : IOptionsSnapshot<TOptions>
            where TOptions : class
        {
            private readonly TOptions _value;

            public SingletonOptionsSnapshot(TOptions value)
            {
                _value = value;
            }

            public TOptions Value => _value;

            public TOptions Get(string name) => _value;
        }

        private sealed class EmptyDisposable : IDisposable
        {
            public static EmptyDisposable Instance { get; } = new EmptyDisposable();

            public void Dispose()
            {
            }
        }
    }
}
