md .\!All
del .\!All\001_all.sql
del .\!All\002_data.sql

copy /b .\procedures\*.sql + .\data\*.sql .\!All\all_no_tables.sql
