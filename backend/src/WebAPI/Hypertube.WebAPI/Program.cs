using Serilog;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.FileProviders;
using System.Text;
using Hypertube.Domain.Entities;
using Hypertube.Infrastructure.Persistence;
using Hypertube.Application.Authentication.Services;
using Hypertube.Application.Common.Services;
using Hypertube.Application.Profile.Services;
using Hypertube.Application.Movies.Services;
using Hypertube.Infrastructure.Authentication;
using Hypertube.Infrastructure.Services;
using Hypertube.Infrastructure.ExternalServices;
using Hypertube.WebAPI.Swagger;

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .WriteTo.File("logs/hypertube-.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();

try
{
    Log.Information("Starting Hypertube API");

    var builder = WebApplication.CreateBuilder(args);

    // Add Serilog
    builder.Host.UseSerilog();

    // Controllers
    builder.Services.AddControllers();

    // Database Context
    var connectionString = Environment.GetEnvironmentVariable("DATABASE_URL")
        ?? throw new InvalidOperationException("DATABASE_URL environment variable is required");

    builder.Services.AddDbContext<HypertubeDbContext>(options =>
        options.UseNpgsql(
            connectionString,
            npgsqlOptions => npgsqlOptions.MigrationsAssembly("Hypertube.Infrastructure")
        )
    );

    // Identity Configuration
    builder.Services.AddIdentity<User, IdentityRole<Guid>>(options =>
    {
        // Password settings
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequiredLength = 8;

        // User settings
        options.User.RequireUniqueEmail = true;

        // Sign-in settings
        options.SignIn.RequireConfirmedEmail = false; // Set true if email confirmation is needed
    })
    .AddEntityFrameworkStores<HypertubeDbContext>()
    .AddDefaultTokenProviders();

    // JWT Authentication Configuration
    var jwtSecret = Environment.GetEnvironmentVariable("JWT_SECRET")
        ?? throw new InvalidOperationException("JWT_SECRET environment variable is required");

    builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.SaveToken = true;
        options.RequireHttpsMetadata = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = Environment.GetEnvironmentVariable("JWT_ISSUER") ?? "Hypertube",
            ValidAudience = Environment.GetEnvironmentVariable("JWT_AUDIENCE") ?? "Hypertube",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ClockSkew = TimeSpan.Zero
        };
    })
    .AddGoogle(options =>
    {
        options.ClientId = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_ID") ?? string.Empty;
        options.ClientSecret = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_SECRET") ?? string.Empty;
        options.SignInScheme = IdentityConstants.ExternalScheme;
    });

    // Register Authentication Services
    builder.Services.AddScoped<ITokenService, TokenService>();
    builder.Services.AddScoped<IAuthenticationService, AuthenticationService>();
    builder.Services.AddScoped<IEmailService, EmailService>();
    builder.Services.AddScoped<IProfileService, ProfileService>();

    // Register Video Services
    builder.Services.AddScoped<IVideoConversionService, VideoConversionService>();
    builder.Services.AddScoped<IVideoCodecDetector, VideoCodecDetector>();
    builder.Services.AddScoped<IVideoRemuxService, VideoRemuxService>();

    // Register Subtitle Service
    builder.Services.AddHttpClient<ISubtitleService, SubtitleService>();

    // Register Torrent Search Services
    builder.Services.AddHttpClient<YtsService>();
    builder.Services.AddHttpClient<PirateBayService>();
    builder.Services.AddHttpClient<IMovieMetadataService, TmdbService>();
    builder.Services.AddScoped<ITorrentSearchService, TorrentSearchService>();

    // Register Movie Services
    builder.Services.AddScoped<IMovieService, MovieService>();
    builder.Services.AddScoped<ICommentService, CommentService>();

    // Register Torrent Seeding Service (BackgroundService for continuous seeding)
    builder.Services.AddSingleton<TorrentSeedingService>();
    builder.Services.AddHostedService(sp => sp.GetRequiredService<TorrentSeedingService>());

    // Register Torrent Download Service (Singleton to maintain active downloads state)
    var downloadDirectory = Environment.GetEnvironmentVariable("DOWNLOAD_DIRECTORY")
        ?? Path.Combine(Directory.GetCurrentDirectory(), "downloads");
    builder.Services.AddSingleton<ITorrentDownloadService>(sp =>
    {
        var logger = sp.GetRequiredService<ILogger<TorrentDownloadService>>();
        var httpClient = new HttpClient();
        var seedingService = sp.GetRequiredService<TorrentSeedingService>();
        return new TorrentDownloadService(logger, httpClient, downloadDirectory, seedingService, sp);
    });

    // CORS Configuration
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("AllowFrontend", policy =>
        {
            policy.WithOrigins("http://localhost:3000", "http://localhost:5173")
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
        });
    });

    // Swagger/OpenAPI
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new() {
            Title = "Hypertube API",
            Version = "v1",
            Description = "API for Hypertube"
        });

        // Include XML comments
        var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
        var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
        if (File.Exists(xmlPath))
        {
            c.IncludeXmlComments(xmlPath);
        }

        // Add JWT Authentication to Swagger
        c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
        {
            Description = "JWT Authorization header using the Bearer scheme. Example: \"Authorization: Bearer {token}\"",
            Name = "Authorization",
            In = Microsoft.OpenApi.Models.ParameterLocation.Header,
            Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
            Scheme = "Bearer"
        });

        c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
        {
            {
                new Microsoft.OpenApi.Models.OpenApiSecurityScheme
                {
                    Reference = new Microsoft.OpenApi.Models.OpenApiReference
                    {
                        Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                        Id = "Bearer"
                    }
                },
                Array.Empty<string>()
            }
        });

        // Add file upload support
        c.OperationFilter<FileUploadOperationFilter>();
    });

    var app = builder.Build();

    // Apply database migrations automatically
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<HypertubeDbContext>();
        try
        {
            Log.Information("Applying database migrations...");
            db.Database.Migrate();
            Log.Information("Database migrations applied successfully");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "An error occurred while applying database migrations");
        }
    }

    // Configure the HTTP request pipeline

    // Swagger (enabled in Development)
    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "Hypertube API v1");
            c.RoutePrefix = "swagger";
        });
    }

    // Serilog request logging
    app.UseSerilogRequestLogging();

    // CORS
    app.UseCors("AllowFrontend");

    // Static files for uploads
    var uploadsPath = Path.Combine(Directory.GetCurrentDirectory(), "uploads");
    if (!Directory.Exists(uploadsPath))
    {
        Directory.CreateDirectory(uploadsPath);
        Log.Information("Created uploads directory: {Path}", uploadsPath);
    }
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(uploadsPath),
        RequestPath = "/uploads"
    });

    // Authentication & Authorization
    app.UseAuthentication();
    app.UseAuthorization();

    // Map Controllers
    app.MapControllers();

    // Root endpoint
    app.MapGet("/", () => new
    {
        service = "Hypertube API",
        version = "1.0",
        status = "running",
        swagger = "/swagger"
    }).WithName("Root").WithOpenApi();

    Log.Information("Hypertube API started successfully on port 5000");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
