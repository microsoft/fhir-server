function getNextTask(queueId, count, taskHeartbeatTimeoutThresholdInSeconds = 600) {
    if (!queueId) {
        throwArgumentValidationError(`The required parameter 'taskId' is not specified.`);
    }

    if (!count) {
        throwArgumentValidationError(`The required parameter 'taskId' is not specified.`);
    }

    if (!taskHeartbeatTimeoutThresholdInSeconds) {
        throwArgumentValidationError(`The required parameter 'taskHeartbeatTimeoutThresholdInSeconds' is not specified.`);
    }

    const context = getContext();
    const collection = getContext().getCollection();
    count = parseInt(count)
    taskHeartbeatTimeoutThresholdInSeconds = parseInt(taskHeartbeatTimeoutThresholdInSeconds)
    var expirationDateTime = new Date()
    expirationDateTime.setSeconds(expirationDateTime.getSeconds() - taskHeartbeatTimeoutThresholdInSeconds);
    tryQueryAndUpdate();

    function tryQueryAndUpdate(continuation) {
        let query = {
            query: 'SELECT TOP @count * FROM ROOT r WHERE r.queueId = @queueId and \
                (r.status = 1 or (r.status = 2 and r.heartbeatDateTime < @expirationDateTime)) \
                ORDER BY r.heartbeatDateTime',
            parameters: [
                { 'name': '@queueId', 'value': queueId },
                { 'name': '@count', 'value': count },
                { 'name': '@expirationDateTime', 'value': expirationDateTime }]
        };

        let isQueryAccepted = collection.queryDocuments(
            collection.getSelfLink(),
            query,
            {},
            function (err, documents) {
                if (err) throw err;
                if (documents.length > 0) {
                    // If at least one document is found, update it.
                    docCount = documents.length;
                    for (var i = 0; i < docCount; i++) {
                        tryUpdateTask(documents[i]);
                    }

                    context.getResponse().setBody(documents)
                }
            });

        if (!isQueryAccepted) {
            // We ran out of time.
            throwTooManyRequestsError();
        }
    }

    function tryUpdateTask(documentToUpdate) {
        documentToUpdate.heartbeatDateTime = new Date()
        documentToUpdate.runId = uuidv4()
        var accept = collection.replaceDocument(documentToUpdate._self, documentToUpdate,
            function (err, documentToUpdate) {
                if (err) throw "Unable to keep alive task ";
            });

        if (!accept) throw "Unable to keep alive, abort";
    }

    function uuidv4() {
        return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, function (c) {
            var r = Math.random() * 16 | 0, v = c == 'x' ? r : (r & 0x3 | 0x8);
            return v.toString(16);
        });
    }
}
