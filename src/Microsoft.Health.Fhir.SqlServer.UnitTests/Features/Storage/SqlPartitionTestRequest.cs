// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.SqlServer.Features.Partitions;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.SqlServer.Registration;
using NSubstitute;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Storage
{
    internal sealed class SqlPartitionTestRequest : IDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly IServiceScope _scope;

        internal SqlPartitionTestRequest(string partitionId, string databaseName)
        {
            var context = Substitute.For<IFhirRequestContext>();
            context.PartitionId.Returns(partitionId);
            var accessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            accessor.RequestContext.Returns(context);
            var configuration = new PartitionConfiguration();
            configuration.Partitions.Add(new PartitionInfo { Id = partitionId, DatabaseName = databaseName });
            var http = Substitute.For<IHttpContextAccessor>();
            var services = new ServiceCollection();
            services.AddSingleton(accessor);
            services.AddSingleton(http);
            services.AddSingleton(new PartitionManager(Options.Create(configuration)));
            services.AddLogging();
            services.AddSqlPartitionDatabaseSelection();
            _provider = services.BuildServiceProvider(validateScopes: true);
            _scope = _provider.CreateScope();
            http.HttpContext.Returns(new DefaultHttpContext { RequestServices = _scope.ServiceProvider });
            Resolver = _provider.GetRequiredService<SqlPartitionDatabaseResolver>();
        }

        internal SqlPartitionDatabaseResolver Resolver { get; }

        internal static SqlPartitionDatabaseResolver CreateApplicationResolver()
        {
            var http = Substitute.For<IHttpContextAccessor>();
            http.HttpContext.Returns((HttpContext)null);
            return new SqlPartitionDatabaseResolver(http);
        }

        public void Dispose()
        {
            _scope.Dispose();
            _provider.Dispose();
        }
    }
}
