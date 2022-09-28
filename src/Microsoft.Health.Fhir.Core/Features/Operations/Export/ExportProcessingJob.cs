using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.JobManagement;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export
{
    class ExportProcessingJob : IJob
    {
        private JobInfo _jobInfo;
        private Func<IExportJobTask> _exportJobTaskFactory;
        private IProgress<string> _progress;
        private ExportJobRecord _record;

        public ExportProcessingJob(JobInfo jobInfo, Func<IExportJobTask> exportJobTaskFactory)
        {
            _jobInfo = jobInfo;
            _exportJobTaskFactory = exportJobTaskFactory;
        }

        public Task<string> ExecuteAsync(IProgress<string> progress, CancellationToken cancellationToken)
        {
            _progress = progress;
            ExportJobRecord record = JsonConvert.DeserializeObject<ExportJobRecord>(string.IsNullOrEmpty(_jobInfo.Result) ? _jobInfo.Definition : _jobInfo.Result);
            IExportJobTask exportJobTask = _exportJobTaskFactory();

            // The ExportJobTask was used to handling the database updates and etags itself, but the new job hosting flow manages it in a central location.
            // This method allows the same class to be used in both Cosmos (with the old method) and SQL (with the new method).
            // The etag passed to the ExportJobTask is unused, the actual etag is managed in the JobHosting class.
            exportJobTask.UpdateExportJob = UpdateExportJob;
            Task exportTask = exportJobTask.ExecuteAsync(record, WeakETag.FromVersionId("0"), cancellationToken);
            return exportTask.ContinueWith(
                (Task parent) =>
                {
                    switch (_record.Status)
                    {
                        case OperationStatus.Completed:
                            return JsonConvert.SerializeObject(_record);
                        case OperationStatus.Failed:
                            throw new JobExecutionException(_record.FailureDetails.FailureReason);
                        case OperationStatus.Canceled:
                        case OperationStatus.Queued:
                        case OperationStatus.Running:
                            throw new RetriableJobException("Export job failed. See logs from ExportJobTask.");
                        default:
                            throw new Exception("Job status not set.");
                    };
                }, cancellationToken);
        }

        private Task<ExportJobOutcome> UpdateExportJob(ExportJobRecord exportJobRecord, WeakETag weakETag, CancellationToken cancellationToken)
        {
            _record = exportJobRecord;
            _progress.Report(JsonConvert.SerializeObject(exportJobRecord));
            return Task.FromResult(new ExportJobOutcome(exportJobRecord, weakETag));
        }
    }
}
