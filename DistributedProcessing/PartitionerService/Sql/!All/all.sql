IF OBJECT_ID('dbo.ModelType') IS NULL begin
	print 'Creating table dbo.ModelType ...'

	CREATE TABLE dbo.ModelType(
		modelTypeId int NOT NULL,
		modelTypeName nvarchar(50) NOT NULL,
	 CONSTRAINT PK_ModelType PRIMARY KEY CLUSTERED 
	(
		modelTypeId ASC
	) WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
	) ON [PRIMARY]

	CREATE UNIQUE NONCLUSTERED INDEX UX_ModelType ON dbo.ModelType
	(
		modelTypeName ASC
	) WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
end else begin
	print 'Table dbo.ModelType already exists ...'
end
go


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

		-- A human readable model type id to give a hint of what's running.
		-- This is needed because the modelData is stored in a zipped binary format.
		modelTypeId int NOT NULL, 

		-- All the initial data that is needed to run the calculation.
		-- It is designed to be huge, and so zipped binary format is used.
		modelData varbinary(max) NOT NULL,

		runQueueStatusId int NOT NULL,
		processId int NULL,
		notificationTypeId int NOT NULL,
		errorMessage nvarchar(max) NULL,
		progress decimal(18, 14) NOT NULL,

		-- Additional progress data used for further analysis and / or for earlier termination.
		-- We want to store the progress data in JSON rather than zipped binary, so that to be able to write some queries when needed.
		progressData nvarchar(max) NOT NULL,

		callCount bigint NOT NULL,

		 -- Should be close to 1.0 all the time. Substantial deviations is a sign of errors. If not needed, then set to 1.0.
		relativeInvariant float NOT NULL,

		createdOn datetime NOT NULL,
		modifiedOn datetime NOT NULL,
		startedOn datetime NULL,

		-- Partitioner has extra column to account for the worker node running the calculation.
		workerNodeId uniqueidentifier NULL,

	 CONSTRAINT PK_WorkerNodeRunModelData PRIMARY KEY CLUSTERED 
	(
		runQueueId ASC
	) WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
	) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]

	ALTER TABLE dbo.RunQueue ADD DEFAULT ((0)) FOR runQueueStatusId
	ALTER TABLE dbo.RunQueue ADD DEFAULT ((0)) FOR notificationTypeId
	ALTER TABLE dbo.RunQueue ADD DEFAULT ((0)) FOR progress
	ALTER TABLE dbo.RunQueue ADD DEFAULT ((0)) FOR callCount
	ALTER TABLE dbo.RunQueue ADD DEFAULT ((1)) FOR relativeInvariant
	ALTER TABLE dbo.RunQueue ADD DEFAULT (getdate()) FOR createdOn
	ALTER TABLE dbo.RunQueue ADD DEFAULT (getdate()) FOR modifiedOn

	ALTER TABLE dbo.RunQueue WITH CHECK ADD CONSTRAINT FK_RunQueue_NotificationType FOREIGN KEY(notificationTypeId)
	REFERENCES dbo.NotificationType (notificationTypeId)
	ALTER TABLE dbo.RunQueue CHECK CONSTRAINT FK_RunQueue_NotificationType

	ALTER TABLE dbo.RunQueue WITH CHECK ADD CONSTRAINT FK_RunQueue_RunQueueStatus FOREIGN KEY(runQueueStatusId)
	REFERENCES dbo.RunQueueStatus (runQueueStatusId)
	ALTER TABLE dbo.RunQueue CHECK CONSTRAINT FK_RunQueue_RunQueueStatus

	ALTER TABLE dbo.RunQueue  WITH CHECK ADD  CONSTRAINT FK_RunQueue_ModelType FOREIGN KEY(modelTypeId)
	REFERENCES dbo.ModelType (modelTypeId)
	ALTER TABLE dbo.RunQueue CHECK CONSTRAINT FK_RunQueue_ModelType

	ALTER TABLE dbo.RunQueue  WITH CHECK ADD  CONSTRAINT FK_RunQueue_WorkerNode FOREIGN KEY(workerNodeId)
	REFERENCES dbo.WorkerNode (workerNodeId)
	ALTER TABLE dbo.RunQueue CHECK CONSTRAINT FK_RunQueue_WorkerNode

end else begin
	print 'Table dbo.RunQueue already exists ...'
end
go

IF OBJECT_ID('dbo.Setting') IS NULL begin
	print 'Creating table dbo.Setting ...'

	CREATE TABLE dbo.Setting(
		settingId int NOT NULL,
		settingName nvarchar(50) NOT NULL,
		settingBool bit NULL,
		settingGuid uniqueidentifier NULL,
		settingLong bigint NULL,
		settingText nvarchar(1000) NULL,
	 CONSTRAINT PK_Setting PRIMARY KEY CLUSTERED 
	(
		settingId ASC
	) WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
	) ON [PRIMARY]

	CREATE UNIQUE NONCLUSTERED INDEX IX_Setting ON dbo.Setting
	(
		settingName ASC
	) WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
end else begin
	print 'Table dbo.Setting already exists ...'
end
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


create procedure dbo.tryCancelRunQueue (@runQueueId uniqueidentifier, @errorMessage nvarchar(max))
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


create procedure dbo.tryFailRunQueue (@runQueueId uniqueidentifier, @errorMessage nvarchar(max))
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
			  (0, 'Suspended', 0, NULL, NULL, NULL)

		) as a (settingId, settingName, settingBool, settingGuid, settingLong, settingText)
	)
insert into Setting
select valTbl.*
from valTbl
left outer join Setting on valTbl.settingId = Setting.settingId
where Setting.settingId is null
go


