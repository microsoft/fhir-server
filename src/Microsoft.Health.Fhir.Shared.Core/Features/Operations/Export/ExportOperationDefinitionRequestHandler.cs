// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Serialization;
using MediatR;
using Microsoft.Health.Fhir.Core.Data;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Messages.Export;
using Microsoft.Health.Fhir.Core.Models;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export
{
    public class ExportOperationDefinitionRequestHandler : IRequestHandler<ExportOperationDefinitionRequest, ExportOperationDefinitionResponse>
    {
        private readonly IModelInfoProvider _modelInfoProvider;

        public ExportOperationDefinitionRequestHandler(IModelInfoProvider modelInfoProvider)
        {
            EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));

            _modelInfoProvider = modelInfoProvider;
        }

        public Task<ExportOperationDefinitionResponse> Handle(ExportOperationDefinitionRequest request, CancellationToken cancellationToken)
        {
            using Stream stream = DataLoader.OpenOperationDefinitionFileStream($"{request.Route}.json");
            using TextReader reader = new StreamReader(stream);
            using JsonReader jsonReader = new JsonTextReader(reader);

            ISourceNode result = FhirJsonNode.Read(jsonReader);
            ITypedElement operationDefinition = result.ToTypedElement(_modelInfoProvider.StructureDefinitionSummaryProvider);

            return Task.FromResult(new ExportOperationDefinitionResponse(operationDefinition.ToResourceElement()));
        }
    }
}
