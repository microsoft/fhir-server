// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.SourceNodeSerialization.SourceNodes;

public interface IResourceNode
{
    string Id { get; set; }

    string ResourceType { get; set; }
}
