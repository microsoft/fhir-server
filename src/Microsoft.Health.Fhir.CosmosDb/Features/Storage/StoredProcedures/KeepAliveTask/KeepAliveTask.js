function keepAliveTask(taskId, runId) {
    // Validate input
    if (!taskId) {
        throwArgumentValidationError(`The required parameter 'taskId' is not specified.`);
    }

    if (!runId) {
        throwArgumentValidationError(`The required parameter 'taskId' is not specified.`);
    }

    const context = getContext();
    const collection = getContext().getCollection();

    let query = {
        query: 'SELECT * FROM ROOT r WHERE r.taskInfo.taskId = @taskId and r.taskInfo.runId = @runId',
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

            tryKeepAlive(resources[0])
        });

    if (!isQueryAccepted) {
        // We ran out of time.
        throwTooManyRequestsError();
    }

    function tryKeepAlive(documentToUpdate) {
        documentToUpdate.taskInfo.heartbeatDateTime = new Date()
        var accept = collection.replaceDocument(documentToUpdate._self, documentToUpdate,
            function (err, documentUpdated) {
                if (err) throw "Unable to keep alive task ";
                context.getResponse().setBody(documentUpdated)
            });

        if (!accept) throw "Unable to keep alive, abort";
    }
}
