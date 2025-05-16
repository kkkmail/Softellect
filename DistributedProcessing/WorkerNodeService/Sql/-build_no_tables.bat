md .\!All
del .\!All\all_no_tables.sql

copy /b .\functions\*.sql + .\procedures\*.sql + ..\..\..\MessagingService\Sql\procedures\*.sql + .\data\*.sql + ..\..\..\MessagingService\Sql\data\*.sql .\!All\all_no_tables.sql
