using AspNetCoreRateLimit;
using BNKaraoke.Api.Controllers;
using BNKaraoke.Api.Data;
using BNKaraoke.Api.Hubs;
using BNKaraoke.Api.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddUserSecrets<Program>();
builder.Configuration.AddEnvironmentVariables();

builder.Services.AddLogging(logging =>
{
    logging.AddConsole();
    logging.AddDebug();
    logging.SetMinimumLevel(LogLevel.Information);
});

builder.Services.AddSingleton(builder.Configuration);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrEmpty(connectionString))
{
    throw new InvalidOperationException("Database connection string 'DefaultConnection' is missing.");
}
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));

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
        RoleClaimType = ClaimTypes.Role
    };
    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = context =>
        {
            var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
            logger.LogDebug("Token validated for user: {User}", context.Principal?.Identity?.Name ?? "Unknown");
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
                        logger.LogDebug("Added Name claim: {SubClaim}", subClaim);
                    }
                }
            }
            return Task.CompletedTask;
        },
        OnAuthenticationFailed = context =>
        {
            var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
            logger.LogError(context.Exception, "Authentication failed");
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Singer", policy => policy.RequireAuthenticatedUser().RequireRole("Singer"));
    options.AddPolicy("SongController", policy => policy.RequireAuthenticatedUser().RequireRole("Song Manager"));
    options.AddPolicy("SongManager", policy => policy.RequireAuthenticatedUser().RequireRole("Song Manager"));
    options.AddPolicy("UserManager", policy => policy.RequireAuthenticatedUser().RequireRole("User Manager"));
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
    var allowedOrigins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() ?? new[] { "https://www.bnkaraoke.com", "http://localhost:8080" };
    options.AddPolicy("AllowNetwork", policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

builder.Services.AddControllers()
    .AddApplicationPart(typeof(EventController).Assembly)
    .AddControllersAsServices();

builder.Services.AddTransient<EventController>();
builder.Services.AddSignalR(options =>
{
    options.KeepAliveInterval = TimeSpan.FromSeconds(15); // Increased to 15 seconds
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

app.Use(async (context, next) =>
{
    var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
    logger.LogDebug("Connection received: {Method} {Path} from {RemoteIp}", context.Request.Method, context.Request.Path, context.Connection.RemoteIpAddress);
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
        var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "Error processing request: {Method} {Path}", context.Request.Method, context.Request.Path);
        throw;
    }
});

app.Use(async (context, next) =>
{
    var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
    logger.LogDebug("Processing request: {Method} {Path} from Origin: {Origin}", context.Request.Method, context.Request.Path, context.Request.Headers["Origin"]);
    var requestorId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "Unknown";
    await next.Invoke();
    if (context.Response.StatusCode == 404 && context.Request.Path.Value?.StartsWith("/api/events/", StringComparison.OrdinalIgnoreCase) == true &&
        context.Request.Path.Value.EndsWith("/leave", StringComparison.OrdinalIgnoreCase))
    {
        var eventId = context.Request.Path.Value.Split('/')[3];
        logger.LogWarning("404 on /leave for EventId: {EventId}, RequestorId: {RequestorId}", eventId, requestorId);
    }
});

app.Use(async (context, next) =>
{
    var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
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
                user.LastActivity = DateTime.UtcNow;
                await dbContext.SaveChangesAsync();

                var path = context.Request.Path.Value?.ToLower();
                if (path != null && path.Contains("/events/"))
                {
                    var eventIdStr = path.Split("/events/")[1].Split('/')[0];
                    if (int.TryParse(eventIdStr, out int eventId))
                    {
                        var singerStatus = await dbContext.SingerStatus
                            .FirstOrDefaultAsync(ss => ss.EventId == eventId && ss.RequestorId == userId);
                        if (singerStatus != null && path.Contains("/attendance/check-in"))
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
                            logger.LogInformation("Logged Join for UserId: {UserId}, EventId: {EventId}", userId, eventId);
                        }
                        else if (singerStatus != null && path.Contains("/attendance/break"))
                        {
                            var isOnBreak = context.Request.Method == "POST" && path.EndsWith("/start");
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
                            logger.LogInformation("Logged Break status {Status} for UserId: {UserId}, EventId: {EventId}", isOnBreak ? "On" : "Off", userId, eventId);
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
            var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
            var error = context.Features.Get<IExceptionHandlerFeature>();
            logger.LogError(error?.Error, "Unhandled exception at {Path}", context.Request.Path);
            context.Response.StatusCode = 500;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new { error = "An unexpected error occurred" });
        });
    });
}

app.UseStaticFiles();
app.UseRouting();
app.UseCors("AllowNetwork");
app.UseAuthentication();
app.UseAuthorization();
app.UseIpRateLimiting();

app.MapControllers();
app.MapHub<KaraokeDJHub>("/hubs/karaoke-dj");

using (var scope = app.Services.CreateScope())
{
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    await context.Database.MigrateAsync();

    var roles = new[] { "Singer", "Karaoke DJ", "User Manager", "Queue Manager", "Song Manager", "Event Manager", "Application Manager" };
    foreach (var role in roles)
    {
        if (!await roleManager.RoleExistsAsync(role))
        {
            await roleManager.CreateAsync(new IdentityRole(role));
        }
    }

    var users = new[]
    {
        new { PhoneNumber = "1234567891", Roles = new[] {"Singer"}, FirstName = "Singer", LastName = "One" },
        new { PhoneNumber = "1234567895", Roles = new[] {"Song Manager"}, FirstName = "Song", LastName = "Five" }
    };

    foreach (var user in users)
    {
        var appUser = new ApplicationUser
        {
            UserName = user.PhoneNumber,
            PhoneNumber = user.PhoneNumber,
            FirstName = user.FirstName,
            LastName = user.LastName
        };

        if (await userManager.FindByNameAsync(appUser.UserName) == null)
        {
            var result = await userManager.CreateAsync(appUser, "Pwd1234.");
            if (result.Succeeded)
            {
                await userManager.AddToRolesAsync(appUser, user.Roles);
            }
            else
            {
                var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
                logger.LogError("Failed to create user {UserName}: {Errors}", appUser.UserName, string.Join(", ", result.Errors.Select(e => e.Description)));
            }
        }
    }
}

app.Run();