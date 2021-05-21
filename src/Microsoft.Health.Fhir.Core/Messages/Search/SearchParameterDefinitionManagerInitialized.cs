// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using MediatR;

namespace Microsoft.Health.Fhir.Core.Messages.Search
{
    /// <summary>
    /// A notification that is raised when the SearchParameters are initialized
    /// </summary>
    public class SearchParameterDefinitionManagerInitialized : INotification
    {
    }
}
