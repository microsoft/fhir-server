// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Health.SqlServer.Features.Schema.Messages.Notifications;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    public class SchemaUpgradedHandler : INotificationHandler<SchemaUpgradedNotification>
    {
        private SqlServerFhirModel _sqlServerFhirModel;

        public SchemaUpgradedHandler(SqlServerFhirModel sqlServerFhirModel)
        {
            EnsureArg.IsNotNull(sqlServerFhirModel, nameof(sqlServerFhirModel));

            _sqlServerFhirModel = sqlServerFhirModel;
        }

        public async Task Handle(SchemaUpgradedNotification notification, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(notification, nameof(notification));

            // If it is a snapshot upgrade, then we need to run initialization for all schema versions up to the current version.
            // When schema is run via tool, then the notification will be sent out from SqlSchemaManager.cs and if schema is initialized
            // by the fhir-server itself on startup then the notification will be sent out from SchemaInitializer.cs
            await _sqlServerFhirModel.Initialize(notification.Version, notification.IsFullSchemaSnapshot, cancellationToken);
        }
    }
}
