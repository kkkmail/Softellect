# Service Reinstallation Error Codes
# These error codes are used to communicate the status of service reinstallation operations

# Success
$global:ERROR_SUCCESS = 0

# Administrative and Permission Errors (1-10)
$global:ERROR_ADMIN_RIGHTS_REQUIRED = 1

# Prerequisites and Validation Errors (11-30)
$global:ERROR_PREREQUISITES_FAILED = 11
$global:ERROR_SERVICE_FOLDER_INVALID = 12
$global:ERROR_INSTALLATION_FOLDER_INVALID = 13

# Backup and Restore Errors (31-50)
$global:ERROR_BACKUP_FAILED = 31
$global:ERROR_RESTORE_FAILED = 32

# Service Management Errors (51-70)
$global:ERROR_UNINSTALL_FAILED = 51
$global:ERROR_INSTALL_FAILED = 52

# Database Migration Errors (71-90)
$global:ERROR_MIGRATION_EXTRACT_FAILED = 71
$global:ERROR_MIGRATION_UP_FAILED = 72
$global:ERROR_MIGRATION_DOWN_FAILED = 73

# File Operation Errors (91-110)
$global:ERROR_CLEAR_FOLDER_FAILED = 91
$global:ERROR_FOLDER_NOT_EMPTY = 92
$global:ERROR_COPY_INSTALLATION_FAILED = 93
$global:ERROR_COPY_FILES_TO_KEEP_FAILED = 94

# Unexpected Errors (111-130)
$global:ERROR_UNEXPECTED_EXCEPTION = 111
$global:ERROR_ROLLBACK_FAILED = 112

# Error code descriptions for logging purposes
$global:ERROR_DESCRIPTIONS = @{
    0   = "Success"
    1   = "Administrator rights required"
    11  = "Prerequisites check failed"
    12  = "Service folder path is invalid"
    13  = "Installation folder path is invalid"
    31  = "Failed to create backup"
    32  = "Failed to restore from backup"
    51  = "Failed to uninstall service"
    52  = "Failed to install service"
    71  = "Failed to extract migration state"
    72  = "Failed to perform database migration (UP)"
    73  = "Failed to perform database migration (DOWN)"
    91  = "Failed to clear service folder"
    92  = "Service folder is not empty after cleanup"
    93  = "Failed to copy installation files"
    94  = "Failed to copy files to keep"
    111 = "Unexpected exception occurred"
    112 = "Rollback operation failed"
}
