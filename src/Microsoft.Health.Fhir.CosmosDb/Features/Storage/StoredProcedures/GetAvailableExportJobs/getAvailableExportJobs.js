/**
* This stored procedure provides the following functionality:
* - Retrieve list of available export jobs.
* - The script determines list of available export jobs by looking at the list of running jobs as well as maximumNumberOfConcurrentJobsAllowed.
* 
* @constructor
* @param {string} maximumNumberOfConcurrentJobsAllowedInString - The maximum number of concurrent jobs allowed in string.
* @param {string} jobHeartbeatTimeoutThresholdInSecondsInString - The number of seconds allowed before the job is considered to be stale in string.
*/

function getAvailableExportJobs(maximumNumberOfConcurrentJobsAllowedInString, jobHeartbeatTimeoutThresholdInSecondsInString) {
    const collection = getContext().getCollection();
    const collectionLink = collection.getSelfLink();
    const response = getContext().getResponse();

    const errorMessages = {
        InvalidMaximumNumberOfConcurrentJobsAllowedInString: `${ErrorCodes.BadRequest}: The specified maximumNumberOfConcurrentJobsAllowedInString with value '${maximumNumberOfConcurrentJobsAllowedInString}' is invalid.`,
        InvalidJobHeartbeatTimeoutThresholdInSecondsInString: `${ErrorCodes.BadRequest}: The specified jobHeartbeatTimeoutThresholdInSecondsInString with value '${jobHeartbeatTimeoutThresholdInSecondsInString}' is invalid.`,
        RequestEntityTooLarge: `${ErrorCodes.RequestEntityTooLarge}: The request could not be completed.`
    };

    // Validate input
    if (!maximumNumberOfConcurrentJobsAllowedInString) {
        throw new Error(errorMessages.InvalidMaximumNumberOfConcurrentJobsAllowedInString);
    }

    let maximumNumberOfConcurrentJobsAllowed = parseInt(maximumNumberOfConcurrentJobsAllowedInString);

    if (maximumNumberOfConcurrentJobsAllowed <= 0) {
        throw new Error(errorMessages.InvalidMaximumNumberOfConcurrentJobsAllowed);
    }

    if (!jobHeartbeatTimeoutThresholdInSecondsInString) {
        throw new Error(errorMessages.InvalidJobHeartbeatTimeoutThresholdInSecondsInString);
    }

    let jobHeartbeatTimeoutThresholdInSeconds = parseInt(jobHeartbeatTimeoutThresholdInSecondsInString);

    if (jobHeartbeatTimeoutThresholdInSeconds <= 0) {
        throw new Error(errorMessages.InvalidJobHeartbeatTimeoutThresholdInSecondsInString);
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
                
                tryQueryAvailableJobs(numberOfRunningJobs);
            });

        if (!isQueryAccepted) {
            // We ran out of time.
            throw new Error(errorMessage.requestEntityTooLarge);
        }
    }

    function tryQueryAvailableJobs(numberOfRunningJobs) {
        let limit = maximumNumberOfConcurrentJobsAllowed - numberOfRunningJobs;

        if (limit < 0) {
            limit = 0;
        }

        let query = {
            query: `SELECT TOP ${limit} * FROM ROOT r WHERE (r.jobRecord.status = 'Queued' OR (r.jobRecord.status = 'Running' AND r._ts <= ${expirationTime})) ORDER BY r._ts ASC`
        };

        let isQueryAccepted = collection.queryDocuments(
            collectionLink,
            query,
            {},
            function (err, resources) {
                if (err) {
                    throw err;
                }

                response.setBody(resources); 
            });

        if (!isQueryAccepted) {
            // We ran out of time.
            throw new Error(errorMessage.requestEntityTooLarge);
        }
    }
}
