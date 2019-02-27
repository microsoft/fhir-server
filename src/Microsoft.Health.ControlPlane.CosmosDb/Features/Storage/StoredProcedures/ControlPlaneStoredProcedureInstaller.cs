// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using EnsureThat;
using Microsoft.Health.ControlPlane.CosmosDb.Features.Storage.Versioning;
using Microsoft.Health.CosmosDb.Features.Storage.StoredProcedures;

namespace Microsoft.Health.ControlPlane.CosmosDb.Features.Storage.StoredProcedures
{
    public class ControlPlaneStoredProcedureInstaller : StoredProcedureInstaller, IControlPlaneCollectionUpdater
    {
        private readonly IEnumerable<IControlPlaneStoredProcedure> _storedProcedures;

        public ControlPlaneStoredProcedureInstaller(IEnumerable<IControlPlaneStoredProcedure> storedProcedures)
        {
            EnsureArg.IsNotNull(storedProcedures, nameof(storedProcedures));

            _storedProcedures = storedProcedures;
        }

        public override IEnumerable<IStoredProcedure> GetStoredProcedures()
        {
            return _storedProcedures;
        }
    }
}
