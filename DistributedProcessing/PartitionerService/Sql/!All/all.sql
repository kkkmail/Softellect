IF OBJECT_ID('dbo.NotificationType') IS NULL begin
    print 'Creating table dbo.NotificationType ...'

    CREATE TABLE dbo.NotificationType(
        notificationTypeId int NOT NULL,
        notificationTypeName nvarchar(50) NOT NULL,
    CONSTRAINT PK_NotificationType PRIMARY KEY CLUSTERED 
    (
        notificationTypeId ASC
    ) WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
    ) ON [PRIMARY]

    CREATE UNIQUE NONCLUSTERED INDEX UX_NotificationType ON dbo.NotificationType
    (
        notificationTypeName ASC
    ) WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
end else begin
    print 'Table dbo.NotificationType already exists ...'
end
go


IF OBJECT_ID('dbo.RunQueueStatus') IS NULL begin
    print 'Creating table dbo.RunQueueStatus ...'

    CREATE TABLE dbo.RunQueueStatus(
        runQueueStatusId int NOT NULL,
        runQueueStatusName nvarchar(50) NOT NULL,
    CONSTRAINT PK_RunQueueStatus PRIMARY KEY CLUSTERED 
    (
        runQueueStatusId ASC
    ) WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
    ) ON [PRIMARY]

    CREATE UNIQUE NONCLUSTERED INDEX UX_RunQueueStatus ON dbo.RunQueueStatus
    (
        runQueueStatusName ASC
    ) WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
end else begin
    print 'Table dbo.RunQueueStatus already exists ...'
end
go


IF OBJECT_ID('dbo.Solver') IS NULL begin
    print 'Creating table dbo.Solver ...'

    CREATE TABLE dbo.Solver(
        solverId uniqueidentifier not null,
        solverOrder bigint identity(1,1) not null,
        solverName nvarchar(100) not null,
        description nvarchar(2000) null, 
        solverData varbinary(max) null,
        createdOn datetime not null,
    CONSTRAINT PK_Solver PRIMARY KEY CLUSTERED 
    (
        solverId ASC
    ) WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
    ) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]

    ALTER TABLE dbo.Solver ADD CONSTRAINT DF_Solver_createdOn DEFAULT (getdate()) FOR createdOn

    CREATE UNIQUE NONCLUSTERED INDEX UX_Solver_solverName ON dbo.Solver
    (
        solverName ASC
    ) WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]

end else begin
    print 'Table dbo.Solver already exists ...'
end
go

IF OBJECT_ID('dbo.WorkerNode') IS NULL begin
    print 'Creating table dbo.WorkerNode ...'

    CREATE TABLE dbo.WorkerNode(
        workerNodeId uniqueidentifier NOT NULL,
        workerNodeOrder bigint IDENTITY(1,1) NOT NULL,
        workerNodeName nvarchar(100) NOT NULL,
        nodePriority int NOT NULL,
        numberOfCores int NOT NULL,
        description nvarchar(1000) NULL,
        isInactive bit NOT NULL,
        workerNodePublicKey varbinary(max) NULL,
        createdOn datetime NOT NULL,
        modifiedOn datetime NOT NULL,
        lastErrorOn datetime NULL,
    CONSTRAINT PK_WorkerNode PRIMARY KEY CLUSTERED 
    (
        workerNodeId ASC
    ) WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
    ) ON [PRIMARY]

    ALTER TABLE dbo.WorkerNode ADD  CONSTRAINT DF_WorkerNode_isInactive  DEFAULT ((0)) FOR isInactive
    ALTER TABLE dbo.WorkerNode ADD  CONSTRAINT DF_WorkerNode_nodePriority  DEFAULT ((100)) FOR nodePriority
    ALTER TABLE dbo.WorkerNode ADD  CONSTRAINT DF_WorkerNode_numberOfCores  DEFAULT ((0)) FOR numberOfCores
    ALTER TABLE dbo.WorkerNode ADD  CONSTRAINT DF_WorkerNode_createdOn  DEFAULT (getdate()) FOR createdOn
    ALTER TABLE dbo.WorkerNode ADD  CONSTRAINT DF_WorkerNode_modifiedOn  DEFAULT (getdate()) FOR modifiedOn

    CREATE UNIQUE NONCLUSTERED INDEX UX_WorkerNodeName ON dbo.WorkerNode
    (
        workerNodeName ASC
    ) WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]

    CREATE UNIQUE NONCLUSTERED INDEX UX_WorkerNodeOrder ON dbo.WorkerNode
    (
        workerNodeOrder ASC
    ) WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
end else begin
    print 'Table dbo.WorkerNode already exists ...'
end
go

IF OBJECT_ID('dbo.RunQueue') IS NULL begin
    print 'Creating table dbo.RunQueue ...'

    CREATE TABLE dbo.RunQueue(
        runQueueId uniqueidentifier NOT NULL,
        runQueueOrder bigint IDENTITY(1,1) NOT NULL,

        -- A solver id to determine which solver should run the model.
        -- This is needed because the modelData is stored in a zipped binary format.
        solverId uniqueidentifier not null,

        runQueueStatusId int NOT NULL,
        processId int NULL,
        notificationTypeId int NOT NULL,
        errorMessage nvarchar(max) NULL,
        progress decimal(38, 16) NOT NULL,

        -- Additional progress data (if any) used for further analysis and / or for earlier termination.
        -- We want to store the progress data in JSON rather than zipped binary, so that to be able to write some queries when needed.
        progressData nvarchar(max) NULL,

        callCount bigint NOT NULL,
        evolutionTime decimal(38, 16) not null,

	        -- Should be close to 1.0 all the time. Substantial deviations is a sign of errors. If not needed, then set to 1.0.
        relativeInvariant float NOT NULL,

        createdOn datetime NOT NULL,
        startedOn datetime NULL,
        modifiedOn datetime NOT NULL,

        -- Partitioner has extra column to account for the worker node running the calculation.
        workerNodeId uniqueidentifier NULL,

    CONSTRAINT PK_RunQueue PRIMARY KEY CLUSTERED 
    (
        runQueueId ASC
    ) WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
    ) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]

    ALTER TABLE dbo.RunQueue ADD CONSTRAINT DF_RunQueue_runQueueStatusId DEFAULT ((0)) FOR runQueueStatusId
    ALTER TABLE dbo.RunQueue ADD CONSTRAINT DF_RunQueue_notificationTypeId DEFAULT ((0)) FOR notificationTypeId
    ALTER TABLE dbo.RunQueue ADD CONSTRAINT DF_RunQueue_progress DEFAULT ((0)) FOR progress
    ALTER TABLE dbo.RunQueue ADD CONSTRAINT DF_RunQueue_callCount DEFAULT ((0)) FOR callCount
    ALTER TABLE dbo.RunQueue ADD CONSTRAINT DF_RunQueue_evolutionTime DEFAULT ((0)) FOR evolutionTime
    ALTER TABLE dbo.RunQueue ADD CONSTRAINT DF_RunQueue_relativeInvariant DEFAULT ((1)) FOR relativeInvariant
    ALTER TABLE dbo.RunQueue ADD CONSTRAINT DF_RunQueue_createdOn DEFAULT (getdate()) FOR createdOn
    ALTER TABLE dbo.RunQueue ADD CONSTRAINT DF_RunQueue_modifiedOn DEFAULT (getdate()) FOR modifiedOn

    ALTER TABLE dbo.RunQueue WITH CHECK ADD CONSTRAINT FK_RunQueue_NotificationType FOREIGN KEY(notificationTypeId)
    REFERENCES dbo.NotificationType (notificationTypeId)
    ALTER TABLE dbo.RunQueue CHECK CONSTRAINT FK_RunQueue_NotificationType

    ALTER TABLE dbo.RunQueue WITH CHECK ADD CONSTRAINT FK_RunQueue_RunQueueStatus FOREIGN KEY(runQueueStatusId)
    REFERENCES dbo.RunQueueStatus (runQueueStatusId)
    ALTER TABLE dbo.RunQueue CHECK CONSTRAINT FK_RunQueue_RunQueueStatus

    ALTER TABLE dbo.RunQueue  WITH CHECK ADD  CONSTRAINT FK_RunQueue_Solver FOREIGN KEY(solverId)
    REFERENCES dbo.Solver (solverId)
    ALTER TABLE dbo.RunQueue CHECK CONSTRAINT FK_RunQueue_Solver

    ALTER TABLE dbo.RunQueue  WITH CHECK ADD  CONSTRAINT FK_RunQueue_WorkerNode FOREIGN KEY(workerNodeId)
    REFERENCES dbo.WorkerNode (workerNodeId)
    ALTER TABLE dbo.RunQueue CHECK CONSTRAINT FK_RunQueue_WorkerNode

end else begin
    print 'Table dbo.RunQueue already exists ...'
end
go

IF OBJECT_ID('dbo.ModelData') IS NULL begin
    print 'Creating table dbo.ModelData ...'

    CREATE TABLE dbo.ModelData(
        runQueueId uniqueidentifier NOT NULL,

        -- All the initial data that is needed to run the calculation.
        -- It is designed to be huge, and so zipped binary format is used.
        modelData varbinary(max) NOT NULL,

    CONSTRAINT PK_ModelData PRIMARY KEY CLUSTERED 
    (
	    runQueueId ASC
    ) WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
    ) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]

    ALTER TABLE dbo.ModelData WITH CHECK ADD CONSTRAINT FK_ModelData_RunQueue FOREIGN KEY(runQueueId)
    REFERENCES dbo.RunQueue (runQueueId)
    ALTER TABLE dbo.ModelData CHECK CONSTRAINT FK_ModelData_RunQueue

end else begin
	print 'Table dbo.ModelData already exists ...'
end
go

IF OBJECT_ID('dbo.Setting') IS NULL begin
    print 'Creating table dbo.Setting ...'

    CREATE TABLE dbo.Setting(
        settingName nvarchar(100) NOT NULL,
        settingBool bit NULL,
        settingGuid uniqueidentifier NULL,
        settingLong bigint NULL,
        settingText nvarchar(max) NULL,
        settingBinary varbinary(max) NULL,
        createdOn datetime not null,
    CONSTRAINT PK_Setting PRIMARY KEY CLUSTERED 
    (
        settingName ASC
    ) WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
    ) ON [PRIMARY]

    ALTER TABLE dbo.Setting ADD CONSTRAINT DF_Setting_createdOn DEFAULT (getdate()) FOR createdOn

    CREATE UNIQUE NONCLUSTERED INDEX UX_Setting ON dbo.Setting
    (
        settingName ASC
    ) WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
end else begin
	print 'Table dbo.Setting already exists ...'
end
go



IF OBJECT_ID('dbo.WorkerNode_Solver') IS NULL begin
    print 'Creating table dbo.WorkerNode_Solver ...'

    CREATE TABLE dbo.WorkerNode_Solver(
        workerNodeId uniqueidentifier not null,
        solverId uniqueidentifier not null,
        createdOn datetime not null,
        lastErrorOn datetime null,
        isDeployed bit not null,
        deploymentError nvarchar(max) null,
        CONSTRAINT PK_WorkerNode_Solver PRIMARY KEY CLUSTERED 
    (
        workerNodeId ASC,
        solverId ASC
    ) WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
    ) ON [PRIMARY]

    ALTER TABLE dbo.WorkerNode_Solver ADD CONSTRAINT DF_WorkerNode_Solver_createdOn DEFAULT (getdate()) FOR createdOn
    ALTER TABLE dbo.WorkerNode_Solver ADD CONSTRAINT DF_WorkerNode_Solver_isDeployed DEFAULT (0) FOR isDeployed

    ALTER TABLE dbo.WorkerNode_Solver  WITH CHECK ADD  CONSTRAINT FK_WorkerNode_Solver_WorkerNode FOREIGN KEY(workerNodeId)
    REFERENCES dbo.WorkerNode (workerNodeId)
    ALTER TABLE dbo.WorkerNode_Solver CHECK CONSTRAINT FK_WorkerNode_Solver_WorkerNode

    ALTER TABLE dbo.WorkerNode_Solver  WITH CHECK ADD  CONSTRAINT FK_WorkerNode_Solver_Solver FOREIGN KEY(solverId)
    REFERENCES dbo.Solver (solverId)
    ALTER TABLE dbo.WorkerNode_Solver CHECK CONSTRAINT FK_WorkerNode_Solver_Solver

end else begin
    print 'Table dbo.WorkerNode already exists ...'
end
go

drop view if exists vw_newid
go


create view vw_newid
as
select newid() as new_id
go

drop function if exists dbo.getAvailableWorkerNode
go


create function dbo.getAvailableWorkerNode(@lastAllowedNodeErrInMinutes int)
returns table
as
return
(
	with a as
	(
	select
		workerNodeId
		,nodePriority
		,cast(
			case
				when numberOfCores <= 0 then 1
				else (select count(1) as runningModels from RunQueue where workerNodeId = w.workerNodeId and runQueueStatusId in (2, 5, 7)) / (cast(numberOfCores as money))
			end as money) as workLoad
		,case when lastErrorOn is null or dateadd(minute, @lastAllowedNodeErrInMinutes, lastErrorOn) < getdate() then 0 else 1 end as noErr
	from WorkerNode w
	where isInactive = 0
	),
	b as
	(
		select
			a.*, 
			c.new_id
			from a
			cross apply (select new_id from vw_newid) c
	)
	select top 1
	workerNodeId
	from b
	where noErr = 0 and workLoad < 1
	order by nodePriority desc, workLoad, new_id
)
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
with a as
(
select
	workerNodeId
	,nodePriority
	,isnull(cast(
		case
			when numberOfCores <= 0 then 1
			else (select count(1) as runningModels from RunQueue where workerNodeId = w.workerNodeId and runQueueStatusId in (2, 5, 7)) / (cast(numberOfCores as money))
		end as money), 0) as workLoad
	,case when lastErrorOn is null then null else datediff(minute, getdate(), lastErrorOn) end as lastErrMinAgo
from WorkerNode w
where isInactive = 0
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

IF OBJECT_ID('dbo.DeliveryType') IS NULL begin
    print 'Creating table dbo.DeliveryType ...'

    CREATE TABLE dbo.DeliveryType(
        deliveryTypeId int NOT NULL,
        deliveryTypeName nvarchar(50) NOT NULL,
    CONSTRAINT PK_DeliveryType PRIMARY KEY CLUSTERED 
    (
        deliveryTypeId ASC
    ) WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
    ) ON [PRIMARY]
end else begin
    print 'Table dbo.DeliveryType already exists ...'
end
go

IF OBJECT_ID('dbo.Message') IS NULL begin
    print 'Creating table dbo.Message ...'

    CREATE TABLE dbo.Message(
        messageId uniqueidentifier NOT NULL,
        senderId uniqueidentifier NOT NULL,
        recipientId uniqueidentifier NOT NULL,
        messageOrder bigint IDENTITY(1,1) NOT NULL,
        dataVersion int NOT NULL,
        deliveryTypeId int NOT NULL,
        messageData varbinary(max) NOT NULL,
        createdOn datetime NOT NULL,
    CONSTRAINT PK_Message PRIMARY KEY CLUSTERED 
    (
        messageId ASC
    ) WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
    ) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]

    ALTER TABLE dbo.Message  WITH CHECK ADD  CONSTRAINT FK_Message_DeliveryType FOREIGN KEY(deliveryTypeId)
    REFERENCES dbo.DeliveryType (deliveryTypeId)

    ALTER TABLE dbo.Message CHECK CONSTRAINT FK_Message_DeliveryType
end else begin
    print 'Table dbo.Message already exists ...'
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


