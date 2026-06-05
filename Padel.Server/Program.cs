using System.Globalization;
using System.IO;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Components.Endpoints;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Padel.Core.Editing;
using Padel.Core.Matchmaking;
using Padel.Core.Model;
using Padel.Core.Tournaments;
using Padel.Server;
using Padel.Server.Auth;
using Padel.Server.Components;
using Padel.Server.Contracts;
using Padel.Server.Data;

var builder = WebApplication.CreateBuilder(args);

// All persistent state (SQLite DB + signing key) lives next to the executable, so
// it is the same regardless of which folder the server is launched from.
var dataDir = AppContext.BaseDirectory;

// --- Configuration ---
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();

// If no signing key was configured, generate one and persist it next to the data
// file so the server "just works" on first run without managing secrets by hand.
if (string.IsNullOrWhiteSpace(jwt.Key) || jwt.Key.Length < 32)
{
    jwt.Key = EnsureSigningKey(dataDir);
}

// Ensure TokenService (resolved via IOptions) sees the effective key even when
// it was generated above rather than read from configuration.
builder.Services.PostConfigure<JwtOptions>(options => options.Key = jwt.Key);

// --- Services ---
var connectionString = builder.Configuration.GetConnectionString("Default");
if (string.IsNullOrWhiteSpace(connectionString))
{
    connectionString = $"Data Source={Path.Combine(dataDir, "padel.db")}";
}
builder.Services.AddDbContext<PadelDbContext>(options => options.UseSqlite(connectionString));
builder.Services.AddSingleton<TokenService>();
builder.Services.AddHttpContextAccessor();

// Two auth schemes coexist: cookies for the browser/web UI (default), JWT bearer
// for the desktop app's /api calls. API endpoints opt into JWT via the "ApiJwt" policy.
builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    })
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.LogoutPath = "/logout";
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;
        options.Cookie.Name = "padel_auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        // Secure when served over HTTPS, plain over HTTP — works on a LAN and behind a proxy.
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key))
        };
    });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("ApiJwt", policy => policy
        .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser());
});
builder.Services.AddRazorComponents();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Registration policy (gate sign-up before the server faces the internet).
builder.Services.Configure<RegistrationOptions>(builder.Configuration.GetSection(RegistrationOptions.SectionName));

// When running behind a reverse proxy / tunnel (Caddy, Cloudflare), honour the
// X-Forwarded-Proto/-For headers so the app knows requests are really HTTPS.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // The proxy runs on the same host / trusted network; don't restrict by known IP.
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();
var registration = app.Services.GetRequiredService<IOptions<RegistrationOptions>>().Value;

// --- Database init + seed the default account ---
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PadelDbContext>();
    db.Database.EnsureCreated();
    UpgradeSchema(db);
    SeedAccount(db, app.Configuration, app.Logger);
}

// Must run before auth/routing so the proxy's scheme/host are applied to the request.
app.UseForwardedHeaders();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// --- Web UI sign-in/out (cookie auth via plain form posts from the Razor pages) ---
app.MapPost("/account/login", async (HttpContext http, PadelDbContext db) =>
{
    var form = await http.Request.ReadFormAsync();
    var username = form["username"].ToString().Trim();
    var password = form["password"].ToString();

    var account = await db.Accounts.FirstOrDefaultAsync(a => a.Username == username);
    if (account is null || !PasswordHasher.Verify(password, account.PasswordHash))
    {
        return Results.Redirect("/login?error=1");
    }

    await SignInCookieAsync(http, account.Username, account.Role);
    return Results.Redirect("/");
});

app.MapPost("/account/register", async (HttpContext http, PadelDbContext db) =>
{
    var form = await http.Request.ReadFormAsync();
    var username = form["username"].ToString().Trim();
    var password = form["password"].ToString();

    if (!registration.Allows(form["invite"].ToString()))
    {
        return Results.Redirect(registration.IsClosed ? "/register?error=closed" : "/register?error=invite");
    }

    if (username.Length < 3 || string.IsNullOrWhiteSpace(password) || password.Length < 4)
    {
        return Results.Redirect("/register?error=invalid");
    }

    if (await db.Accounts.AnyAsync(a => a.Username == username))
    {
        return Results.Redirect("/register?error=taken");
    }

    var account = new AccountRecord { Username = username, PasswordHash = PasswordHasher.Hash(password), Role = "user" };
    db.Accounts.Add(account);
    await db.SaveChangesAsync();

    await SignInCookieAsync(http, account.Username, account.Role);
    return Results.Redirect("/");
});

app.MapPost("/account/logout", async (HttpContext http) =>
{
    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
});

// Record a match from the web UI (cookie-authenticated form post). Bumps the
// dataset Version so the desktop app's next sync sees the change (conflict-checked).
app.MapPost("/datasets/{id}/scores", async (string id, HttpContext http, PadelDbContext db) =>
{
    var user = http.User.Identity?.Name;
    if (string.IsNullOrEmpty(user))
    {
        return Results.Redirect("/login");
    }

    var record = await db.Datasets.FirstOrDefaultAsync(d => d.Id == id && d.Owner == user);
    if (record is null)
    {
        return Results.NotFound();
    }

    var form = await http.Request.ReadFormAsync();
    int Field(string key) => int.TryParse(form[key], out var v) ? v : 0;

    var ids = new[] { Field("a1"), Field("a2"), Field("b1"), Field("b2") };
    var scoreA = Field("scoreA");
    var scoreB = Field("scoreB");
    var date = DateTime.TryParse(form["date"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
        ? d
        : DateTime.Today;

    // Four distinct, real players required.
    if (ids.Any(x => x <= 0) || ids.Distinct().Count() != 4)
    {
        return Results.Redirect($"/datasets/{id}/add?error=players");
    }

    if (scoreA < 0 || scoreB < 0)
    {
        return Results.Redirect($"/datasets/{id}/add?error=score");
    }

    PadelDataFile data;
    try
    {
        data = JsonSerializer.Deserialize<PadelDataFile>(record.Json) ?? new PadelDataFile();
    }
    catch
    {
        return Results.Redirect($"/datasets/{id}/add?error=data");
    }

    var known = data.Players.Select(p => p.Id).ToHashSet();
    if (!ids.All(known.Contains))
    {
        return Results.Redirect($"/datasets/{id}/add?error=players");
    }

    PadelEditor.AddScore(data, date, ids[0], ids[1], ids[2], ids[3], scoreA, scoreB);

    record.Json = JsonSerializer.Serialize(data);
    record.Version += 1;
    record.UpdatedUtc = DateTime.UtcNow;
    await db.SaveChangesAsync();

    return Results.Redirect($"/datasets/{id}?added=1");
}).RequireAuthorization();

// Add a player. (Path differs from the /players page route to avoid an ambiguous match.)
app.MapPost("/datasets/{id}/players/add", async (string id, HttpContext http, PadelDbContext db) =>
{
    var form = await http.Request.ReadFormAsync();
    var name = form["name"].ToString();
    var level = int.TryParse(form["level"], out var l) ? Math.Clamp(l, 1, 10) : 3;
    return await ApplyAsync(id, http, db, data => PadelEditor.AddPlayer(data, name, level), $"/datasets/{id}/players");
}).RequireAuthorization();

// Update a player's level.
app.MapPost("/datasets/{id}/players/level", async (string id, HttpContext http, PadelDbContext db) =>
{
    var form = await http.Request.ReadFormAsync();
    var playerId = int.TryParse(form["playerId"], out var p) ? p : 0;
    var level = int.TryParse(form["level"], out var l) ? Math.Clamp(l, 1, 10) : 3;
    return await ApplyAsync(id, http, db, data => PadelEditor.SetPlayerLevel(data, playerId, level), $"/datasets/{id}/players");
}).RequireAuthorization();

// Edit a match's score.
app.MapPost("/datasets/{id}/match/{date}/{num:int}/save", async (string id, string date, int num, HttpContext http, PadelDbContext db) =>
{
    var form = await http.Request.ReadFormAsync();
    var matchDate = DateTime.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : DateTime.MinValue;
    var scoreA = int.TryParse(form["scoreA"], out var a) ? a : -1;
    var scoreB = int.TryParse(form["scoreB"], out var b) ? b : -1;
    if (scoreA < 0 || scoreB < 0)
    {
        return Results.Redirect($"/datasets/{id}/history?error=1");
    }
    return await ApplyAsync(id, http, db, data => PadelEditor.UpdateScore(data, matchDate, num, scoreA, scoreB), $"/datasets/{id}/history");
}).RequireAuthorization();

// Delete a match.
app.MapPost("/datasets/{id}/match/{date}/{num:int}/delete", async (string id, string date, int num, HttpContext http, PadelDbContext db) =>
{
    var matchDate = DateTime.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : DateTime.MinValue;
    return await ApplyAsync(id, http, db, data => PadelEditor.DeleteScore(data, matchDate, num) > 0, $"/datasets/{id}/history");
}).RequireAuthorization();

// Save generated matches (those with both scores filled in) as played matches.
app.MapPost("/datasets/{id}/generate/save", async (string id, HttpContext http, PadelDbContext db) =>
{
    var form = await http.Request.ReadFormAsync();
    var date = DateTime.TryParse(form["date"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : DateTime.Today;
    var (a1, a2, b1, b2) = (form["a1"], form["a2"], form["b1"], form["b2"]);
    var (sa, sb) = (form["scoreA"], form["scoreB"]);

    return await ApplyAsync(id, http, db, data =>
    {
        var known = data.Players.Select(p => p.Id).ToHashSet();
        var added = 0;
        for (var i = 0; i < a1.Count; i++)
        {
            if (!int.TryParse(a1[i], out var p1) || !int.TryParse(a2[i], out var p2)
                || !int.TryParse(b1[i], out var p3) || !int.TryParse(b2[i], out var p4))
            {
                continue;
            }

            var ids = new[] { p1, p2, p3, p4 };
            if (ids.Distinct().Count() != 4 || !ids.All(known.Contains))
            {
                continue;
            }

            // Only record matches whose scores were filled in.
            if (!int.TryParse(sa[i], out var x) || !int.TryParse(sb[i], out var y) || x < 0 || y < 0)
            {
                continue;
            }

            PadelEditor.AddScore(data, date, p1, p2, p3, p4, x, y);
            added++;
        }

        return added > 0;
    }, $"/datasets/{id}");
}).RequireAuthorization();

// Save generated matches as a planning (unplayed schedule) for the date.
app.MapPost("/datasets/{id}/generate/plan", async (string id, HttpContext http, PadelDbContext db) =>
{
    var form = await http.Request.ReadFormAsync();
    var date = DateTime.TryParse(form["date"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : DateTime.Today;
    var (a1, a2, b1, b2, rnd, ter) = (form["a1"], form["a2"], form["b1"], form["b2"], form["round"], form["terrain"]);

    return await ApplyAsync(id, http, db, data =>
    {
        var known = data.Players.Select(p => p.Id).ToHashSet();
        var matches = new List<(int, int, int, int, int, int)>();
        for (var i = 0; i < a1.Count; i++)
        {
            if (!int.TryParse(a1[i], out var p1) || !int.TryParse(a2[i], out var p2)
                || !int.TryParse(b1[i], out var p3) || !int.TryParse(b2[i], out var p4))
            {
                continue;
            }

            var ids = new[] { p1, p2, p3, p4 };
            if (ids.Distinct().Count() != 4 || !ids.All(known.Contains))
            {
                continue;
            }

            var round = i < rnd.Count && int.TryParse(rnd[i], out var rr) ? rr : 1;
            var terrain = i < ter.Count && int.TryParse(ter[i], out var tt) ? tt : i + 1;
            matches.Add((round, terrain, p1, p2, p3, p4));
        }

        if (matches.Count == 0)
        {
            return false;
        }

        PadelEditor.ReplacePlanned(data, date, matches);
        return true;
    }, $"/datasets/{id}/planning");
}).RequireAuthorization();

// Record scores for planned matches (moves played ones into the leaderboard/history).
app.MapPost("/manage/datasets/{id}/planning/{date}/scores", async (string id, string date, HttpContext http, PadelDbContext db) =>
{
    var form = await http.Request.ReadFormAsync();
    var d = DateTime.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? parsed.Date : DateTime.MinValue;
    var (rnd, ter, sa, sb) = (form["round"], form["terrain"], form["scoreA"], form["scoreB"]);

    return await ApplyAsync(id, http, db, data =>
    {
        var recorded = 0;
        for (var i = 0; i < rnd.Count; i++)
        {
            if (!int.TryParse(rnd[i], out var r) || !int.TryParse(ter[i], out var t))
            {
                continue;
            }

            // Only record matches whose scores were filled in.
            if (!int.TryParse(sa[i], out var x) || !int.TryParse(sb[i], out var y) || x < 0 || y < 0)
            {
                continue;
            }

            if (PadelEditor.RecordPlannedScore(data, d, r, t, x, y))
            {
                recorded++;
            }
        }

        return recorded > 0;
    }, $"/datasets/{id}/planning");
}).RequireAuthorization();

// --- Dataset management (create / rename / delete) under /manage to avoid page routes ---
app.MapPost("/manage/datasets/create", async (HttpContext http, PadelDbContext db) =>
{
    var user = http.User.Identity?.Name;
    if (string.IsNullOrEmpty(user))
    {
        return Results.Redirect("/login");
    }

    var form = await http.Request.ReadFormAsync();
    var name = form["name"].ToString().Trim();
    if (string.IsNullOrWhiteSpace(name))
    {
        return Results.Redirect("/?error=name");
    }

    var record = new DatasetRecord
    {
        Owner = user,
        Name = name,
        Json = JsonSerializer.Serialize(new PadelDataFile()),
        Version = 1,
        UpdatedUtc = DateTime.UtcNow
    };
    db.Datasets.Add(record);
    await db.SaveChangesAsync();
    return Results.Redirect($"/datasets/{record.Id}/players");
}).RequireAuthorization();

app.MapPost("/manage/datasets/{id}/rename", async (string id, HttpContext http, PadelDbContext db) =>
{
    var user = http.User.Identity?.Name;
    if (string.IsNullOrEmpty(user))
    {
        return Results.Redirect("/login");
    }

    var form = await http.Request.ReadFormAsync();
    var name = form["name"].ToString().Trim();
    var record = await db.Datasets.FirstOrDefaultAsync(d => d.Id == id && d.Owner == user);
    if (record is null)
    {
        return Results.NotFound();
    }

    if (!string.IsNullOrWhiteSpace(name))
    {
        record.Name = name;
        record.Version += 1;
        record.UpdatedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    return Results.Redirect($"/datasets/{id}");
}).RequireAuthorization();

app.MapPost("/manage/datasets/{id}/delete", async (string id, HttpContext http, PadelDbContext db) =>
{
    var user = http.User.Identity?.Name;
    if (string.IsNullOrEmpty(user))
    {
        return Results.Redirect("/login");
    }

    var record = await db.Datasets.FirstOrDefaultAsync(d => d.Id == id && d.Owner == user);
    if (record is not null)
    {
        db.Datasets.Remove(record);
        await db.SaveChangesAsync();
    }

    return Results.Redirect("/");
}).RequireAuthorization();

// --- Tournaments (under /manage to avoid page routes) ---
app.MapPost("/manage/datasets/{id}/tournament/create", async (string id, HttpContext http, PadelDbContext db) =>
{
    var form = await http.Request.ReadFormAsync();
    var date = DateTime.TryParse(form["date"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d.Date : DateTime.Today;
    var pids = form["p"].Where(s => int.TryParse(s, out _)).Select(s => int.Parse(s!)).ToList();

    var result = await ApplyAsync(id, http, db, data =>
    {
        var selected = data.Players.Where(p => pids.Contains(p.Id)).ToList();
        if (selected.Count < 4 || selected.Count % 4 != 0)
        {
            return false;
        }

        var avg = MatchGenerator.AveragePointsById(data);
        var round1 = MatchGenerator.Generate(selected, avg, 1, new Random());
        if (round1.Count == 0)
        {
            return false;
        }

        var names = data.Players.ToDictionary(p => p.Id, p => p.Name);
        var teams = new List<string>();
        foreach (var m in round1.OrderBy(x => x.Terrain))
        {
            teams.Add($"{names[m.A1]} & {names[m.A2]}");
            teams.Add($"{names[m.B1]} & {names[m.B2]}");
        }

        data.TournamentEntries.RemoveAll(t => t.TournamentDate.Date == date);
        data.TournamentEntries.AddRange(TournamentBuilder.Generate(teams, date));
        return true;
    }, $"/datasets/{id}/tournament/{date:yyyy-MM-dd}");

    // On a validation failure ApplyAsync redirects to the target with ?error=1;
    // send the user back to the tournaments page instead.
    return result;
}).RequireAuthorization();

app.MapPost("/manage/datasets/{id}/tournament/{date}/scores", async (string id, string date, HttpContext http, PadelDbContext db) =>
{
    var form = await http.Request.ReadFormAsync();
    var matchDate = DateTime.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d.Date : DateTime.MinValue;
    var (rounds, matches, sa, sb) = (form["round"], form["match"], form["scoreA"], form["scoreB"]);

    return await ApplyAsync(id, http, db, data =>
    {
        var entries = data.TournamentEntries.Where(t => t.TournamentDate.Date == matchDate).ToList();
        if (entries.Count == 0)
        {
            return false;
        }

        var byKey = entries
            .GroupBy(e => (e.RoundNumber, e.MatchNumber))
            .ToDictionary(g => g.Key, g => g.First());

        for (var i = 0; i < rounds.Count; i++)
        {
            if (!int.TryParse(rounds[i], out var rn) || !int.TryParse(matches[i], out var mn)
                || !byKey.TryGetValue((rn, mn), out var entry))
            {
                continue;
            }

            entry.ScoreA = int.TryParse(sa[i], out var x) && x >= 0 ? x : (int?)null;
            entry.ScoreB = int.TryParse(sb[i], out var y) && y >= 0 ? y : (int?)null;
        }

        TournamentBuilder.Propagate(entries);
        return true;
    }, $"/datasets/{id}/tournament/{date}");
}).RequireAuthorization();

// --- Settings ---
app.MapPost("/manage/datasets/{id}/settings", async (string id, HttpContext http, PadelDbContext db) =>
{
    var form = await http.Request.ReadFormAsync();
    var americano = form.ContainsKey("americano");
    var perSheet = int.TryParse(form["perSheet"], out var ps) ? Math.Clamp(ps, 5, 200) : 20;
    return await ApplyAsync(id, http, db, data =>
    {
        data.AmericanoMode = americano;
        data.LeaderboardPlayersPerSheet = perSheet;
        return true;
    }, $"/datasets/{id}/settings");
}).RequireAuthorization();

// --- XLSX export (file download) ---
app.MapGet("/datasets/{id}/export", async (string id, HttpContext http, PadelDbContext db) =>
{
    var user = http.User.Identity?.Name;
    if (string.IsNullOrEmpty(user))
    {
        return Results.Redirect("/login");
    }

    var record = await db.Datasets.FirstOrDefaultAsync(d => d.Id == id && d.Owner == user);
    if (record is null)
    {
        return Results.NotFound();
    }

    PadelDataFile data;
    try
    {
        data = JsonSerializer.Deserialize<PadelDataFile>(record.Json) ?? new PadelDataFile();
    }
    catch
    {
        return Results.NotFound();
    }

    var bytes = ExcelExport.Build(data);
    var safeName = string.Concat(record.Name.Split(Path.GetInvalidFileNameChars()));
    if (string.IsNullOrWhiteSpace(safeName))
    {
        safeName = "padel";
    }

    return Results.File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"{safeName}.xlsx");
}).RequireAuthorization();

// --- Auth ---
app.MapPost("/api/login", async (LoginRequest req, PadelDbContext db, TokenService tokens) =>
{
    var account = await db.Accounts.FirstOrDefaultAsync(a => a.Username == req.Username);
    if (account is null || !PasswordHasher.Verify(req.Password, account.PasswordHash))
    {
        return Results.Unauthorized();
    }

    return Results.Ok(new LoginResponse(tokens.CreateToken(account.Username, account.Role)));
});

// Open registration: anyone who can reach the server can create their own account.
// Each account only ever sees its own datasets (enforced on the dataset endpoints).
app.MapPost("/api/register", async (RegisterRequest req, PadelDbContext db, TokenService tokens) =>
{
    if (!registration.Allows(req.InviteCode))
    {
        return Results.BadRequest(new { message = registration.IsClosed
            ? "Les inscriptions sont fermées."
            : "Code d'invitation invalide." });
    }

    var username = req.Username?.Trim() ?? string.Empty;
    if (username.Length < 3)
    {
        return Results.BadRequest(new { message = "Nom d'utilisateur trop court (3 caractères minimum)." });
    }

    if (string.IsNullOrWhiteSpace(req.Password) || req.Password.Length < 4)
    {
        return Results.BadRequest(new { message = "Mot de passe trop court (4 caractères minimum)." });
    }

    if (await db.Accounts.AnyAsync(a => a.Username == username))
    {
        return Results.Conflict(new { message = "Ce nom d'utilisateur est déjà pris." });
    }

    var account = new AccountRecord
    {
        Username = username,
        PasswordHash = PasswordHasher.Hash(req.Password),
        Role = "user"
    };
    db.Accounts.Add(account);
    await db.SaveChangesAsync();

    // Log the new account in immediately.
    return Results.Ok(new LoginResponse(tokens.CreateToken(account.Username, account.Role)));
});

app.MapPost("/api/change-password", async (ChangePasswordRequest req, ClaimsPrincipal user, PadelDbContext db) =>
{
    var username = user.Identity?.Name;
    if (string.IsNullOrEmpty(username))
    {
        return Results.Unauthorized();
    }

    var account = await db.Accounts.FirstOrDefaultAsync(a => a.Username == username);
    if (account is null || !PasswordHasher.Verify(req.CurrentPassword, account.PasswordHash))
    {
        return Results.BadRequest(new { message = "Mot de passe actuel incorrect." });
    }

    if (string.IsNullOrWhiteSpace(req.NewPassword) || req.NewPassword.Length < 4)
    {
        return Results.BadRequest(new { message = "Le nouveau mot de passe est trop court." });
    }

    account.PasswordHash = PasswordHasher.Hash(req.NewPassword);
    await db.SaveChangesAsync();
    return Results.Ok();
}).RequireAuthorization("ApiJwt");

// --- Datasets (desktop API; JWT-authenticated) ---
var datasets = app.MapGroup("/api/datasets").RequireAuthorization("ApiJwt");

datasets.MapGet("/", async (ClaimsPrincipal user, PadelDbContext db) =>
{
    var owner = user.Identity?.Name ?? string.Empty;
    var list = await db.Datasets
        .Where(d => d.Owner == owner)
        .OrderByDescending(d => d.UpdatedUtc)
        .Select(d => new DatasetSummary(d.Id, d.Name, d.Version, d.UpdatedUtc))
        .ToListAsync();
    return Results.Ok(list);
});

datasets.MapGet("/{id}", async (string id, ClaimsPrincipal user, PadelDbContext db) =>
{
    var owner = user.Identity?.Name ?? string.Empty;
    var d = await db.Datasets.FirstOrDefaultAsync(x => x.Id == id && x.Owner == owner);
    return d is null
        ? Results.NotFound()
        : Results.Ok(new DatasetDetail(d.Id, d.Name, d.Version, d.Json));
});

datasets.MapPost("/", async (CreateDatasetRequest req, ClaimsPrincipal user, PadelDbContext db) =>
{
    var owner = user.Identity?.Name ?? string.Empty;
    var record = new DatasetRecord
    {
        Owner = owner,
        Name = req.Name,
        Json = req.Json,
        Version = 1,
        UpdatedUtc = DateTime.UtcNow
    };
    db.Datasets.Add(record);
    await db.SaveChangesAsync();
    return Results.Created($"/api/datasets/{record.Id}", new CreateDatasetResponse(record.Id, record.Version));
});

datasets.MapPut("/{id}", async (string id, SaveDatasetRequest req, ClaimsPrincipal user, PadelDbContext db) =>
{
    var owner = user.Identity?.Name ?? string.Empty;
    var record = await db.Datasets.FirstOrDefaultAsync(x => x.Id == id && x.Owner == owner);
    if (record is null)
    {
        return Results.NotFound();
    }

    // Optimistic concurrency: reject if the dataset changed since the client loaded it.
    if (record.Version != req.Version)
    {
        return Results.Conflict(new { message = "Dataset changed on the server. Reload before saving.", serverVersion = record.Version });
    }

    record.Name = req.Name;
    record.Json = req.Json;
    record.Version += 1;
    record.UpdatedUtc = DateTime.UtcNow;
    await db.SaveChangesAsync();
    return Results.Ok(new SaveDatasetResponse(record.Version));
});

datasets.MapDelete("/{id}", async (string id, ClaimsPrincipal user, PadelDbContext db) =>
{
    var owner = user.Identity?.Name ?? string.Empty;
    var record = await db.Datasets.FirstOrDefaultAsync(x => x.Id == id && x.Owner == owner);
    if (record is null)
    {
        return Results.NotFound();
    }

    db.Datasets.Remove(record);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

app.MapRazorComponents<App>();

app.Run();

static async Task SignInCookieAsync(HttpContext http, string username, string role)
{
    var claims = new[]
    {
        new Claim(ClaimTypes.Name, username),
        new Claim(ClaimTypes.Role, role)
    };
    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    await http.SignInAsync(
        CookieAuthenticationDefaults.AuthenticationScheme,
        new ClaimsPrincipal(identity),
        new AuthenticationProperties { IsPersistent = true });
}

// Loads the caller's dataset, applies a mutation, and saves it (bumping Version so
// the desktop sync notices). Redirects with ?ok=1 on success or ?error=… otherwise.
static async Task<IResult> ApplyAsync(
    string id, HttpContext http, PadelDbContext db,
    Func<PadelDataFile, bool> mutate, string redirectTo)
{
    var user = http.User.Identity?.Name;
    if (string.IsNullOrEmpty(user))
    {
        return Results.Redirect("/login");
    }

    var record = await db.Datasets.FirstOrDefaultAsync(d => d.Id == id && d.Owner == user);
    if (record is null)
    {
        return Results.NotFound();
    }

    PadelDataFile data;
    try
    {
        data = JsonSerializer.Deserialize<PadelDataFile>(record.Json) ?? new PadelDataFile();
    }
    catch
    {
        return Results.Redirect($"{redirectTo}?error=data");
    }

    if (!mutate(data))
    {
        return Results.Redirect($"{redirectTo}?error=1");
    }

    record.Json = JsonSerializer.Serialize(data);
    record.Version += 1;
    record.UpdatedUtc = DateTime.UtcNow;
    await db.SaveChangesAsync();
    return Results.Redirect($"{redirectTo}?ok=1");
}

// Adds the per-user Owner column to an existing database. EnsureCreated only
// creates missing tables, it never alters an existing one, so a DB created before
// per-user data existed needs this one-time, idempotent upgrade.
static void UpgradeSchema(PadelDbContext db)
{
    try
    {
        db.Database.ExecuteSqlRaw("ALTER TABLE \"Datasets\" ADD COLUMN \"Owner\" TEXT NOT NULL DEFAULT ''");
    }
    catch (Microsoft.Data.Sqlite.SqliteException)
    {
        // Column already exists (fresh DB created with it, or already upgraded).
        return;
    }

    // Pre-existing datasets had no owner; hand them to the default 'padel' login.
    db.Database.ExecuteSqlRaw("UPDATE \"Datasets\" SET \"Owner\" = 'padel' WHERE \"Owner\" = ''");
}

static string EnsureSigningKey(string dataDir)
{
    var path = Path.Combine(dataDir, "jwt-signing-key.txt");
    if (File.Exists(path))
    {
        var existing = File.ReadAllText(path).Trim();
        if (existing.Length >= 32)
        {
            return existing;
        }
    }

    var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
    File.WriteAllText(path, key);
    Console.WriteLine($"[Padel.Server] Generated a new JWT signing key at {path}. Keep this file; deleting it logs everyone out.");
    return key;
}

static void SeedAccount(PadelDbContext db, IConfiguration config, ILogger logger)
{
    if (db.Accounts.Any())
    {
        return;
    }

    var username = config["Seed:Username"];
    var password = config["Seed:Password"];
    var usingDefault = false;

    if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
    {
        // Zero-config first run: create a default login so the user is never stuck.
        username = "padel";
        password = "padel";
        usingDefault = true;
    }

    db.Accounts.Add(new AccountRecord
    {
        Username = username,
        PasswordHash = PasswordHasher.Hash(password),
        Role = "group"
    });
    db.SaveChanges();

    if (usingDefault)
    {
        logger.LogWarning(
            "Created a DEFAULT group login 'padel' / 'padel'. Change it from the app " +
            "(or set Seed__Username / Seed__Password before first run). Do NOT expose the " +
            "server to the internet with the default password.");
    }
    else
    {
        logger.LogInformation("Created initial group account '{Username}'.", username);
    }
}
