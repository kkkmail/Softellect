using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Softellect.Migrations.WorkerNodeService.Migrations;

[Index("settingName", Name = "UX_Setting", IsUnique = true)]
public partial class Setting
{
    [Key]
    [StringLength(100)]
    public string settingName { get; set; } = null!;

    public bool? settingBool { get; set; }

    public Guid? settingGuid { get; set; }

    public long? settingLong { get; set; }

    public string? settingText { get; set; }

    public byte[]? settingBinary { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime createdOn { get; set; }
}
