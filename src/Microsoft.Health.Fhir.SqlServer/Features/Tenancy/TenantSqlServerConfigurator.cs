// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
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
    public sealed class TenantSqlServerConfigurator : ITenantServiceConfigurator
    {
        private const int MaxPoolSizePerTenant = 20;

        private readonly ITenantConnectionStringProvider _connectionStringProvider;
        private readonly SqlServerDataStoreConfiguration _rootConfiguration;

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

        private static string BuildBoundedConnectionString(string connectionString)
        {
            var builder = new SqlConnectionStringBuilder(connectionString)
            {
                MaxPoolSize = MaxPoolSizePerTenant,
                MinPoolSize = 0,
            };

            return builder.ConnectionString;
        }

        private static SqlServerDataStoreConfiguration CloneConfiguration(SqlServerDataStoreConfiguration source)
        {
            EnsureArg.IsNotNull(source, nameof(source));

            SqlServerDataStoreConfiguration clone = CloneWithWritablePublicProperties(source);
            clone.Retry = CloneWithWritablePublicProperties(source.Retry);
            clone.SchemaOptions = CloneWithWritablePublicProperties(source.SchemaOptions);

            return clone;
        }

        private static T CloneWithWritablePublicProperties<T>(T source)
            where T : class, new()
        {
            if (source == null)
            {
                return null;
            }

            var clone = new T();

            foreach (PropertyInfo property in typeof(T).GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (property.CanRead && property.CanWrite)
                {
                    property.SetValue(clone, property.GetValue(source));
                }
            }

            return clone;
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
