using System;
using System.ComponentModel.DataAnnotations;

namespace ApocalypseLauncher.API.Models;

public class User
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(50)]
    public string Username { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [MaxLength(100)]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string PasswordHash { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    public DateTime? LastLoginAt { get; set; }

    public bool IsActive { get; set; } = true;

    public bool IsBanned { get; set; } = false;

    public string? BanReason { get; set; }

    // Для whitelist системы
    public bool IsWhitelisted { get; set; } = false;

    // UUID для Minecraft
    [Required]
    [MaxLength(36)]
    public string MinecraftUUID { get; set; } = string.Empty;
}
