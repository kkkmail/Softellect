drop procedure if exists dbo.tryUpdateProgressRunQueue
go


SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO


create procedure dbo.tryUpdateProgressRunQueue (
						@runQueueId uniqueidentifier,
						@progress decimal(18, 14),
                        @progressData nvarchar(max),
						@callCount bigint,
						@relativeInvariant float)
as
begin
	declare @rowCount int
	set nocount on;

    update dbo.RunQueue
    set
        progress = @progress,
        progressData = @progressData,
        callCount = @callCount,
        relativeInvariant = @relativeInvariant,
        modifiedOn = (getdate())
    where runQueueId = @runQueueId and runQueueStatusId in (dbo.RunQueueStatus_InProgress(), dbo.RunQueueStatus_CancelRequested())

	set @rowCount = @@rowcount
	select @rowCount as [RowCount]
end
go

