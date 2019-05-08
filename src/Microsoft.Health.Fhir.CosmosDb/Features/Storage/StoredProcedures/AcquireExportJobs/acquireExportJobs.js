/**
* This stored procedure acquires list of available export jobs. 
* 
* @constructor
* @param {string} maximumNumberOfConcurrentJobsAllowedInString - The maximum number of concurrent jobs allowed in string.
* @param {string} jobHeartbeatTimeoutThresholdInSecondsInString - The number of seconds allowed before the job is considered to be stale in string.
*/

function acquireExportJobs(maximumNumberOfConcurrentJobsAllowedInString, jobHeartbeatTimeoutThresholdInSecondsInString) {
    const collection = getContext().getCollection();
    const collectionLink = collection.getSelfLink();
    const response = getContext().getResponse();

    // Validate input
    if (!maximumNumberOfConcurrentJobsAllowedInString) {
        throwArgumentValidationError(`The required parameter 'maximumNumberOfConcurrentJobsAllowedInString' is not specified.`);
    }

    let maximumNumberOfConcurrentJobsAllowed = parseInt(maximumNumberOfConcurrentJobsAllowedInString);

    if (maximumNumberOfConcurrentJobsAllowed <= 0) {
        throwArgumentValidationError(`The specified maximumNumberOfConcurrentJobsAllowedInString with value '${maximumNumberOfConcurrentJobsAllowedInString}' is invalid.`);
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

    tryQueryRunningJobs();

    function tryQueryRunningJobs() {
        // Find list of active running jobs.
        let query = {
            query: `SELECT VALUE COUNT(1) FROM ROOT r WHERE r.jobRecord.status = 'Running' AND r._ts > ${expirationTime}`
        };

        let isQueryAccepted = collection.queryDocuments(
            collectionLink,
            query,
            {},
            function (err, resources) {
                if (err) {
                    throw err;
                }

                let numberOfRunningJobs = resources[0];

                // Based on list of running jobs, query for list of available jobs.
                tryQueryAvailableJobs(numberOfRunningJobs);
            });

        if (!isQueryAccepted) {
            // We ran out of time.
            throwTooManyRequestsError();
        }
    }

    function tryQueryAvailableJobs(numberOfRunningJobs, continuation) {
        let limit = maximumNumberOfConcurrentJobsAllowed - numberOfRunningJobs;

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
                    tryQueryAvailableJobs(numberOfRunningJobs, responseOptions.continuation);
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
        throw new Error(ErrorCodes.RequestEntityTooLarge, `The request could not be completed.`);
    }
}
