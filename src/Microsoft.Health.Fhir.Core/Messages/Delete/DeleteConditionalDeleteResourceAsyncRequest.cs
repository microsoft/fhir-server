// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using MediatR;

namespace Microsoft.Health.Fhir.Core.Messages.Delete;

public class DeleteConditionalDeleteResourceAsyncRequest : IRequest<DeleteConditionalDeleteResourceAsyncResponse>
{
    public DeleteConditionalDeleteResourceAsyncRequest(long groupId)
    {
        GroupId = groupId;
    }

    public long GroupId { get; }
}
