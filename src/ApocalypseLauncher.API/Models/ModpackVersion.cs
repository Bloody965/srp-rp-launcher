using System;
using System.ComponentModel.DataAnnotations;

namespace ApocalypseLauncher.API.Models;

public class ModpackVersion
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(20)]
    public string Version { get; set; } = string.Empty;

    [Required]
    public string DownloadUrl { get; set; } = string.Empty;

    [Required]
    public string SHA256Hash { get; set; } = string.Empty;

    public long FileSizeBytes { get; set; }

    [MaxLength(1000)]
    public string? Changelog { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsActive { get; set; } = true;
}
