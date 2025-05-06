using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Softellect.Migrations.Common;

[Table("Setting")]
[Index(nameof(SettingName), IsUnique = true)]
public class Setting
{
    [Key]
    [Column("settingName")]
    [StringLength(100)]
    public string SettingName { get; set; } = null!;

    [Column("settingBool")]
    public bool? SettingBool { get; set; }

    [Column("settingGuid")]
    public Guid? SettingGuid { get; set; }

    [Column("settingLong")]
    public long? SettingLong { get; set; }

    [Column("settingText")]
    [MaxLength]
    public string? SettingText { get; set; }

    [Column("settingBinary")]
    public byte[]? SettingBinary { get; set; }

    [Column("createdOn")]
    public DateTime CreatedOn { get; set; }
}

[Table("NotificationType")]
[Index(nameof(NotificationTypeName), IsUnique = true)]
public class NotificationType
{
    [Key]
    [Column("notificationTypeId")]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int NotificationTypeId { get; set; }

    [Required]
    [Column("notificationTypeName")]
    [StringLength(50)]
    public string NotificationTypeName { get; set; } = null!;

    // public ICollection<RunQueue> RunQueues { get; set; } = new List<RunQueue>();
}

[Table("RunQueueStatus")]
[Index(nameof(RunQueueStatusName), IsUnique = true)]
public class RunQueueStatus
{
    [Key]
    [Column("runQueueStatusId")]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int RunQueueStatusId { get; set; }

    [Required]
    [Column("runQueueStatusName")]
    [StringLength(50)]
    public string RunQueueStatusName { get; set; } = null!;

    // public ICollection<RunQueue> RunQueues { get; set; } = new List<RunQueue>();
}
