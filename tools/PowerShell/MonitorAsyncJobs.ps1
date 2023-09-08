function RunJobMonitor($SQLServer, $SQLDBName, $uid, $pwd, $QueueType, $GroupId)
{
	while($true)
	{
		CheckAsyncJob -SqlServer $SQLServer -SQLDBName $SQLDBName -uid $uid -pwd $pwd -QueueType $QueueType -GroupId $GroupId
		Start-Sleep -Second 300
	}
}

function CheckAsyncJob($SQLServer, $SQLDBName, $uid, $pwd, $QueueType, $GroupId)
{
	$SqlQuery = "SELECT JobId, Status, CreateDate, StartDate, EndDate, HeartbeatDate from JobQueue where QueueType = $QueueType and GroupId = $GroupId;";
	$SqlConnection = New-Object System.Data.SqlClient.SqlConnection;
	$SqlConnection.ConnectionString = "Server = $SQLServer; Database = $SQLDBName; Integrated Security = False; User ID = $uid; Password = $pwd;";
	$SqlCmd = New-Object System.Data.SqlClient.SqlCommand;
	$SqlCmd.CommandText = $SqlQuery;
	$SqlCmd.Connection = $SqlConnection;
	$SqlAdapter = New-Object System.Data.SqlClient.SqlDataAdapter;
	$SqlAdapter.SelectCommand = $SqlCmd;
	$DataSet = New-Object System.Data.DataSet;
	$SqlAdapter.Fill($DataSet);

	$createTime = $DataSet.Tables[0].Rows[0].CreateDate
	$DataSet.Tables[0].Rows | Foreach-Object {
		if($createTime -ge $_.CreateDate) {
			$createTime = $_.CreateDate
		}
	}

	$endTime = $createTime
	$DataSet.Tables[0].Rows | Foreach-Object {
		if($_.Status -ne 0) {
			if($endTime -le $_.HeartbeatDate) {
				$endTime = $_.HeartbeatDate
			}
		}
	}

	$queued = 0
	$running = 0
	$succeeded = 0
	$failed = 0
	$cancelled = 0
	$DataSet.Tables[0].Rows | Foreach-Object {
		switch($_.Status) {
			0 { $queued++ }
			1 { $running++ }
			2 { $succeeded++ }
			3 { $failed++ }
			4 { $cancelled++ }
		}
	}

	$total = $DataSet.Tables[0].Rows.Count
	$finished = $succeeded + $failed + $cancelled

	$runTime = $endTime-$createTime
	$runSeconds = $runTime.TotalSeconds
	
	$estimatedTotalRunSeconds = $runSeconds*$total/$finished 

	Write-Host ""
	Write-Host "Progress: `t$finished/$total jobs finished"
	Write-Host ""
	Write-Host "Queued: `t$queued"
	Write-Host "Running: `t$running"
	Write-Host "Succeeded: `t$succeeded"
	Write-Host "Failed: `t$failed"
	Write-Host "Cancelled: `t$cancelled"
	Write-Host ""
	Write-Host "Create Time: `t$createTime"
	Write-Host "End Time: `t$endTime"
	Write-Host ""
	Write-Host "Run Duration (sec): `t`t$runSeconds" 
	Write-Host "Estimated Duration (sec): `t$estimatedTotalRunSeconds"
}