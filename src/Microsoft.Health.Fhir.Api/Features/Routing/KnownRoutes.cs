// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using OperationsConstants = Microsoft.Health.Fhir.Core.Features.Operations.OperationsConstants;

namespace Microsoft.Health.Fhir.Api.Features.Routing
{
    internal class KnownRoutes
    {
        internal const string ResourceTypeRouteConstraint = "fhirResource";
        internal const string ResourceIdRouteConstraint = "fhirResourceId";
        internal const string CompartmentResourceTypeRouteConstraint = "fhirCompartmentResource";
        internal const string CompartmentTypeRouteConstraint = "fhirCompartment";

        private const string ResourceTypeRouteSegment = "{" + KnownActionParameterNames.ResourceType + ":" + ResourceTypeRouteConstraint + "}";
        private const string CompartmentResourceTypeRouteSegment = "{" + KnownActionParameterNames.ResourceType + ":" + CompartmentResourceTypeRouteConstraint + "}";
        private const string CompartmentTypeRouteSegment = "{" + KnownActionParameterNames.CompartmentType + ":" + CompartmentTypeRouteConstraint + "}";
        private const string IdRouteSegment = "{" + KnownActionParameterNames.Id + ":" + ResourceIdRouteConstraint + "}";
        private const string VidRouteSegment = "{" + KnownActionParameterNames.Vid + "}";

        public const string History = "_history";
        public const string Search = "_search";
        public const string ResourceType = ResourceTypeRouteSegment;
        public const string ResourceTypeHistory = ResourceType + "/" + History;
        public const string ResourceTypeSearch = ResourceType + "/" + Search;
        public const string ResourceTypeById = ResourceType + "/" + IdRouteSegment;
        public const string ResourceTypeByIdHistory = ResourceTypeById + "/" + History;
        public const string ResourceTypeByIdAndVid = ResourceTypeByIdHistory + "/" + VidRouteSegment;

        public const string Export = "$export";
        public const string ExportResourceType = ResourceType + "/" + Export;
        public const string ExportResourceTypeById = ResourceTypeById + "/" + Export;
        public const string ExportJobLocation = OperationsConstants.Operations + "/" + OperationsConstants.Export + "/" + IdRouteSegment;

        public const string Validate = "$validate";
        public const string ValidateResourceType = ResourceType + "/" + Validate;
        public const string ValidateResourceTypeById = ResourceTypeById + "/" + Validate;

        public const string Reindex = "$reindex";
        public const string ReindexSingleResource = ResourceTypeById + "/" + Reindex;
        public const string ReindexJobLocation = OperationsConstants.Operations + "/" + OperationsConstants.Reindex + "/" + IdRouteSegment;

        public const string CompartmentTypeByResourceType = CompartmentTypeRouteSegment + "/" + IdRouteSegment + "/" + CompartmentResourceTypeRouteSegment;

        public const string Metadata = "metadata";

        public const string Operation = "${" + KnownActionParameterNames.Operation + "}";

        public const string HealthCheck = "/health/check";
        public const string CustomError = "/CustomError";
    }
}
