// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Operations;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage;

/// <summary>
/// Factory for creating <see cref="IAccessTokenProvider"/> instances for access to Cosmos.
/// </summary>
public delegate IAccessTokenProvider CosmosAccessTokenProviderFactory();
