using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace backend.Data.Entities;

/// <summary>
/// Entity for caching daily broadcast schedule
/// Stores list of Bangumi IDs for each date
/// </summary>
[Table("DailyScheduleCache")]
public class DailyScheduleCacheEntity
{
    [Key]
    public string Date { get; set; } = string.Empty;  // Format: yyyy-MM-dd

    public string BangumiIdsJson { get; set; } = "[]";  // JSON array of Bangumi IDs

    public DateTime CreatedAt { get; set; }
}
