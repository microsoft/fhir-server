// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    public interface ISqlServerFhirModel
    {
        (short lowestId, short highestId) ResourceTypeIdRange { get; }

        short GetResourceTypeId(string resourceTypeName);

        bool TryGetResourceTypeId(string resourceTypeName, out short id);

        string GetResourceTypeName(short resourceTypeId);

        byte GetClaimTypeId(string claimTypeName);

        short GetSearchParamId(Uri searchParamUri);

        void AddSearchParamIdToUriMapping(string searchParamUri, short searchParamId);

        void RemoveSearchParamIdToUriMapping(string searchParamUri);

        byte GetCompartmentTypeId(string compartmentType);

        bool TryGetSystemId(string system, out int systemId);

        int GetSystemId(string system);

        int GetQuantityCodeId(string code);

        bool TryGetQuantityCodeId(string code, out int quantityCodeId);
    }
}
