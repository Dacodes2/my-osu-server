using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OsuPrivateServer.Models;

public class User
{
    [Key]
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    [JsonPropertyName("country_code")]
    public string CountryCode { get; set; } = "US";

    [JsonPropertyName("avatar_url")]
    public string AvatarUrl { get; set; } = "/avatars/default";

    [JsonPropertyName("cover_url")]
    public string CoverUrl { get; set; } = string.Empty;

    [JsonPropertyName("is_active")]
    public bool IsActive { get; set; } = true;

    [JsonPropertyName("is_supporter")]
    public bool IsSupporter { get; set; } = true;

    [JsonPropertyName("statistics")]
    public UserStatistics Statistics { get; set; } = new();
}

[Owned]
public class UserStatistics
{
    [JsonPropertyName("global_rank")]
    public int? GlobalRank { get; set; }

    [JsonPropertyName("country_rank")]
    public int? CountryRank { get; set; }

    [JsonPropertyName("pp")]
    [Column(TypeName = "decimal(18,2)")]
    public decimal? Pp { get; set; }

    [JsonPropertyName("level")]
    public LevelInfo Level { get; set; } = new();

    [JsonPropertyName("grade_counts")]
    public GradeCounts GradeCounts { get; set; } = new();
    
    [JsonPropertyName("play_count")]
    public int PlayCount { get; set; }
    
    [JsonPropertyName("ranked_score")]
    public long RankedScore { get; set; }
    
    [JsonPropertyName("total_score")]
    public long TotalScore { get; set; }
    
    [JsonPropertyName("hit_accuracy")]
    public double HitAccuracy { get; set; } = 100.0;
}

[Owned]
public class LevelInfo
{
    [JsonPropertyName("current")]
    public int Current { get; set; }

    [JsonPropertyName("progress")]
    public int Progress { get; set; }
}

[Owned]
public class GradeCounts
{
    [JsonPropertyName("ssh")]
    public int Ssh { get; set; }
    [JsonPropertyName("ss")]
    public int Ss { get; set; }
    [JsonPropertyName("sh")]
    public int Sh { get; set; }
    [JsonPropertyName("s")]
    public int S { get; set; }
    [JsonPropertyName("a")]
    public int A { get; set; }
}
