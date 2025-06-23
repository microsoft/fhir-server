DECLARE @ErrorMessage NVARCHAR(200) = 'Your reindex job has been canceled during an upgrade. Please resubmit a new one.';

UPDATE [dbo].[ReindexJob]
SET 
    [Status] = 'Canceled',
	RawJobRecord = JSON_MODIFY(RawJobRecord, '$.error', @ErrorMessage)
WHERE [Status] NOT IN ('Completed', 'Failed', 'Canceled');