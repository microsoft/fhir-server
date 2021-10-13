function getTask(taskId) {
    // Validate input
    if (!taskId) {
        throwArgumentValidationError(`The required parameter 'taskId' is not specified.`);
    }

    const context = getContext();
    var collection = getContext().getCollection();

    let query = {
        query: 'SELECT r.taskId, r.queueId, r.status, r.taskTypeId, r.runId, r.isCanceled, r.retryCount, r.maxRetryCount, r.heartbeatDateTime,\
                r.inputData, r.taskContext, r.result FROM ROOT r WHERE r.taskId = @taskId',
        parameters: [{ 'name': '@taskId', 'value': taskId }]
    };

    let isQueryAccepted = collection.queryDocuments(
        collection.getSelfLink(),
        query,
        {},
        function (err, resources) {
            if (err) {
                throw err;
            }

            context.getResponse().setBody(resources[0])
        });

    if (!isQueryAccepted) {
        // We ran out of time.
        throwTooManyRequestsError();
    }
}
