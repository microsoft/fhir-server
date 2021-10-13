function getTask(taskId) {
    // Validate input
    if (!taskId) {
        throwArgumentValidationError(`The required parameter 'taskId' is not specified.`);
    }

    const context = getContext();
    var collection = getContext().getCollection();

    let query = {
        query: 'SELECT * FROM ROOT r WHERE r.taskInfo.taskId = @taskId',
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
