using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace OsuPrivateServer.Models;

public class Score
{
    [Key]
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("user_id")]
    public int UserId { get; set; }

    [JsonPropertyName("beatmap_id")]
    public int BeatmapId { get; set; }

    [JsonPropertyName("ruleset_id")]
    public int RulesetId { get; set; }

    [JsonPropertyName("total_score")]
    public long TotalScore { get; set; }

    [JsonPropertyName("accuracy")]
    public double Accuracy { get; set; }

    [JsonPropertyName("max_combo")]
    public int MaxCombo { get; set; }

    [JsonPropertyName("rank")]
    public string Rank { get; set; } = "F";

    [JsonPropertyName("pp")]
    public double? Pp { get; set; }

    [JsonPropertyName("passed")]
    public bool Passed { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("statistics")]
    public ScoreStatistics Statistics { get; set; } = new();
}

[Owned]
public class ScoreStatistics
{
    [JsonPropertyName("count_300")]
    public int Count300 { get; set; }

    [JsonPropertyName("count_100")]
    public int Count100 { get; set; }

    [JsonPropertyName("count_50")]
    public int Count50 { get; set; }

    [JsonPropertyName("count_miss")]
    public int CountMiss { get; set; }
    
    [JsonPropertyName("count_geki")]
    public int CountGeki { get; set; }

    [JsonPropertyName("count_katu")]
    public int CountKatu { get; set; }
}
