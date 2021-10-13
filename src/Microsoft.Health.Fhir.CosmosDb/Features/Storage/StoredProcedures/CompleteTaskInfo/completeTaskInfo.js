function completeTaskInfo(taskId, runId, taskResult) {
    // Validate input
    if (!taskId) {
        throwArgumentValidationError(`The required parameter 'taskId' is not specified.`);
    }

    if (!runId) {
        throwArgumentValidationError(`The required parameter 'taskId' is not specified.`);
    }

    const contxet = getContext();
    const collection = getContext().getCollection();

    let query = {
        query: 'SELECT * FROM ROOT r WHERE r.taskId = @taskId and r.runId = @runId',
        parameters: [{ 'name': '@taskId', 'value': taskId }, { 'name': '@runId', 'value': runId }]
    };

    let isQueryAccepted = collection.queryDocuments(
        collection.getSelfLink(),
        query,
        {},
        function (err, resources) {
            if (err) {
                throw err;
            }

            if (resources.length < 1) {
                throw (`Non-exist task ${taskId}.`);
            }

            tryCompleteContext(resources[0])
        });

    if (!isQueryAccepted) {
        // We ran out of time.
        throwTooManyRequestsError();
    }

    function tryCompleteContext(documentToUpdate) {
        // Find list of active running jobs.
        documentToUpdate.heartbeatDateTime = new Date()
        documentToUpdate.result = taskResult
        documentToUpdate.status = 3

        var accept = collection.replaceDocument(documentToUpdate._self, documentToUpdate,
            function (err, documentUpdated) {
                if (err) throw "Unable to complete task ";
                getContext().getResponse().setBody(documentUpdated)
            });

        if (!accept) throw "Unable to complete, abort";
    }
}
