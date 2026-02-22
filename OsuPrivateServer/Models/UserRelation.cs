using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace OsuPrivateServer.Models;

public class UserRelation
{
    [Key]
    public int Id { get; set; }

    [JsonPropertyName("user_id")]
    public int UserId { get; set; }

    [JsonPropertyName("target_id")]
    public int TargetId { get; set; }

    [JsonPropertyName("status")]
    public int Status { get; set; } // 1 = Friend, 2 = Blocked
}
