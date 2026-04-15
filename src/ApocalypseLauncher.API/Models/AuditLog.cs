using System;
using System.ComponentModel.DataAnnotations;

namespace ApocalypseLauncher.API.Models;

public class AuditLog
{
    [Key]
    public int Id { get; set; }

    public int? UserId { get; set; }

    [Required]
    [MaxLength(50)]
    public string Action { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Details { get; set; }

    [Required]
    public string IpAddress { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation property
    public User? User { get; set; }
}
