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


