using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OsuPrivateServer.Data;
using OsuPrivateServer.Models;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

var port = Environment.GetEnvironmentVariable("PORT") ?? "5000";
builder.WebHost.UseUrls($"http://*:{port}");

// Determine public URL
string websiteUrl = Environment.GetEnvironmentVariable("WEBSITE_URL") 
                    ?? (builder.Environment.IsDevelopment() ? $"http://localhost:{port}" : "https://my-osu-server.onrender.com");


// Add services to the container.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configure Database
var connectionString = Environment.GetEnvironmentVariable("DATABASE_URL");
if (string.IsNullOrEmpty(connectionString))
{
    // Use SQLite for local persistence (creates a file named 'osu.db')
    // This is better than InMemory because it saves data to a file.
    // NOTE: On Render Free Tier, this file will still reset on restart (ephemeral filesystem),
    // but it works perfectly for local testing and persistent hosting elsewhere.
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseSqlite("Data Source=osu.db"));
}
else
{
    // Parse Render's DATABASE_URL (postgres://user:pass@host/db) to Npgsql format
    try 
    {
        var databaseUri = new Uri(connectionString);
        var userInfo = databaseUri.UserInfo.Split(':');
        var npgsqlBuilder = new Npgsql.NpgsqlConnectionStringBuilder
        {
            Host = databaseUri.Host,
            Port = databaseUri.Port,
            Username = userInfo[0],
            Password = userInfo[1],
            Database = databaseUri.LocalPath.TrimStart('/'),
            SslMode = Npgsql.SslMode.Require
        };
        builder.Services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(npgsqlBuilder.ToString()));
    }
    catch
    {
         // Fallback if parsing fails or simple string
         builder.Services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(connectionString));
    }
}

var app = builder.Build();

// Migrate/Create DB
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
    
    // Seed if empty
    if (!db.Users.Any())
    {
        db.Users.Add(new User
        {
            Id = 1001,
            Username = "Offline God",
            CountryCode = "US",
            CoverUrl = "https://assets.ppy.sh/user-profile-covers/1001/01460305f6e2b9c5d0c754630514088998188165.jpeg",
            Statistics = new UserStatistics
            {
                GlobalRank = 1,
                CountryRank = 1,
                Pp = 20000,
                Level = new LevelInfo { Current = 100, Progress = 50 },
                GradeCounts = new GradeCounts { Ss = 1000, S = 500, A = 100 }
            }
        });
        db.SaveChanges();
    }
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// app.UseHttpsRedirection(); // Disable HTTPS redirection for local debugging

app.Use(async (context, next) =>
{
    Console.WriteLine($"[{DateTime.Now}] Request: {context.Request.Method} {context.Request.Path}");
    if (context.Request.HasFormContentType)
    {
        foreach (var key in context.Request.Form.Keys)
        {
             Console.WriteLine($"  Form Key: {key} = {context.Request.Form[key]}");
        }
    }
    await next();
});

app.MapPost("/users", (HttpRequest request, AppDbContext db) =>
{
    if (!request.HasFormContentType)
    {
        return Results.BadRequest("Expected form content type");
    }

    var form = request.Form;
    // The client sends data as user[username], user[user_email], user[password]
    string user = form["user[username]"];
    string email = form["user[user_email]"];
    string password = form["user[password]"];

    if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            {
                 return Results.BadRequest(new { 
                     form_error = new { 
                         user = new { 
                             username = string.IsNullOrEmpty(user) ? new[] { "Username required!" } : null,
                             user_email = string.IsNullOrEmpty(email) ? new[] { "Email required!" } : null,
                             password = string.IsNullOrEmpty(password) ? new[] { "Password required!" } : null
                         } 
                     } 
                 });
            }

            // Check if user already exists
            if (db.Users.Any(u => u.Username.ToLower() == user.ToLower()))
            {
                return Results.BadRequest(new { 
                    form_error = new { 
                        user = new { 
                            username = new[] { "Username already taken!" } 
                        } 
                    } 
                });
            }

    int newId = (db.Users.Max(u => (int?)u.Id) ?? 1000) + 1;
    var newUser = new User
    {
        Id = newId,
        Username = user,
        CountryCode = "US",
        Statistics = new UserStatistics 
        { 
            GlobalRank = newId, 
            Pp = 0, 
            Level = new LevelInfo { Current = 1 } 
        }
    };
    db.Users.Add(newUser);
    db.SaveChanges();

    return Results.Ok(new { });
});

app.MapPost("/oauth/token", ([FromForm] string grant_type, [FromForm] string username, [FromForm] string password, [FromForm] string client_id, [FromForm] string client_secret, AppDbContext db) =>
{
    // Auto-register or login
    var user = db.Users.FirstOrDefault(u => u.Username.ToLower() == username.ToLower());
    
    if (user == null)
    {
        int newId = (db.Users.Max(u => (int?)u.Id) ?? 1000) + 1;
        user = new User
        {
            Id = newId,
            Username = username,
            CountryCode = "US",
            AvatarUrl = $"{websiteUrl}/avatars/{newId}",
            Statistics = new UserStatistics 
            { 
                GlobalRank = newId, 
                Pp = 0, 
                Level = new LevelInfo { Current = 1 } 
            }
        };
        db.Users.Add(user);
        db.SaveChanges();
    }
    else if (string.IsNullOrEmpty(user.AvatarUrl) || !user.AvatarUrl.StartsWith("http") || user.AvatarUrl.Contains("localhost"))
    {
        // Fix missing or incorrect avatar url for existing users
        user.AvatarUrl = $"{websiteUrl}/avatars/{user.Id}";
        db.SaveChanges();
    }

    return Results.Ok(new
    {
        access_token = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(user.Id.ToString())),
        expires_in = 86400,
        token_type = "Bearer",
        refresh_token = "dummy_refresh_token" // Added refresh token to prevent potential client issues
    });
}).DisableAntiforgery(); // Explicitly disable antiforgery for this endpoint since it's an API called by a desktop client

app.MapGet("/api/v2/me", (HttpRequest request, AppDbContext db) =>
{
    var token = request.Headers.Authorization.FirstOrDefault()?.Split(" ").Last();
    if (string.IsNullOrEmpty(token)) return Results.Unauthorized();

    try
    {
        var userIdString = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        if (int.TryParse(userIdString, out int userId))
        {
            var user = db.Users.FirstOrDefault(u => u.Id == userId);
            if (user != null) return Results.Ok(user);
        }
    }
    catch { }

    return Results.Unauthorized();
});

// Generic user profile
app.MapGet("/api/v2/users/{id}", (string id, AppDbContext db) =>
{
    User? user = null;
    if (int.TryParse(id, out int userId))
    {
        user = db.Users.FirstOrDefault(u => u.Id == userId);
    }
    else
    {
        user = db.Users.FirstOrDefault(u => u.Username.ToLower() == id.ToLower());
    }

    if (user == null) return Results.NotFound();
    return Results.Ok(user);
});

// Full profile with mode (osu)
app.MapGet("/api/v2/users/{id}/{mode}", (string id, string mode, AppDbContext db) =>
{
    User? user = null;
    if (int.TryParse(id, out int userId))
    {
        user = db.Users.FirstOrDefault(u => u.Id == userId);
    }
    else
    {
        user = db.Users.FirstOrDefault(u => u.Username.ToLower() == id.ToLower());
    }

    if (user == null) return Results.NotFound();
    return Results.Ok(user);
});

// Leaderboards
app.MapGet("/api/v2/rankings/{mode}/{type}", (string mode, string type, [FromQuery] int page, AppDbContext db) =>
{
    // Update ranks dynamically before returning
    var users = db.Users
        .Include(u => u.Statistics)
        .OrderByDescending(u => u.Statistics.Pp)
        .ToList();

    for (int i = 0; i < users.Count; i++)
    {
        users[i].Statistics.GlobalRank = i + 1;
        users[i].Statistics.CountryRank = i + 1; // Simplified
    }
    db.SaveChanges();

    var pagedUsers = users.Skip((page - 1) * 50).Take(50).ToList();

    // Map to client expected structure (UserStatistics)
    var mappedRanking = pagedUsers.Select(u => new
    {
        user = new
        {
            id = u.Id,
            username = u.Username,
            country_code = u.CountryCode,
            avatar_url = u.AvatarUrl,
            cover_url = u.CoverUrl,
            is_active = u.IsActive,
            is_supporter = u.IsSupporter
        },
        pp = u.Statistics.Pp,
        global_rank = u.Statistics.GlobalRank,
        country_rank = u.Statistics.CountryRank,
        ranked_score = u.Statistics.RankedScore,
        hit_accuracy = u.Statistics.HitAccuracy,
        play_count = u.Statistics.PlayCount,
        total_score = u.Statistics.TotalScore,
        level = u.Statistics.Level,
        grade_counts = u.Statistics.GradeCounts
    }).ToList();

    // Use 'cursor' for pagination
    // osu! expects 'cursor' to be an object with 'page' if next page exists, or null if end.
    object? cursor = null;
    if (users.Count > page * 50)
    {
        cursor = new { page = page + 1 };
    }

    return Results.Ok(new 
    { 
        ranking = mappedRanking,
        total = users.Count,
        cursor = cursor
    });
});

app.MapGet("/api/v2/beatmapsets/search", async (HttpRequest request) =>
{
    using var client = new HttpClient();
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/90.0.4430.212 Safari/537.36");
    
    var query = request.QueryString.Value; // e.g. ?q=...
    
    try 
    {
        // Use nerinyan.moe for search as well - it's often more reliable for mimics
        // Ensure we pass the query parameters correctly
        var searchUrl = $"https://api.nerinyan.moe/search{query}";
        
        var response = await client.GetAsync(searchUrl);
        
        if (!response.IsSuccessStatusCode) {
             // Fallback to empty result to prevent client crash
             return Results.Ok(new { beatmapsets = new List<object>(), total = 0 });
        }

        var content = await response.Content.ReadAsStringAsync();
        
        // Parse the JSON array
        try 
        {
            var node = JsonNode.Parse(content);
            if (node is JsonArray array)
            {
                foreach (var item in array)
                {
                    if (item is JsonObject obj)
                    {
                        var id = obj["id"]?.GetValue<int>() ?? 0;
                        if (id > 0)
                        {
                            // Add covers if missing
                            if (!obj.ContainsKey("covers"))
                            {
                                var covers = new JsonObject
                                {
                                    ["cover"] = $"https://assets.ppy.sh/beatmaps/{id}/covers/cover.jpg",
                                    ["cover@2x"] = $"https://assets.ppy.sh/beatmaps/{id}/covers/cover@2x.jpg",
                                    ["card"] = $"https://assets.ppy.sh/beatmaps/{id}/covers/card.jpg",
                                    ["card@2x"] = $"https://assets.ppy.sh/beatmaps/{id}/covers/card@2x.jpg",
                                    ["list"] = $"https://assets.ppy.sh/beatmaps/{id}/covers/list.jpg",
                                    ["list@2x"] = $"https://assets.ppy.sh/beatmaps/{id}/covers/list@2x.jpg",
                                    ["slimcover"] = $"https://assets.ppy.sh/beatmaps/{id}/covers/slimcover.jpg",
                                    ["slimcover@2x"] = $"https://assets.ppy.sh/beatmaps/{id}/covers/slimcover@2x.jpg"
                                };
                                obj["covers"] = covers;
                            }
                            
                            // Add preview_url if missing
                            if (!obj.ContainsKey("preview_url"))
                            {
                                 // Use nerinyan for previews as it's reliable
                                 obj["preview_url"] = $"https://b.nerinyan.moe/preview/{id}.mp3";
                            }

                            // Ensure other critical fields are present
                            if (!obj.ContainsKey("status")) obj["status"] = "ranked";
                        }
                    }
                }
                
                // Wrap in object compatible with osu! API v2
                var result = new JsonObject
                {
                    ["beatmapsets"] = array,
                    ["total"] = array.Count,
                    ["cursor_string"] = null,
                    ["search"] = new JsonObject 
                    {
                        ["sort"] = "relevance"
                    },
                    ["recommended_difficulty"] = null,
                    ["error"] = null
                };
                
                return Results.Content(result.ToJsonString(), "application/json");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error parsing search results: {ex.Message}");
        }
        
        // Fallback for raw content (might still be an array, but better than nothing)
        if (content.TrimStart().StartsWith("["))
        {
            return Results.Content($"{{\"beatmapsets\": {content}, \"total\": 100}}", "application/json");
        }
        
        return Results.Content(content, "application/json");
    }
    catch
    {
        // Return empty result on error to prevent crash
        return Results.Ok(new { beatmapsets = new List<object>(), total = 0 });
    }
});

app.MapGet("/api/v2/beatmapsets/{id}", async (string id) =>
{
    using var client = new HttpClient();
    // Try nerinyan for details as it matches osu! API well
    try
    {
        var response = await client.GetAsync($"https://api.nerinyan.moe/api/v2/beatmapsets/{id}");
        var content = await response.Content.ReadAsStringAsync();
        return Results.Content(content, "application/json");
    }
    catch
    {
        return Results.NotFound();
    }
});

app.MapGet("/api/v2/beatmapsets/{id}/download", (string id) =>
{
    return Results.Redirect($"https://api.nerinyan.moe/d/{id}");
});

app.MapPost("/api/v2/beatmaps/{beatmapId}/solo/scores", async (int beatmapId, HttpRequest request, AppDbContext db) =>
{
    var token = request.Headers.Authorization.FirstOrDefault()?.Split(" ").Last();
    if (string.IsNullOrEmpty(token)) return Results.Unauthorized();

    int userId = 0;
    try
    {
        var userIdString = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        if (!int.TryParse(userIdString, out userId)) return Results.Unauthorized();
    }
    catch { return Results.Unauthorized(); }

    var user = db.Users.FirstOrDefault(u => u.Id == userId);
    if (user == null) return Results.Unauthorized();

    // Create a new pending score
    var score = new Score
    {
        UserId = userId,
        BeatmapId = beatmapId,
        CreatedAt = DateTimeOffset.UtcNow,
        Passed = false // Pending
    };

    // Try to read ruleset_id from body
    try 
    {
        request.EnableBuffering();
        using var reader = new StreamReader(request.Body, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        request.Body.Position = 0;
        
        var json = JsonNode.Parse(body);
        if (json != null)
        {
            score.RulesetId = json["ruleset_id"]?.GetValue<int>() ?? 0;
        }
    }
    catch {}

    db.Scores.Add(score);
    db.SaveChanges();

    return Results.Ok(new { id = score.Id });
});

app.MapPut("/api/v2/beatmaps/{beatmapId}/solo/scores/{scoreId}", async (int beatmapId, long scoreId, HttpRequest request, AppDbContext db) =>
{
    var token = request.Headers.Authorization.FirstOrDefault()?.Split(" ").Last();
    if (string.IsNullOrEmpty(token)) return Results.Unauthorized();

    // Verify user
    int userId = 0;
    try
    {
        var userIdString = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        if (!int.TryParse(userIdString, out userId)) return Results.Unauthorized();
    }
    catch { return Results.Unauthorized(); }

    var score = db.Scores.FirstOrDefault(s => s.Id == scoreId);
    if (score == null) return Results.NotFound();
    if (score.UserId != userId) return Results.Forbid();

    try 
    {
        using var reader = new StreamReader(request.Body);
        var body = await reader.ReadToEndAsync();
        var json = JsonNode.Parse(body);
        
        if (json != null)
        {
            score.Passed = true;
            score.TotalScore = json["total_score"]?.GetValue<long>() ?? 0;
            score.Accuracy = json["accuracy"]?.GetValue<double>() ?? 0;
            score.MaxCombo = json["max_combo"]?.GetValue<int>() ?? 0;
            score.Rank = json["rank"]?.GetValue<string>() ?? "F";
            score.RulesetId = json["ruleset_id"]?.GetValue<int>() ?? score.RulesetId;
            
            // Capture statistics
            var stats = json["statistics"];
            if (stats != null)
            {
                score.Statistics.Count300 = stats["count_300"]?.GetValue<int>() ?? 0;
                score.Statistics.Count100 = stats["count_100"]?.GetValue<int>() ?? 0;
                score.Statistics.Count50 = stats["count_50"]?.GetValue<int>() ?? 0;
                score.Statistics.CountMiss = stats["count_miss"]?.GetValue<int>() ?? 0;
                score.Statistics.CountGeki = stats["count_geki"]?.GetValue<int>() ?? 0;
                score.Statistics.CountKatu = stats["count_katu"]?.GetValue<int>() ?? 0;
            }

            // Calculate PP (Simple approximation)
            // Real PP calculation is complex, so we'll make a fun estimate
            // Base PP on accuracy and combo scaling
            double acc = score.Accuracy; // 0.0 to 1.0 usually, or 0-100? osu! sends 0.98 for 98%
            if (acc > 1.0) acc /= 100.0; // Normalize just in case
            
            double ppBase = 50.0;
            if (score.Rank == "SS" || score.Rank == "X" || score.Rank == "XH" || score.Rank == "SSH") ppBase = 300;
            else if (score.Rank == "S" || score.Rank == "SH") ppBase = 200;
            else if (score.Rank == "A") ppBase = 100;
            else if (score.Rank == "B") ppBase = 50;
            else ppBase = 10;

            // Combo multiplier (logarithmic to avoid explosion)
            double comboMult = Math.Log10(score.MaxCombo + 10) * 10;
            
            score.Pp = (ppBase * acc) + comboMult;

            // Update User Stats
            var user = db.Users.Include(u => u.Statistics).FirstOrDefault(u => u.Id == userId);
            if (user != null)
            {
                user.Statistics.PlayCount++;
                user.Statistics.TotalScore += score.TotalScore;
                user.Statistics.RankedScore += score.TotalScore;
                user.Statistics.Pp += (decimal)(score.Pp ?? 0);
                
                // Level Up Logic: Simple curve
                // Level = Sqrt(TotalScore) / 500
                int newLevel = (int)(Math.Sqrt((double)user.Statistics.TotalScore) / 500) + 1;
                if (newLevel > user.Statistics.Level.Current)
                {
                    user.Statistics.Level.Current = newLevel;
                    user.Statistics.Level.Progress = 0;
                }
                else 
                {
                    // Calculate progress to next level
                    long scoreForCurrentLevel = (long)Math.Pow((user.Statistics.Level.Current - 1) * 500, 2);
                    long scoreForNextLevel = (long)Math.Pow(user.Statistics.Level.Current * 500, 2);
                    long scoreDiff = scoreForNextLevel - scoreForCurrentLevel;
                    long scoreProgress = user.Statistics.TotalScore - scoreForCurrentLevel;
                    
                    if (scoreDiff > 0)
                        user.Statistics.Level.Progress = (int)((double)scoreProgress / scoreDiff * 100);
                }
            }
            
            db.SaveChanges();
            
            return Results.Ok(new { 
                id = score.Id,
                total_score = score.TotalScore,
                accuracy = score.Accuracy,
                pp = score.Pp,
                rank = score.Rank
            }); 
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error saving score: {ex.Message}");
    }

    // Fallback if parsing fails (shouldn't happen with valid client)
    score.Passed = true;
    score.TotalScore = 1000000; 
    score.Rank = "S";
    score.Pp = 100;
    
    // Update User Stats (Fallback)
    var userFallback = db.Users.Include(u => u.Statistics).FirstOrDefault(u => u.Id == userId);
    if (userFallback != null)
    {
        userFallback.Statistics.PlayCount++;
        userFallback.Statistics.TotalScore += score.TotalScore;
        userFallback.Statistics.RankedScore += score.TotalScore;
        userFallback.Statistics.Pp += (decimal)(score.Pp ?? 0);
    }

    db.SaveChanges();

    return Results.Ok(new { 
        id = score.Id,
        total_score = score.TotalScore,
        accuracy = score.Accuracy,
        pp = score.Pp,
        rank = score.Rank
    }); 
});

// Beatmap Leaderboard
app.MapGet("/api/v2/beatmaps/{beatmapId}/scores", (int beatmapId, AppDbContext db) =>
{
    var scores = db.Scores
        .Where(s => s.BeatmapId == beatmapId && s.Passed)
        .OrderByDescending(s => s.TotalScore)
        .Take(50)
        .ToList();

    var resultScores = new List<object>();

    foreach (var score in scores)
    {
        var user = db.Users.FirstOrDefault(u => u.Id == score.UserId);
        if (user != null)
        {
            resultScores.Add(new 
            {
                id = score.Id,
                user_id = score.UserId,
                beatmap_id = score.BeatmapId,
                total_score = score.TotalScore,
                accuracy = score.Accuracy,
                max_combo = score.MaxCombo,
                rank = score.Rank,
                pp = score.Pp,
                passed = score.Passed,
                created_at = score.CreatedAt,
                statistics = new 
                {
                    count_300 = score.Statistics.Count300,
                    count_100 = score.Statistics.Count100,
                    count_50 = score.Statistics.Count50,
                    count_miss = score.Statistics.CountMiss,
                    count_geki = score.Statistics.CountGeki,
                    count_katu = score.Statistics.CountKatu
                },
                mode_int = 0, // Default to osu! standard for now
                user = new 
                {
                    id = user.Id,
                    username = user.Username,
                    country_code = user.CountryCode,
                    avatar_url = user.AvatarUrl,
                    cover_url = user.CoverUrl
                }
            });
        }
    }

    return Results.Ok(new { scores = resultScores });
});

// User Profile Scores (Best, Recent, Firsts)
app.MapGet("/api/v2/users/{id}/scores/{type}", async (string id, string type, [FromQuery] int limit, [FromQuery] int offset, AppDbContext db) =>
{
    int userId = 0;
    if (!int.TryParse(id, out userId))
    {
        var userObj = db.Users.FirstOrDefault(u => u.Username.ToLower() == id.ToLower());
        if (userObj != null) userId = userObj.Id;
    }

    if (userId == 0) return Results.NotFound();

    var query = db.Scores.Where(s => s.UserId == userId && s.Passed);

    if (type == "best")
    {
        query = query.OrderByDescending(s => s.Pp).ThenByDescending(s => s.TotalScore);
    }
    else if (type == "recent")
    {
        query = query.OrderByDescending(s => s.CreatedAt);
    }
    // "firsts" would need global rank logic, skipping for now

    var scores = query.Skip(offset).Take(limit > 0 ? limit : 10).ToList();
    var resultScores = new List<object>();

    using var client = new HttpClient();

    foreach (var score in scores)
    {
        var user = db.Users.FirstOrDefault(u => u.Id == score.UserId);
        
        // Fetch beatmap info from Nerinyan to populate the card
        object beatmapInfo = null;
        object beatmapsetInfo = null;
        
        try 
        {
            // We need beatmap details to show the song on profile
            // This is a bit slow doing it one by one, but fine for a small private server
            var response = await client.GetAsync($"https://api.nerinyan.moe/api/v2/beatmaps/{score.BeatmapId}");
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var node = JsonNode.Parse(content);
                if (node != null)
                {
                    beatmapInfo = node;
                    beatmapsetInfo = node["beatmapset"];
                }
            }
        }
        catch {}

        // Fallback if API fails
        if (beatmapInfo == null)
        {
             beatmapInfo = new { id = score.BeatmapId, status = "ranked", version = "Unknown" };
             beatmapsetInfo = new { id = 0, title = "Unknown Title", artist = "Unknown Artist", status = "ranked", covers = new { cover = "https://osu.ppy.sh/images/headers/profile-covers/c1.jpg" } };
        }

        resultScores.Add(new 
        {
            id = score.Id,
            user_id = score.UserId,
            beatmap_id = score.BeatmapId,
            total_score = score.TotalScore,
            accuracy = score.Accuracy,
            max_combo = score.MaxCombo,
            rank = score.Rank,
            pp = score.Pp,
            passed = score.Passed,
            created_at = score.CreatedAt,
            statistics = new 
            {
                count_300 = score.Statistics.Count300,
                count_100 = score.Statistics.Count100,
                count_50 = score.Statistics.Count50,
                count_miss = score.Statistics.CountMiss,
                count_geki = score.Statistics.CountGeki,
                count_katu = score.Statistics.CountKatu
            },
            mode_int = 0,
            user = new 
            {
                id = user.Id,
                username = user.Username,
                country_code = user.CountryCode,
                avatar_url = user.AvatarUrl,
                cover_url = user.CoverUrl
            },
            beatmap = beatmapInfo,
            beatmapset = beatmapsetInfo
        });
    }

    return Results.Ok(resultScores);
});

app.MapPost("/api/v2/users/{id}/avatar", async (int id, HttpRequest request, AppDbContext db) =>
{
    // Check auth
    var token = request.Headers.Authorization.FirstOrDefault()?.Split(" ").Last();
    if (string.IsNullOrEmpty(token)) return Results.Unauthorized();

    try
    {
        var userIdString = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        if (!int.TryParse(userIdString, out int userId) || userId != id) return Results.Forbid();
    }
    catch { return Results.Unauthorized(); }

    if (!request.HasFormContentType) return Results.BadRequest("Expected form content type");

    var form = await request.ReadFormAsync();
    var file = form.Files.GetFile("avatar");

    if (file == null || file.Length == 0) return Results.BadRequest("No file uploaded");

    // Save file
    var avatarsDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "avatars");
    Directory.CreateDirectory(avatarsDir);
    
    var filePath = Path.Combine(avatarsDir, $"{id}.jpg"); // Force jpg for simplicity or detect extension
    
    using (var stream = new FileStream(filePath, FileMode.Create))
    {
        await file.CopyToAsync(stream);
    }

    // Update user avatar URL
    var user = db.Users.FirstOrDefault(u => u.Id == id);
    if (user != null)
    {
        // Use a timestamp query param to bust client cache
        user.AvatarUrl = $"{websiteUrl}/avatars/{id}.jpg?t={DateTime.UtcNow.Ticks}";
        db.SaveChanges();
    }

    return Results.Ok(new { avatar_url = user?.AvatarUrl });
});

// Serve static files (avatars)
app.UseStaticFiles();

// Generic user update
app.MapPut("/api/v2/users/{id}", (int id, HttpRequest request, AppDbContext db) =>
{
    // Check auth
    var token = request.Headers.Authorization.FirstOrDefault()?.Split(" ").Last();
    if (string.IsNullOrEmpty(token)) return Results.Unauthorized();

    try
    {
        var userIdString = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        if (!int.TryParse(userIdString, out int userId) || userId != id) return Results.Forbid();
    }
    catch { return Results.Unauthorized(); }

    // Read JSON body - simplified
    // In real app we should use [FromBody] UserUpdateRequest model
    // Here we'll just check for form data or json.
    // For simplicity, let's assume we might receive form data or we can parse JSON manually if needed.
    // But standard ASP.NET Core minimal API with [FromBody] is better if we have a model.
    // Let's stick to a simple form approach or query params for now if client sends them, 
    // OR just use the edit_profile logic but exposed as JSON API.

    // Let's assume the client sends JSON.
    // We need to read the body.
    
    // Quick hack: The user wants to change country.
    // Let's try to read it from a custom header or query param for easiest integration
    // OR let's just use the existing form logic if we can.
    
    // Better: Read the request body as a JsonDocument
    /*
    using var reader = new StreamReader(request.Body);
    var body = await reader.ReadToEndAsync();
    var json = JsonDocument.Parse(body);
    */
    
    // For now, let's return OK to avoid errors if client calls this.
    return Results.Ok();
});

// Specific endpoint for our custom UI to call
app.MapPost("/api/custom/update_profile", async (HttpRequest request, AppDbContext db) =>
{
    var token = request.Headers.Authorization.FirstOrDefault()?.Split(" ").Last();
    if (string.IsNullOrEmpty(token)) return Results.Unauthorized();

    int userId = 0;
    try
    {
        var userIdString = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        if (!int.TryParse(userIdString, out userId)) return Results.Unauthorized();
    }
    catch { return Results.Unauthorized(); }

    var form = await request.ReadFormAsync();
    var countryCode = form["country_code"].ToString();
    
    var user = db.Users.FirstOrDefault(u => u.Id == userId);
    if (user != null)
    {
        if (!string.IsNullOrEmpty(countryCode))
            user.CountryCode = countryCode.ToUpper();
            
        db.SaveChanges();
    }
    
    return Results.Ok(user);
});

app.MapGet("/api/v2/friends", () => Results.Ok(new List<object>()));
app.MapGet("/api/v2/chat/channels", () => Results.Ok(new List<object>()));
app.MapGet("/api/v2/notifications", () => Results.Ok(new { notifications = new List<object>() }));

app.MapGet("/avatars/{id}", (string id, AppDbContext db) => {
    if (int.TryParse(id, out int userId)) {
        var user = db.Users.FirstOrDefault(u => u.Id == userId);
        if (user != null) {
            return Results.Redirect($"https://ui-avatars.com/api/?background=random&name={user.Username}&size=512");
        }
    }
    return Results.NotFound();
});

app.MapGet("/edit_profile", (HttpRequest request, AppDbContext db) => {
    var token = request.Query["token"].ToString();
    var idStr = request.Query["id"].ToString();
    
    // Validate token if provided
    User? user = null;
    if (!string.IsNullOrEmpty(token)) {
        try {
            var userIdString = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
            if (int.TryParse(userIdString, out int userId)) {
                user = db.Users.FirstOrDefault(u => u.Id == userId);
            }
        } catch {}
    }

    if (user == null && !string.IsNullOrEmpty(idStr) && int.TryParse(idStr, out int reqId)) {
         // Fallback for debugging if token is invalid but ID is provided (INSECURE - for local testing only)
         // In production, MUST require token.
         user = db.Users.FirstOrDefault(u => u.Id == reqId);
    }
    
    string authSection = "";
    if (user != null) {
        authSection = $@"
        <div class='user-info'>
            <img src='{user.AvatarUrl}' class='avatar-small'>
            <div>
                <h2>Hello, {user.Username}!</h2>
                <p>ID: {user.Id} (Unique Account ID)</p>
            </div>
        </div>
        <input type='hidden' id='auth_token' value='{token}'>
        <input type='hidden' id='auth_id' value='{user.Id}'>
        ";
    } else {
        authSection = @"
        <div class='alert'>
            <p><strong>Not Logged In via Game Client</strong></p>
            <p>Please open this page by clicking 'Edit Profile' in the game, or enter your details manually below.</p>
        </div>";
    }

    return Results.Content($@"
<!DOCTYPE html>
<html>
<head>
    <title>Edit Profile</title>
    <style>
        body {{ font-family: 'Segoe UI', sans-serif; max-width: 600px; margin: 2rem auto; padding: 0 1rem; background: #222; color: #fff; }}
        .container {{ background: #333; padding: 2rem; border-radius: 8px; box-shadow: 0 4px 6px rgba(0,0,0,0.3); }}
        h1 {{ margin-top: 0; color: #ff66aa; }}
        .form-group {{ margin-bottom: 1.5rem; }}
        label {{ display: block; margin-bottom: 0.5rem; font-weight: bold; }}
        input[type='text'], input[type='number'] {{ width: 100%; padding: 0.8rem; border: 1px solid #444; border-radius: 4px; background: #444; color: white; box-sizing: border-box; }}
        input[type='file'] {{ width: 100%; padding: 0.5rem; background: #444; color: white; border-radius: 4px; }}
        button {{ padding: 0.8rem 1.5rem; background: #ff66aa; color: white; border: none; border-radius: 4px; cursor: pointer; font-size: 1rem; font-weight: bold; transition: background 0.2s; }}
        button:hover {{ background: #ff4da6; }}
        .avatar-preview {{ width: 100px; height: 100px; border-radius: 20%; object-fit: cover; margin-bottom: 1rem; border: 2px solid #ff66aa; }}
        .note {{ font-size: 0.8rem; color: #aaa; margin-top: 0.5rem; }}
        .user-info {{ display: flex; align-items: center; gap: 1rem; margin-bottom: 2rem; background: #444; padding: 1rem; border-radius: 8px; }}
        .avatar-small {{ width: 60px; height: 60px; border-radius: 50%; object-fit: cover; }}
        .alert {{ background: #442222; color: #ffaaaa; padding: 1rem; border-radius: 4px; margin-bottom: 1rem; }}
    </style>
    <script>
        function updateForms() {{
            var token = document.getElementById('auth_token')?.value;
            var id = document.getElementById('auth_id')?.value;
            
            if (token && id) {{
                // Update Profile Form
                var form = document.getElementById('profile_form');
                form.action = '/edit_profile?token=' + token;
                
                // Update Avatar Form
                var avForm = document.getElementById('avatar_form');
                avForm.action = '/upload_avatar?token=' + token;
                
                // Hide manual inputs if we have auth
                document.getElementById('manual_user_input').style.display = 'none';
                document.getElementById('manual_id_input').style.display = 'none';
                
                // Inject ID into hidden fields if needed (though we use token/query param now)
                var hiddenId = document.createElement('input');
                hiddenId.type = 'hidden';
                hiddenId.name = 'user_id';
                hiddenId.value = id;
                avForm.appendChild(hiddenId);
            }}
        }}
        window.onload = updateForms;
    </script>
</head>
<body>
    <div class='container'>
        <h1>Edit Profile</h1>
        
        {authSection}
        
        <!-- Profile Update Form -->
        <form id='profile_form' method='POST' action='/edit_profile' class='form-group'>
            <div class='form-group' id='manual_user_input'>
                <label>Username</label>
                <input name='username' placeholder='Enter your username to identify' value='{(user?.Username ?? "")}'>
                <p class='note'>Must match your in-game username.</p>
            </div>
            <div class='form-group'>
                <label>Country Code</label>
                <input name='country_code' maxlength='2' placeholder='US, DE, JP, etc.' value='{(user?.CountryCode ?? "")}'>
            </div>
            <button type='submit'>Update Info</button>
        </form>

        <hr style='border-color: #444; margin: 2rem 0;'>

        <!-- Avatar Upload Form -->
        <form id='avatar_form' method='POST' action='/upload_avatar' enctype='multipart/form-data'>
            <div class='form-group' id='manual_id_input'>
                <label>Change Avatar</label>
                <input type='text' name='user_id' placeholder='Your User ID (e.g. 1001)' value='{(user?.Id.ToString() ?? "")}'>
                <p class='note'>You can find your ID on your profile page.</p>
            </div>
            <div class='form-group'>
                <label>Select Image</label>
                <input type='file' name='avatar' accept='image/*' required>
            </div>
            <button type='submit'>Upload Avatar</button>
        </form>
    </div>
</body>
</html>
", "text/html");
});

app.MapPost("/upload_avatar", async (HttpRequest request, AppDbContext db) => {
    if (!request.HasFormContentType) return Results.BadRequest("Expected form content type");

    var token = request.Query["token"].ToString();
    var form = await request.ReadFormAsync();
    var file = form.Files.GetFile("avatar");
    var userIdStr = form["user_id"];
    
    int userId = 0;
    
    // Auth via Token
    if (!string.IsNullOrEmpty(token)) {
        try {
            var uidStr = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
            int.TryParse(uidStr, out userId);
        } catch {}
    }
    
    // Fallback to form ID
    if (userId == 0 && !string.IsNullOrEmpty(userIdStr)) {
        int.TryParse(userIdStr, out userId);
    }

    if (file == null || file.Length == 0) return Results.BadRequest("No file uploaded");
    if (userId == 0) return Results.BadRequest("Invalid User ID or Session");

    // Save file
    var avatarsDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "avatars");
    Directory.CreateDirectory(avatarsDir);
    
    var filePath = Path.Combine(avatarsDir, $"{userId}.jpg");
    
    using (var stream = new FileStream(filePath, FileMode.Create))
    {
        await file.CopyToAsync(stream);
    }

    // Update user avatar URL
    var user = db.Users.FirstOrDefault(u => u.Id == userId);
    if (user != null)
    {
        user.AvatarUrl = $"http://localhost:5000/avatars/{userId}.jpg?t={DateTime.UtcNow.Ticks}";
        db.SaveChanges();
    }

    return Results.Content($"<h1>Avatar Updated!</h1><p>Restart the game or change screens to see changes.</p><a href='/edit_profile?token={token}&id={userId}' style='color: #ff66aa'>Back</a>", "text/html");
}).DisableAntiforgery();

app.MapPost("/edit_profile", ([FromForm] string username, [FromForm] string country_code, [FromQuery] string token, AppDbContext db) => {
    User? user = null;
    
    // Auth via Token
    if (!string.IsNullOrEmpty(token)) {
        try {
            var uidStr = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
            if (int.TryParse(uidStr, out int uid)) {
                user = db.Users.FirstOrDefault(u => u.Id == uid);
            }
        } catch {}
    }
    
    // Fallback to username
    if (user == null && !string.IsNullOrEmpty(username)) {
        user = db.Users.FirstOrDefault(u => u.Username.ToLower() == username.ToLower());
    }

    if (user == null) return Results.BadRequest("User not found");
    
    if (!string.IsNullOrEmpty(country_code)) {
        user.CountryCode = country_code.ToUpper();
    }
    
    // Also update username if provided and authenticated
    if (!string.IsNullOrEmpty(username) && user.Username != username) {
         // Check collision
         if (!db.Users.Any(u => u.Username.ToLower() == username.ToLower() && u.Id != user.Id)) {
             user.Username = username;
         }
    }
    
    db.SaveChanges();
    return Results.Content($"<h1>Profile Updated!</h1><p>Restart the game to see changes.</p><a href='/edit_profile?token={token}&id={user.Id}' style='color: #ff66aa'>Back</a>", "text/html");
}).DisableAntiforgery();

app.MapGet("/users/{id}", (string id, AppDbContext db) => {
    if (int.TryParse(id, out int userId)) {
         var user = db.Users.FirstOrDefault(u => u.Id == userId);
         if (user != null) {
             return Results.Content($@"
             <!DOCTYPE html>
             <html>
             <body>
                 <h1>{user.Username}</h1>
                 <img src='{user.AvatarUrl}' width='100' />
                 <p>Country: {user.CountryCode}</p>
                 <a href='/edit_profile'>Edit Profile</a>
             </body>
             </html>
             ", "text/html");
         }
    }
    return Results.NotFound();
});

app.Run();
