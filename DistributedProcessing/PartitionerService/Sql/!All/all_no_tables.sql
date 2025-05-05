drop view if exists vw_newid
go


create view vw_newid
as
select newid() as new_id
go

drop function if exists dbo.getAvailableWorkerNode
go


--create function dbo.getAvailableWorkerNode(@lastAllowedNodeErrInMinutes int)
--returns table
--as
--return
--(
--	with a as
--	(
--	select
--		workerNodeId
--		,nodePriority
--		,cast(
--			case
--				when numberOfCores <= 0 then 1
--				else (select count(1) as runningModels from RunQueue where workerNodeId = w.workerNodeId and runQueueStatusId in (2, 5, 7)) / (cast(numberOfCores as money))
--			end as money) as workLoad
--		,case when lastErrorOn is null or dateadd(minute, @lastAllowedNodeErrInMinutes, lastErrorOn) < getdate() then 0 else 1 end as noErr
--	from WorkerNode w
--	where isInactive = 0
--	),
--	b as
--	(
--		select
--			a.*, 
--			c.new_id
--			from a
--			cross apply (select new_id from vw_newid) c
--	)
--	select top 1
--	workerNodeId
--	from b
--	where noErr = 0 and workLoad < 1
--	order by nodePriority desc, workLoad, new_id
--)
go

drop function if exists dbo.RunQueueStatus_NotStarted
go
create function dbo.RunQueueStatus_NotStarted() returns int as begin return 0 end
go
drop function if exists dbo.RunQueueStatus_Inactive
go
create function dbo.RunQueueStatus_Inactive() returns int as begin return 1 end
go
drop function if exists dbo.RunQueueStatus_RunRequested
go
create function dbo.RunQueueStatus_RunRequested() returns int as begin return 7 end
go
drop function if exists dbo.RunQueueStatus_InProgress
go
create function dbo.RunQueueStatus_InProgress() returns int as begin return 2 end
go
drop function if exists dbo.RunQueueStatus_Completed
go
create function dbo.RunQueueStatus_Completed() returns int as begin return 3 end
go
drop function if exists dbo.RunQueueStatus_Failed
go
create function dbo.RunQueueStatus_Failed() returns int as begin return 4 end
go
drop function if exists dbo.RunQueueStatus_CancelRequested
go
create function dbo.RunQueueStatus_CancelRequested() returns int as begin return 5 end
go
drop function if exists dbo.RunQueueStatus_Cancelled
go
create function dbo.RunQueueStatus_Cancelled() returns int as begin return 6 end
go

drop view if exists vw_AvailableWorkerNode
go


create view vw_AvailableWorkerNode
as
with le as
(
select
    workerNodeId
    ,solverId
    ,max(lastErrorOn) as lastErrorOn
from RunQueue r
where workerNodeId is not null and lastErrorOn is not null
group by r.workerNodeId, r.solverId
)
,a as
(
select
    w.workerNodeId
    ,ws.solverId
    ,nodePriority
    ,isnull(cast(
        case
            when numberOfCores <= 0 then 1
            else (select count(1) as runningModels from RunQueue where workerNodeId = w.workerNodeId and runQueueStatusId in (2, 5, 7)) / (cast(numberOfCores as money))
        end as money), 0) as workLoad
    ,case when le.lastErrorOn is null then null else datediff(minute, getdate(), le.lastErrorOn) end as lastErrMinAgo
from WorkerNode w
inner join WorkerNodeSolver ws on w.workerNodeId = ws.workerNodeId
left outer join le on ws.workerNodeId = le.solverId and ws.solverId = le.solverId
inner join Solver s on ws.solverId = s.solverId
where w.isInactive = 0 and ws.isDeployed = 1 and s.isInactive = 0
)
select
    a.*, 
    c.new_id as OrderId
    from a
    cross apply (select new_id from vw_newid) c

go

drop procedure if exists dbo.deleteRunQueue
go


SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

--declare @runQueueId uniqueidentifier
--set @runQueueId = newid()

create procedure dbo.deleteRunQueue @runQueueId uniqueidentifier
as
begin
    set nocount on;

    declare @rowCount int;
    declare @sql nvarchar(max);

    -- Generate the delete statements for all tables with a foreign key referencing dbo.RunQueue.runQueueId
    select @sql = STRING_AGG(
        'delete from ' + QUOTENAME(OBJECT_SCHEMA_NAME(fk.parent_object_id)) + '.' + QUOTENAME(OBJECT_NAME(fk.parent_object_id)) + ' where runQueueId = @runQueueId;', 
        CHAR(13) + CHAR(10)
    )
    from 
        sys.foreign_keys as fk
        inner join sys.foreign_key_columns as fkc
            on fk.object_id = fkc.constraint_object_id
        inner join sys.columns as c
            on fkc.parent_column_id = c.column_id and fkc.parent_object_id = c.object_id
        inner join sys.tables as t
            on fk.parent_object_id = t.object_id
    where 
        fk.referenced_object_id = object_id('dbo.RunQueue')
        and c.name = 'runQueueId';

    -- Execute the dynamic SQL if there are any delete statements
    if @sql is not null and @sql <> ''
    begin
        --print @sql
        exec sp_executesql @sql, N'@runQueueId uniqueidentifier', @runQueueId;
    end

    -- Delete from dbo.RunQueue and capture the row count
    delete from dbo.RunQueue where runQueueId = @runQueueId;
    set @rowCount = @@rowcount;

    -- Return the number of rows affected by the delete from dbo.RunQueue
    select @rowCount as [RowCount];
end
go

drop procedure if exists dbo.tryCancelRunQueue
go


SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO


create procedure dbo.tryCancelRunQueue (@runQueueId uniqueidentifier, @errorMessage nvarchar(max) = null)
as
begin
	declare @rowCount int
	set nocount on;

	update dbo.RunQueue
	set
		runQueueStatusId = dbo.RunQueueStatus_Cancelled(),
		processId = null,
		modifiedOn = (getdate()),
		errorMessage = @errorMessage
	where runQueueId = @runQueueId and runQueueStatusId in (dbo.RunQueueStatus_NotStarted(), dbo.RunQueueStatus_InProgress(), dbo.RunQueueStatus_CancelRequested())

	set @rowCount = @@rowcount
	select @rowCount as [RowCount]
end
go

drop procedure if exists dbo.tryClearNotificationRunQueue
go


SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO


create procedure dbo.tryClearNotificationRunQueue @runQueueId uniqueidentifier
as
begin
	declare @rowCount int
	set nocount on;

    update dbo.RunQueue
    set
        notificationTypeId = 0,
        modifiedOn = (getdate())
    where runQueueId = @runQueueId and runQueueStatusId in (dbo.RunQueueStatus_InProgress())

	set @rowCount = @@rowcount
	select @rowCount as [RowCount]
end
go

drop procedure if exists dbo.tryCompleteRunQueue
go


SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO


create procedure dbo.tryCompleteRunQueue @runQueueId uniqueidentifier
as
begin
	declare @rowCount int
	set nocount on;

	update dbo.RunQueue
	set
		runQueueStatusId = dbo.RunQueueStatus_Completed(),
		processId = null,
		modifiedOn = (getdate())
	where runQueueId = @runQueueId and processId is not null and runQueueStatusId in (dbo.RunQueueStatus_InProgress(), dbo.RunQueueStatus_CancelRequested())

	set @rowCount = @@rowcount
	select @rowCount as [RowCount]
end
go

drop procedure if exists dbo.tryFailRunQueue
go


SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO


create procedure dbo.tryFailRunQueue (@runQueueId uniqueidentifier, @errorMessage nvarchar(max) = null)
as
begin
	declare @rowCount int
	set nocount on;

    update dbo.RunQueue
    set
        runQueueStatusId = dbo.RunQueueStatus_Failed(),
        processId = null,
        modifiedOn = (getdate()),
        errorMessage = @errorMessage
    where runQueueId = @runQueueId and runQueueStatusId in (dbo.RunQueueStatus_InProgress(), dbo.RunQueueStatus_CancelRequested())

	set @rowCount = @@rowcount
	select @rowCount as [RowCount]
end
go

drop procedure if exists dbo.tryNotifyRunQueue
go


SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO


create procedure dbo.tryNotifyRunQueue (@runQueueId uniqueidentifier, @notificationTypeId int)
as
begin
	declare @rowCount int
	set nocount on;

    update dbo.RunQueue
    set
        notificationTypeId = @notificationTypeId,
        modifiedOn = (getdate())
    where runQueueId = @runQueueId and runQueueStatusId in (dbo.RunQueueStatus_InProgress(), dbo.RunQueueStatus_CancelRequested())

	set @rowCount = @@rowcount
	select @rowCount as [RowCount]
end
go

drop procedure if exists dbo.tryRequestCancelRunQueue
go


SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO


create procedure dbo.tryRequestCancelRunQueue (@runQueueId uniqueidentifier, @notificationTypeId int)
as
begin
	declare @rowCount int
	set nocount on;

    update dbo.RunQueue
    set
        runQueueStatusId = dbo.RunQueueStatus_CancelRequested(),
        notificationTypeId = @notificationTypeId,
        modifiedOn = (getdate())
    where runQueueId = @runQueueId and runQueueStatusId = dbo.RunQueueStatus_InProgress()

	set @rowCount = @@rowcount
	select @rowCount as [RowCount]
end
go

drop procedure if exists dbo.tryResetRunQueue
go


SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO


create procedure dbo.tryResetRunQueue @runQueueId uniqueidentifier
as
begin
	declare @rowCount int
	set nocount on;

	update dbo.RunQueue
	set
		runQueueStatusId = 0,
		errorMessage = null,
		workerNodeId = null,
		startedOn = null,
		modifiedOn = getdate()
	where runQueueId = @runQueueId and runQueueStatusId = 4

	set @rowCount = @@rowcount
	select @rowCount as [RowCount]
end
go

drop procedure if exists dbo.tryStartRunQueue
go


SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

create procedure dbo.tryStartRunQueue (@runQueueId uniqueidentifier, @processId int)
as
begin
	declare @rowCount int
	set nocount on;

	update dbo.RunQueue
	set
		processId = @processId,
		runQueueStatusId = dbo.RunQueueStatus_InProgress(),
		startedOn = (getdate()),
		modifiedOn = (getdate())
	where runQueueId = @runQueueId and runQueueStatusId in (dbo.RunQueueStatus_NotStarted(), dbo.RunQueueStatus_InProgress())


	set @rowCount = @@rowcount
	select @rowCount as [RowCount]
end
go

drop procedure if exists dbo.tryUndeploySolver
go


SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO


create procedure dbo.tryUndeploySolver (@solverId uniqueidentifier)
as
begin
	declare @rowCount int
	set nocount on;

	update dbo.WorkerNodeSolver
	set isDeployed = 0
	where solverId = @solverId

	set @rowCount = @@rowcount
	select @rowCount as [RowCount]
end
go

drop procedure if exists deleteExpiredMessages
go


SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO


create procedure deleteExpiredMessages (@dataVersion int, @createdOn datetime)
as
begin
	declare @rowCount int
	set nocount on;

    delete from dbo.Message
    where
        deliveryTypeId = 1
        and dataVersion = @dataVersion
        and createdOn < @createdOn

	set @rowCount = @@rowcount
	select @rowCount as [RowCount]
end
go

drop procedure if exists deleteMessage
go


SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO


create procedure deleteMessage @messageId uniqueidentifier
as
begin
	declare @rowCount int
	set nocount on;

	delete from dbo.Message where messageId = @messageId

	set @rowCount = @@rowcount
	select @rowCount as [RowCount]
end
go

drop procedure if exists saveMessage
go


SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO


--create procedure saveMessage (
--					@messageId uniqueidentifier,
--					@senderId uniqueidentifier,
--					@recipientId uniqueidentifier,
--					@dataVersion int,
--					@deliveryTypeId int,
--					@messageData varbinary(max))
--as
--begin
--	declare @rowCount int
--	set nocount on;

--	insert into Message (messageId, senderId, recipientId, dataVersion, deliveryTypeId, messageData, createdOn)
--	select @messageId, @senderId, @recipientId, @dataVersion, @deliveryTypeId, @messageData, getdate()
--	where not exists (select 1 from Message where messageId = @messageId)

--	set @rowCount = @@rowcount
--	select @rowCount as [RowCount]
--end
--go

--CREATE PROCEDURE saveMessage (
--    @messageId uniqueidentifier,
--    @senderId uniqueidentifier,
--    @recipientId uniqueidentifier,
--    @dataVersion int,
--    @deliveryTypeId int,
--    @messageData varbinary(max)
--)
--AS
--BEGIN
--    DECLARE @rowCount int
--    SET NOCOUNT ON;

--    BEGIN TRY
--        -- Attempt to insert the row if it doesn’t already exist
--        INSERT INTO Message (messageId, senderId, recipientId, dataVersion, deliveryTypeId, messageData, createdOn)
--        SELECT @messageId, @senderId, @recipientId, @dataVersion, @deliveryTypeId, @messageData, GETDATE()
--        WHERE NOT EXISTS (SELECT 1 FROM Message WHERE messageId = @messageId)

--        -- Capture the row count after insertion
--        SET @rowCount = @@ROWCOUNT
--        SELECT @rowCount AS [RowCount]
--    END TRY
--    BEGIN CATCH
--        -- Capture and return error details if any
--        DECLARE @ErrorMessage NVARCHAR(4000) = ERROR_MESSAGE();
--        DECLARE @ErrorSeverity INT = ERROR_SEVERITY();
--        DECLARE @ErrorState INT = ERROR_STATE();
--        RAISERROR(@ErrorMessage, @ErrorSeverity, @ErrorState);
--        RETURN -1  -- Optional: indicate error with a negative row count
--    END CATCH
--END
--GO

CREATE PROCEDURE saveMessage (
    @messageId uniqueidentifier,
    @senderId uniqueidentifier,
    @recipientId uniqueidentifier,
    @dataVersion int,
    @deliveryTypeId int,
    @messageData varbinary(max)
)
AS
BEGIN
    DECLARE @rowCount int
    SET NOCOUNT ON;

    -- Check if the message already exists
    IF NOT EXISTS (SELECT 1 FROM Message WHERE messageId = @messageId)
    BEGIN
        -- If not, insert it and set row count to 1
        INSERT INTO Message (messageId, senderId, recipientId, dataVersion, deliveryTypeId, messageData, createdOn)
        VALUES (@messageId, @senderId, @recipientId, @dataVersion, @deliveryTypeId, @messageData, GETDATE())

        SET @rowCount = 1  -- Indicate a successful insert
    END
    ELSE
    BEGIN
        -- If already exists, set row count to 0 without inserting
        SET @rowCount = 0
    END

    -- Return the row count to indicate whether an insertion occurred
    SELECT @rowCount AS [RowCount]
END
GO

;with 
	valTbl as
	(
		select * 
		from 
		( values
			  (0, 'NoChartGeneration')
			, (1, 'RegularChartGeneration')
			, (2, 'ForceChartGeneration')
		) as a (notificationTypeId, notificationTypeName)
	)
insert into NotificationType
select valTbl.*
from valTbl
left outer join NotificationType on valTbl.notificationTypeId = NotificationType.notificationTypeId
where NotificationType.notificationTypeId is null
go


;with 
	valTbl as
	(
		select * 
		from 
		( values
			  (0, 'NotStarted')
			, (1, 'Inactive')
			, (7, 'RunRequested')
			, (2, 'InProgress')
			, (3, 'Completed')
			, (4, 'Failed')
			, (5, 'CancelRequested')
			, (6, 'Cancelled')

		) as a (runQueueStatusId, runQueueStatusName)
	)
insert into RunQueueStatus
select valTbl.*
from valTbl
left outer join RunQueueStatus on valTbl.runQueueStatusId = RunQueueStatus.runQueueStatusId
where RunQueueStatus.runQueueStatusId is null
go


;with 
	valTbl as
	(
		select * 
		from 
		( values
			  ('Suspended', 0, NULL, NULL, NULL, NULL, getdate())

		) as a (settingName, settingBool, settingGuid, settingLong, settingText, settingBinary, createdOn)
	)
insert into Setting
select valTbl.*
from valTbl
left outer join Setting on valTbl.settingName = Setting.settingName
where Setting.settingName is null
go


;with 
	valTbl as
	(
		select * 
		from 
		( values
			  (0, 'GuaranteedDelivery')
			, (1, 'NonGuaranteedDelivery')

		) as a (deliveryTypeId, deliveryTypeName)
	)
insert into DeliveryType
select valTbl.*
from valTbl
left outer join DeliveryType on valTbl.deliveryTypeId = DeliveryType.deliveryTypeId
where DeliveryType.deliveryTypeId is null
go


