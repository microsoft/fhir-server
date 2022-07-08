/**
* This stored procedure acquires list of available export jobs.
*
* @constructor
* @param {string} numberOfJobs - The number of jobs to fetch in string.
* @param {string} jobHeartbeatTimeoutThresholdInSecondsInString - The number of seconds allowed before the job is considered to be stale in string.
*/

function acquireExportJobs(numberOfJobs, jobHeartbeatTimeoutThresholdInSecondsInString) {
    const collection = getContext().getCollection();
    const collectionLink = collection.getSelfLink();
    const response = getContext().getResponse();

    // Validate input
    if (!numberOfJobs) {
        throwArgumentValidationError(`The required parameter 'numberOfJobs' is not specified.`);
    }

    let numberOfJobsToAcquire = parseInt(numberOfJobs);

    if (numberOfJobsToAcquire <= 0) {
        throwArgumentValidationError(`The specified numberOfJobs with value '${numberOfJobs}' is invalid.`);
    }

    if (!jobHeartbeatTimeoutThresholdInSecondsInString) {
        throwArgumentValidationError(`The required parameter 'jobHeartbeatTimeoutThresholdInSecondsInString' is not specified.`);
    }

    let jobHeartbeatTimeoutThresholdInSeconds = parseInt(jobHeartbeatTimeoutThresholdInSecondsInString);

    if (jobHeartbeatTimeoutThresholdInSeconds <= 0) {
        throwArgumentValidationError(`The specified jobHeartbeatTimeoutThresholdInSecondsInString with value '${jobHeartbeatTimeoutThresholdInSecondsInString}' is invalid.`);
    }

    // Calculate the expiration time in seconds where the job is considered to be stale.
    let expirationTime = new Date().setMilliseconds(0) / 1000 - jobHeartbeatTimeoutThresholdInSeconds;

    tryQueryAvailableJobs(numberOfJobsToAcquire);

    function tryQueryAvailableJobs(limit, continuation) {
        if (limit < 0) {
            limit = 0;
        }

        let query = {
            query: `SELECT TOP ${limit} * FROM ROOT r WHERE (r.jobRecord.status = 'Queued' OR (r.jobRecord.status = 'Running' AND r._ts <= ${expirationTime})) ORDER BY r._ts ASC`
        };

        let requestOptions = {
            continuation: continuation
        };

        let isQueryAccepted = collection.queryDocuments(
            collectionLink,
            query,
            requestOptions,
            function (err, documents, responseOptions) {
                if (err) {
                    throw err;
                }

                if (documents.length > 0) {
                    // Update each documents.
                    tryAcquire(documents, 0);
                } else if (responseOptions.continuation) {
                    // The query came back with empty result but has continuation token, follow the token.
                    tryQueryAvailableJobs(limit, responseOptions.continuation);
                } else {
                    // We don't have any documents so we are done.
                    response.setBody([]);
                }
            });

        if (!isQueryAccepted) {
            // We ran out of time.
            throwTooManyRequestsError();
        }
    }

    function tryAcquire(documents, index) {
        if (documents.length === index) {
            // Finished acquiring all jobs.
            response.setBody(documents);
        } else {
            let document = documents[index];

            let requestOptions = {
                etag: document._etag
            };

            // Update the state.
            document.jobRecord.status = 'Running';

            let isQueryAccepted = collection.replaceDocument(
                document._self,
                document,
                requestOptions,
                function (err, updatedDocument) {
                    if (err) {
                        throw err;
                    }

                    documents[index] = updatedDocument;
                    tryAcquire(documents, index + 1);
                });

            if (!isQueryAccepted) {
                // We ran out of time.
                throwTooManyRequestsError();
            }
       }
    }

    function throwArgumentValidationError(message) {
        throw new Error(ErrorCodes.BadRequest, message);
    }

    function throwTooManyRequestsError() {
        throw new Error(429, `The request could not be completed.`);
    }
}
