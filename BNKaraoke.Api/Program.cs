using AspNetCoreRateLimit;
using BNKaraoke.Api.Controllers;
using BNKaraoke.Api.Data;
using BNKaraoke.Api.Hubs;
using BNKaraoke.Api.Models;
using BNKaraoke.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

var builder = WebApplication.CreateBuilder(args);

// Add Serilog
builder.Host.UseSerilog((context, configuration) =>
{
    configuration.ReadFrom.Configuration(context.Configuration);
    Log.Information("Serilog initialized with configuration from {ConfigSource}", context.Configuration["Serilog:WriteTo:0:Args:path"]);
});

builder.Configuration.AddUserSecrets<Program>();
builder.Configuration.AddEnvironmentVariables();

// Replace default logging with Serilog
builder.Services.AddLogging(logging =>
{
    logging.ClearProviders();
    logging.SetMinimumLevel(LogLevel.Information);
});

builder.Services.AddSingleton(builder.Configuration);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrEmpty(connectionString))
{
    Log.Error("Database connection string 'DefaultConnection' is missing.");
    throw new InvalidOperationException("Database connection string 'DefaultConnection' is missing.");
}
builder.Services.AddPooledDbContextFactory<ApplicationDbContext>(options =>
{
    options.UseNpgsql(connectionString);
    Log.Information("DbContextFactory configured with connection string: {ConnectionString}", connectionString);
});
builder.Services.AddScoped<ApplicationDbContext>(provider =>
    provider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContext());

builder.Services.AddSingleton<ISongCacheService, SongCacheService>();

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.SignIn.RequireConfirmedAccount = true;
    options.User.RequireUniqueEmail = false;
    options.Password.RequireDigit = true;
    options.Password.RequiredLength = 8;
    options.Password.RequireNonAlphanumeric = true;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

var keyString = builder.Configuration["JwtSettings:SecretKey"];
if (string.IsNullOrEmpty(keyString) || Encoding.UTF8.GetBytes(keyString).Length < 32)
{
    Log.Error("Jwt secret key is missing or too short.");
    throw new InvalidOperationException("Jwt secret key is missing or too short.");
}

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["JwtSettings:Issuer"],
        ValidAudience = builder.Configuration["JwtSettings:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(keyString)),
        NameClaimType = ClaimTypes.Name,
        RoleClaimType = ClaimTypes.Role,
        ClockSkew = TimeSpan.FromMinutes(5)
    };
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs/karaoke-dj"))
            {
                context.Token = accessToken;
                Log.Debug("Extracted access_token for SignalR: {Token}", accessToken.ToString());
            }
            return Task.CompletedTask;
        },
        OnTokenValidated = context =>
        {
            Log.Debug("Token validated for user: {User}, Path: {Path}, Roles: {Roles}",
                context.Principal?.Identity?.Name ?? "Unknown",
                context.Request.Path,
                string.Join(", ", context.Principal?.FindAll(ClaimTypes.Role).Select(c => c.Value) ?? Array.Empty<string>()));
            var claims = context.Principal?.Claims;
            if (claims != null)
            {
                var subClaim = claims.FirstOrDefault(c => c.Type == "sub")?.Value
                            ?? claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;
                if (!string.IsNullOrEmpty(subClaim))
                {
                    var identity = context.Principal?.Identity as ClaimsIdentity;
                    if (identity != null && !identity.HasClaim(c => c.Type == ClaimTypes.Name))
                    {
                        identity.AddClaim(new Claim(ClaimTypes.Name, subClaim));
                        Log.Debug("Added Name claim: {SubClaim}", subClaim);
                    }
                }
            }
            return Task.CompletedTask;
        },
        OnAuthenticationFailed = context =>
        {
            Log.Error(context.Exception, "JWT authentication failed for {Path}, Token: {Token}",
                context.Request.Path,
                context.Request.Query["access_token"].ToString());
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Singer", policy => policy.RequireAuthenticatedUser().RequireRole("Singer"));
    options.AddPolicy("SongController", policy => policy.RequireAuthenticatedUser().RequireRole("Song Manager"));
    options.AddPolicy("SongManager", policy => policy.RequireAuthenticatedUser().RequireRole("Song Manager"));
    options.AddPolicy("User Manager", policy => policy.RequireAuthenticatedUser().RequireRole("User Manager"));
    options.AddPolicy("KaraokeDJ", policy => policy.RequireAuthenticatedUser().RequireRole("Karaoke DJ"));
    options.AddPolicy("QueueManager", policy => policy.RequireAuthenticatedUser().RequireRole("Queue Manager"));
    options.AddPolicy("EventManager", policy => policy.RequireAuthenticatedUser().RequireRole("Event Manager"));
    options.AddPolicy("ApplicationManager", policy => policy.RequireAuthenticatedUser().RequireRole("Application Manager"));
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
    options.InvokeHandlersAfterFailure = false;
});

builder.Services.AddMemoryCache();
builder.Services.Configure<IpRateLimitOptions>(options =>
{
    options.GeneralRules = new List<RateLimitRule>
    {
        new RateLimitRule
        {
            Endpoint = "POST:/api/dj/singer/update",
            Period = "1s",
            Limit = 5
        }
    };
    options.QuotaExceededResponse = new QuotaExceededResponse
    {
        StatusCode = 429,
        ContentType = "application/json",
        Content = "{\"error\": \"Too many requests. Please try again later.\"}"
    };
});
builder.Services.AddSingleton<IIpPolicyStore, MemoryCacheIpPolicyStore>();
builder.Services.AddSingleton<IRateLimitCounterStore, MemoryCacheRateLimitCounterStore>();
builder.Services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();
builder.Services.AddSingleton<IProcessingStrategy, AsyncKeyLockProcessingStrategy>();

builder.Services.AddCors(options =>
{
    var isDevelopment = builder.Environment.IsDevelopment();
    if (isDevelopment)
    {
        Log.Information("CORS configured to allow any origin in development");
        options.AddPolicy("AllowNetwork", policy =>
        {
            policy.SetIsOriginAllowed(_ => true)
                  .AllowAnyMethod()
                  .AllowAnyHeader()
                  .AllowCredentials();
        });
    }
    else
    {
        options.AddPolicy("AllowNetwork", policy =>
        {
            policy.SetIsOriginAllowed(origin =>
            {
                if (string.IsNullOrWhiteSpace(origin))
                    return false;
                var host = new Uri(origin).Host;
                return host.EndsWith(".bnkaraoke.com") || host == "bnkaraoke.com";
            })
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials();
        });
    }
});

builder.Services.AddControllers()
    .AddApplicationPart(typeof(EventController).Assembly)
    .AddControllersAsServices();

builder.Services.AddTransient<EventController>();
builder.Services.AddSignalR(options =>
{
    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
    options.MaximumReceiveMessageSize = 102400;
});
builder.Services.AddHttpClient();

builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "BNKaraoke API",
        Version = "v1",
        Description = "API for managing karaoke users and songs"
    });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        In = ParameterLocation.Header,
        Description = "Please enter JWT with Bearer into field",
        Name = "Authorization",
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

app.UseSerilogRequestLogging();

app.UseCors("AllowNetwork");
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.Use(async (context, next) =>
{
    if (context.User.Identity?.IsAuthenticated == true && context.Request.Path.Value?.ToLower().StartsWith("/api/events/") == true)
    {
        var userName = context.User.Identity.Name ?? "Unknown";
        var roles = context.User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        Log.Information("Access attempt to {Path} by UserName: {UserName}, Roles: {Roles}",
            context.Request.Path, userName, string.Join(", ", roles));
        if (context.Request.Path.Value.ToLower().Contains("/manage") && !roles.Contains("Event Manager"))
        {
            Log.Warning("Authorization failed for {Path}. UserName: {UserName} lacks Event Manager role.",
                context.Request.Path, userName);
            context.Response.StatusCode = 403;
            await context.Response.WriteAsJsonAsync(new { error = "Missing Event Manager role" });
            return;
        }
    }
    await next();
});
app.UseIpRateLimiting();

app.Use(async (context, next) =>
{
    Log.Debug("Connection received: {Method} {Path} from {RemoteIp}", context.Request.Method, context.Request.Path, context.Connection.RemoteIpAddress?.ToString() ?? "Unknown");
    await next.Invoke();
});

app.Use(async (context, next) =>
{
    try
    {
        await next.Invoke();
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Error processing request: {Method} {Path}", context.Request.Method, context.Request.Path);
        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new { error = "An unexpected error occurred" });
    }
});

app.Use(async (context, next) =>
{
    Log.Debug("Processing request: {Method} {Path} from Origin: {Origin}", context.Request.Method, context.Request.Path, context.Request.Headers["Origin"]);
    var requestorId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "Unknown";
    await next.Invoke();
    if (context.Response.StatusCode == 404 && context.Request.Path.Value?.StartsWith("/api/events/", StringComparison.OrdinalIgnoreCase) == true &&
        context.Request.Path.Value.EndsWith("/leave", StringComparison.OrdinalIgnoreCase))
    {
        var eventId = context.Request.Path.Value.Split('/')[3];
        Log.Warning("404 on /leave for EventId: {EventId}, RequestorId: {RequestorId}", eventId, requestorId);
    }
});

app.Use(async (context, next) =>
{
    if (context.User.Identity?.IsAuthenticated == true)
    {
        var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!string.IsNullOrEmpty(userId))
        {
            using var scope = app.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<KaraokeDJHub>>();
            var user = await dbContext.Users.FindAsync(userId);
            if (user != null)
            {
                const int maxRetries = 3;
                for (int retry = 0; retry < maxRetries; retry++)
                {
                    try
                    {
                        user.LastActivity = DateTime.UtcNow;
                        await dbContext.SaveChangesAsync();
                        break;
                    }
                    catch (DbUpdateConcurrencyException ex)
                    {
                        if (retry < maxRetries - 1)
                        {
                            Log.Warning(ex, "Concurrency failure updating LastActivity for UserId: {UserId}, Attempt: {Attempt}", userId, retry + 1);
                            await dbContext.Entry(user).ReloadAsync();
                        }
                        else
                        {
                            Log.Error(ex, "Failed to update LastActivity for UserId: {UserId} after {MaxRetries} attempts", userId, maxRetries);
                            throw;
                        }
                    }
                }

                var path = context.Request.Path.Value?.ToLower();
                if (path != null && path.Contains("/events/") && path.Contains("/attendance/"))
                {
                    var eventIdStr = path.Split("/events/")[1].Split('/')[0];
                    if (int.TryParse(eventIdStr, out int eventId) &&
                        (path.EndsWith("/check-in") || path.EndsWith("/check-out") || path.EndsWith("/break/start") || path.EndsWith("/break/end")))
                    {
                        var singerStatus = await dbContext.SingerStatus
                            .FirstOrDefaultAsync(ss => ss.EventId == eventId && ss.RequestorId == userId);
                        if (singerStatus != null)
                        {
                            if (path.EndsWith("/check-in"))
                            {
                                singerStatus.IsJoined = true;
                                singerStatus.IsLoggedIn = true;
                                singerStatus.UpdatedAt = DateTime.UtcNow;
                                await dbContext.SaveChangesAsync();
                                await hubContext.Clients.Group($"Event_{eventId}")
                                    .SendAsync("SingerStatusUpdated", new
                                    {
                                        UserId = userId,
                                        EventId = eventId,
                                        DisplayName = $"{user.FirstName} {user.LastName}".Trim(),
                                        IsLoggedIn = true,
                                        IsJoined = true,
                                        IsOnBreak = singerStatus.IsOnBreak
                                    });
                                Log.Information("Logged Join for UserId: {UserId}, EventId: {EventId}", userId, eventId);
                            }
                            else if (path.EndsWith("/check-out"))
                            {
                                singerStatus.IsJoined = false;
                                singerStatus.IsLoggedIn = true;
                                singerStatus.IsOnBreak = false;
                                singerStatus.UpdatedAt = DateTime.UtcNow;
                                await dbContext.SaveChangesAsync();
                                await hubContext.Clients.Group($"Event_{eventId}")
                                    .SendAsync("SingerStatusUpdated", new
                                    {
                                        UserId = userId,
                                        EventId = eventId,
                                        DisplayName = $"{user.FirstName} {user.LastName}".Trim(),
                                        IsLoggedIn = true,
                                        IsJoined = false,
                                        IsOnBreak = false
                                    });
                                Log.Information("Logged CheckOut for UserId: {UserId}, EventId: {EventId}", userId, eventId);
                            }
                            else if (path.Contains("/break"))
                            {
                                var isOnBreak = path.EndsWith("/start");
                                singerStatus.IsOnBreak = isOnBreak;
                                singerStatus.UpdatedAt = DateTime.UtcNow;
                                await dbContext.SaveChangesAsync();
                                await hubContext.Clients.Group($"Event_{eventId}")
                                    .SendAsync("SingerStatusUpdated", new
                                    {
                                        UserId = userId,
                                        EventId = eventId,
                                        DisplayName = $"{user.FirstName} {user.LastName}".Trim(),
                                        IsLoggedIn = singerStatus.IsLoggedIn,
                                        IsJoined = singerStatus.IsJoined,
                                        IsOnBreak = isOnBreak
                                    });
                                Log.Information("Logged Break status {Status} for UserId: {UserId}, EventId: {EventId}", isOnBreak ? "On" : "Off", userId, eventId);
                            }
                        }
                    }
                }
            }
        }
    }
    await next();
});

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "BNKaraoke API v1");
        c.RoutePrefix = "swagger";
    });
}
else
{
    app.UseExceptionHandler(errorApp =>
    {
        errorApp.Run(async context =>
        {
            var error = context.Features.Get<IExceptionHandlerFeature>();
            Log.Error(error?.Error, "Unhandled exception at {Path}", context.Request.Path);
            context.Response.StatusCode = 500;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new { error = "An unexpected error occurred" });
        });
    });
}

app.UseStaticFiles();
app.MapControllers();
app.MapHub<KaraokeDJHub>("/hubs/karaoke-dj");

Log.Information("Application starting...");
app.Run();