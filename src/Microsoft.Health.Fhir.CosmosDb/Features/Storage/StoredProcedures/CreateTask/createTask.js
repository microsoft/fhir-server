/**
* This stored procedure create a task.
*/

function createTask(taskId, queueId, taskTypeId, inputData, isUniqueTaskByType, maxRetryCount = 3) {
    const context = getContext();
    const collection = context.getCollection();
    const collectionLink = collection.getSelfLink();

    if (!taskId) {
        throwArgumentValidationError(`The required parameter 'taskId' is not specified.`);
    }

    if (!queueId) {
        throwArgumentValidationError(`The required parameter 'queueId' is not specified.`);
    }

    if (!taskTypeId) {
        throwArgumentValidationError(`The required parameter 'taskTypeId' is not specified.`);
    }

    var taskToCreate = {
        taskId: taskId,
        queueId: queueId,
        taskTypeId: parseInt(taskTypeId),
        maxRetryCount: parseInt(maxRetryCount),
        inputData: inputData,
        heartbeatDateTime: new Date(),
        status: 1,
        retryCount: 0,
        isCanceled: 0
    };

    var query = {};
    if (!isUniqueTaskByType) {
        query = {
            query: 'SELECT VALUE COUNT(1) FROM ROOT r WHERE r.taskId = @taskId or (r.taskTypeId = @taskTypeId and r.status <> 3)',
            parameters: [{ 'name': '@taskId', 'value': taskId }, { 'name': '@taskTypeId', 'value': taskTypeId }]
        };
    } else {
        query = {
            query: 'SELECT VALUE COUNT(1) FROM ROOT r WHERE r.taskId = @taskId',
            parameters: [{ 'name': '@taskId', 'value': taskId }]
        };
    }

    let isQueryAccepted = collection.queryDocuments(
        collectionLink,
        query,
        {},
        function (err, resources) {
            if (err) {
                throw err;
            }

            let numberOfExistingTask = resources[0];
            if (numberOfExistingTask > 0) {
                throw new Error(`The task with id ${taskId} alredy exists.`);
            }

            tryCreateTask(taskToCreate);
        });

    if (!isQueryAccepted) {
        // We ran out of time.
        throwTooManyRequestsError();
    }
}

function tryCreateTask(taskToCreate) {
    const context = getContext();
    const collection = context.getCollection();
    const collectionLink = collection.getSelfLink();

    var accepted = collection.createDocument(collectionLink,
        taskToCreate,
        function (err, documentCreated) {
            if (err) throw new Error('Error' + err.message);
            context.getResponse().setBody(documentCreated)
        });

    if (!accepted) {
        throwTooManyRequestsError();
    }
}
