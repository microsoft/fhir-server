// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using MediatR;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Messages.Export
{
    public class CreateExportRequest : IRequest<CreateExportResponse>
    {
        public CreateExportRequest(
            Uri requestUri,
            ExportJobType requestType,
            string resourceType = null,
            PartialDateTime since = null,
            PartialDateTime till = null,
            string filters = null,
            string groupId = null,
            string containerName = null,
            string formatName = null,
            bool isParallel = true,
            bool includeHistory = false,
            bool includeDeleted = false,
            uint maxCount = 0,
            string anonymizationConfigurationCollectionReference = null,
            string anonymizationConfigurationLocation = null,
            string anonymizationConfigurationFileETag = null)
        {
            EnsureArg.IsNotNull(requestUri, nameof(requestUri));

            RequestUri = requestUri;
            RequestType = requestType;
            ResourceType = resourceType;

            Since = since;
            Till = till;
            Filters = filters;
            AnonymizationConfigurationCollectionReference = anonymizationConfigurationCollectionReference;
            AnonymizationConfigurationLocation = anonymizationConfigurationLocation;
            AnonymizationConfigurationFileETag = anonymizationConfigurationFileETag;
            GroupId = groupId;
            ContainerName = containerName;
            FormatName = formatName;
            IsParallel = isParallel;
            IncludeHistory = includeHistory;
            IncludeDeleted = includeDeleted;
            MaxCount = maxCount;
        }

        public Uri RequestUri { get; }

        public ExportJobType RequestType { get; }

        public string ResourceType { get; }

        public PartialDateTime Since { get; }

        public PartialDateTime Till { get; }

        public string Filters { get; }

        public string AnonymizationConfigurationCollectionReference { get; }

        public string AnonymizationConfigurationLocation { get; }

        public string AnonymizationConfigurationFileETag { get; }

        public string GroupId { get; }

        public string ContainerName { get; }

        public string FormatName { get; }

        public bool IsParallel { get; }

        public bool IncludeHistory { get; }

        public bool IncludeDeleted { get; }

        public uint MaxCount { get; }
    }
}
