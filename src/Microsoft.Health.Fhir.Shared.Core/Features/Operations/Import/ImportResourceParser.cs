// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using EnsureThat;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Core.Extensions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Resources;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    public class ImportResourceParser : IImportResourceParser
    {
        private static readonly Regex ResourceIdValidationRegex = new Regex(
            "^[A-Za-z0-9\\-\\.]{1,64}$",
            RegexOptions.Compiled);

        private FhirJsonParser _parser;
        private IResourceWrapperFactory _resourceFactory;

        public ImportResourceParser(FhirJsonParser parser, IResourceWrapperFactory resourceFactory)
        {
            _parser = EnsureArg.IsNotNull(parser, nameof(parser));
            _resourceFactory = EnsureArg.IsNotNull(resourceFactory, nameof(resourceFactory));
        }

        public ImportResource Parse(long index, long offset, int length, string rawResource, ImportMode importMode)
        {
            var resource = _parser.Parse<Resource>(rawResource);
            ValidateResourceId(resource?.Id);
            CheckConditionalReferenceInResource(resource, importMode);

            if (resource.Meta == null)
            {
                resource.Meta = new Meta();
            }

            var lastUpdatedIsNull = importMode == ImportMode.InitialLoad || resource.Meta.LastUpdated == null;
            var lastUpdated = lastUpdatedIsNull ? Clock.UtcNow : resource.Meta.LastUpdated.Value;
            resource.Meta.LastUpdated = new DateTimeOffset(lastUpdated.DateTime.TruncateToMillisecond(), lastUpdated.Offset);
            if (!lastUpdatedIsNull && resource.Meta.LastUpdated.Value > Clock.UtcNow.AddSeconds(10)) // 5 sec is the max for the computers in the domain
            {
                throw new NotSupportedException("LastUpdated in the resource cannot be in the future.");
            }

            var keepVersion = true;
            if (lastUpdatedIsNull || string.IsNullOrEmpty(resource.Meta.VersionId) || !int.TryParse(resource.Meta.VersionId, out var _))
            {
                resource.Meta.VersionId = "1";
                keepVersion = false;
            }

            var resourceElement = resource.ToResourceElement();

            var isDeleted = resourceElement.IsSoftDeleted();

            if (isDeleted)
            {
                resource.Meta.RemoveExtension(KnownFhirPaths.AzureSoftDeletedExtensionUrl);
            }

            var resourceWapper = _resourceFactory.Create(resourceElement, isDeleted, true, keepVersion);

            return new ImportResource(index, offset, length, !lastUpdatedIsNull, keepVersion, isDeleted, resourceWapper);
        }

        private static void CheckConditionalReferenceInResource(Resource resource, ImportMode importMode)
        {
            if (importMode == ImportMode.IncrementalLoad)
            {
                return;
            }

            IEnumerable<ResourceReference> references = resource.GetAllChildren<ResourceReference>();
            foreach (ResourceReference reference in references)
            {
                if (string.IsNullOrWhiteSpace(reference.Reference))
                {
                    continue;
                }

                if (reference.Reference.Contains('?', StringComparison.Ordinal))
                {
                    throw new NotSupportedException($"Conditional reference is not supported for $import in {ImportMode.InitialLoad}.");
                }
            }
        }

        private static void ValidateResourceId(string resourceId)
        {
            if (string.IsNullOrWhiteSpace(resourceId) || !ResourceIdValidationRegex.IsMatch(resourceId))
            {
                throw new BadRequestException($"Invalid resource id: '{resourceId ?? "null or empty"}'. " + Core.Resources.IdRequirements);
            }
        }
    }
}
