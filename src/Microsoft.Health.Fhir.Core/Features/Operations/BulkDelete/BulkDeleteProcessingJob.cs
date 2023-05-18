// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.BulkDelete;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.JobManagement;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Api.Features.Operations.BulkDelete
{
    [JobTypeId((int)JobType.BulkDeleteProcessing)]
    public class BulkDeleteProcessingJob : IJob
    {
        private readonly Func<IScoped<IDeleter>> _deleterFactory;

        public BulkDeleteProcessingJob(Func<IScoped<IDeleter>> deleterFactory)
        {
            _deleterFactory = deleterFactory;
        }

        public async Task<string> ExecuteAsync(JobInfo jobInfo, IProgress<string> progress, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(jobInfo, nameof(jobInfo));
            EnsureArg.IsNotNull(progress, nameof(progress));

            try
            {
                BulkDeleteDefinition definition = JsonConvert.DeserializeObject<BulkDeleteDefinition>(jobInfo.Definition);

                using IScoped<IDeleter> deleter = _deleterFactory();
                var itemsDeleted = await deleter.Value.DeleteMultipleAsync(new ConditionalDeleteResourceRequest(definition.Type, definition.SearchParameters, definition.DeleteOperation, -1), cancellationToken);
                var result = new BulkDeleteResult();
                result.ResourcesDeleted.Add(definition.Type, itemsDeleted);
                return JsonConvert.SerializeObject(result);
            }
            catch (Exception)
            {
                throw;
            }
        }
    }
}
