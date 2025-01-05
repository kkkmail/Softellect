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

