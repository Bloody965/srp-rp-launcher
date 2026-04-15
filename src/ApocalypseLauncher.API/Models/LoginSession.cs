using System;
using System.ComponentModel.DataAnnotations;

namespace ApocalypseLauncher.API.Models;

public class LoginSession
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int UserId { get; set; }

    [Required]
    public string Token { get; set; } = string.Empty;

    [Required]
    public string IpAddress { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime ExpiresAt { get; set; }

    public bool IsRevoked { get; set; } = false;

    // Navigation property
    public User User { get; set; } = null!;
}
