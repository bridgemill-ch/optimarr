using Optimarr.Services;
using Optimarr.Controllers;
using Optimarr.Data;
using System.Text.Json.Serialization;
using Serilog;
using Serilog.Events;
using Microsoft.EntityFrameworkCore;
using System.Linq;

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("System", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithEnvironmentName()
    .Enrich.WithMachineName()
    .Enrich.WithThreadId()
    .WriteTo.Console(
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(
        path: "logs/optimarr-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    Log.Information("Starting optimarr application");

    var builder = WebApplication.CreateBuilder(args);

    // Ensure required directories exist and are writable
    var contentRoot = builder.Environment.ContentRootPath;
    var configDir = Path.Combine(contentRoot, "config");
    var dataDir = Path.Combine(contentRoot, "data");
    var logsDir = Path.Combine(contentRoot, "logs");
    
    // Create directories if they don't exist and verify write permissions
    var directoriesToCheck = new[]
    {
        new { Path = configDir, Name = "config", ReadOnly = true },
        new { Path = dataDir, Name = "data", ReadOnly = false },
        new { Path = logsDir, Name = "logs", ReadOnly = false }
    };
    
    var errors = new List<string>();
    
    foreach (var dir in directoriesToCheck)
    {
        try
        {
            // Create directory if it doesn't exist
            if (!Directory.Exists(dir.Path))
            {
                Directory.CreateDirectory(dir.Path);
                Log.Information("Created {Name} directory: {Path}", dir.Name, dir.Path);
            }
            else
            {
                Log.Information("{Name} directory exists: {Path}", dir.Name, dir.Path);
            }
            
            // Check write permissions (skip for read-only config)
            if (!dir.ReadOnly)
            {
                var testFile = Path.Combine(dir.Path, ".write-test");
                try
                {
                    // Try to write a test file
                    File.WriteAllText(testFile, DateTime.UtcNow.ToString("O"));
                    File.Delete(testFile);
                    Log.Information("{Name} directory is writable: {Path}", dir.Name, dir.Path);
                }
                catch (UnauthorizedAccessException ex)
                {
                    var error = $"{dir.Name} directory is not writable: {dir.Path}. Error: {ex.Message}";
                    errors.Add(error);
                    Log.Error(ex, "Permission denied for {Name} directory: {Path}", dir.Name, dir.Path);
                }
                catch (Exception ex)
                {
                    var error = $"{dir.Name} directory write test failed: {dir.Path}. Error: {ex.Message}";
                    errors.Add(error);
                    Log.Error(ex, "Failed to write test file in {Name} directory: {Path}", dir.Name, dir.Path);
                }
            }
            else
            {
                // For read-only directories, just check if it exists and is readable
                if (!Directory.Exists(dir.Path))
                {
                    var error = $"{dir.Name} directory does not exist and could not be created: {dir.Path}";
                    errors.Add(error);
                    Log.Error("{Name} directory does not exist: {Path}", dir.Name, dir.Path);
                }
                else
                {
                    Log.Information("{Name} directory is readable: {Path}", dir.Name, dir.Path);
                }
            }
        }
        catch (Exception ex)
        {
            var error = $"Failed to create or access {dir.Name} directory: {dir.Path}. Error: {ex.Message}";
            errors.Add(error);
            Log.Error(ex, "Failed to create or access {Name} directory: {Path}", dir.Name, dir.Path);
        }
    }
    
    // If there are errors, throw an exception with all error messages
    if (errors.Any())
    {
        var errorMessage = "Directory permission errors detected:\n" + string.Join("\n", errors);
        Log.Fatal(errorMessage);
        throw new InvalidOperationException(errorMessage);
    }

    // Configure appsettings.json location - check config folder first, then root
    var configPath = Path.Combine(builder.Environment.ContentRootPath, "config", "appsettings.json");
    var rootConfigPath = Path.Combine(builder.Environment.ContentRootPath, "appsettings.json");
    
    try
    {
        // If config folder doesn't have appsettings.json but root does, copy it (if config is writable)
        if (!File.Exists(configPath) && File.Exists(rootConfigPath))
        {
            try
            {
                // Try to copy from root to config (only works if config folder is writable)
                File.Copy(rootConfigPath, configPath, overwrite: false);
                Log.Information("Copied appsettings.json from root to config folder");
            }
            catch (UnauthorizedAccessException)
            {
                // Config folder is read-only, that's okay - we'll use root config
                Log.Information("Config folder is read-only, using appsettings.json from root");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not copy appsettings.json to config folder, will use root config");
            }
        }
        
        // Load config from config folder if it exists
        if (File.Exists(configPath))
        {
            builder.Configuration.AddJsonFile(configPath, optional: false, reloadOnChange: true);
            Log.Information("Loaded appsettings.json from config folder: {ConfigPath}", configPath);
        }
        // Also load from root if it exists (for development or if config folder doesn't have it)
        else if (File.Exists(rootConfigPath))
        {
            builder.Configuration.AddJsonFile(rootConfigPath, optional: true, reloadOnChange: true);
            Log.Information("Loaded appsettings.json from root: {RootConfigPath}", rootConfigPath);
        }
        else
        {
            // If neither exists, log a warning (but don't fail - ASP.NET Core has defaults)
            Log.Warning("No appsettings.json found in config folder ({ConfigPath}) or root ({RootConfigPath}). Using default configuration.", configPath, rootConfigPath);
        }
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Error loading appsettings.json. The application will continue with default configuration.");
        // Don't throw - ASP.NET Core can work without appsettings.json
    }

    // Use Serilog for logging
    builder.Host.UseSerilog();

    // Add services to the container
    builder.Services.AddControllers()
        .AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });
    
    // Configure Kestrel server options for long-running requests (10 minutes)
    builder.Services.Configure<Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions>(options =>
    {
        options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(10);
        options.Limits.RequestHeadersTimeout = TimeSpan.FromMinutes(10);
    });

    // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
        {
            Title = "Optimarr API",
            Version = "v1",
            Description = "Media Optimization API with Servarr Integration"
        });
    });

    // Configure database (data directory already created above)
    var dbPath = Path.Combine(builder.Environment.ContentRootPath, "data", "optimarr.db");
    
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseSqlite($"Data Source={dbPath}"));

    // Register application services
    builder.Services.AddScoped<VideoAnalyzerService>(sp => 
        new VideoAnalyzerService(sp.GetRequiredService<IConfiguration>(), sp.GetService<ILogger<VideoAnalyzerService>>()));
    builder.Services.AddScoped<LibraryScannerService>();
    builder.Services.AddSingleton<SonarrService>(sp => 
        new SonarrService(sp.GetRequiredService<IConfiguration>(), 
            sp.GetService<ILogger<SonarrService>>()));
    builder.Services.AddSingleton<RadarrService>(sp => 
        new RadarrService(sp.GetRequiredService<IConfiguration>(), 
            sp.GetService<ILogger<RadarrService>>()));
    builder.Services.AddSingleton<JellyfinService>(sp => 
        new JellyfinService(sp.GetRequiredService<IConfiguration>(), 
            sp.GetService<ILogger<JellyfinService>>()));

    // CORS for local development
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("AllowAll", policy =>
        {
            policy.AllowAnyOrigin()
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        });
    });

    var app = builder.Build();

    // Verify all required directories exist
    var requiredDirs = new[] { configDir, dataDir, logsDir };
    var missingDirs = requiredDirs.Where(dir => !Directory.Exists(dir)).ToList();
    if (missingDirs.Any())
    {
        Log.Warning("Some required directories are missing: {MissingDirs}", string.Join(", ", missingDirs));
        foreach (var dir in missingDirs)
        {
            try
            {
                Directory.CreateDirectory(dir);
                Log.Information("Created missing directory: {Dir}", dir);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to create directory: {Dir}", dir);
            }
        }
    }
    else
    {
        Log.Information("All required directories verified: config, data, logs");
    }

    // Ensure database is created
    using (var scope = app.Services.CreateScope())
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        dbContext.Database.EnsureCreated();
        Log.Information("Database initialized at: {DbPath}", dbPath);
    }

    // Check MediaInfo CLI tool availability
    try
    {
        Log.Information("Checking MediaInfo CLI tool availability...");
        
        var processStartInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "mediainfo",
            Arguments = "--Version",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        
        try
        {
            using var process = System.Diagnostics.Process.Start(processStartInfo);
            if (process != null)
            {
                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                
                if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                {
                    Log.Information("✓ MediaInfo CLI tool is available. Version: {Version}", output.Trim());
                }
                else
                {
                    Log.Error("✗ MediaInfo CLI tool failed. Exit code: {ExitCode}, Error: {Error}", 
                        process.ExitCode, error);
                }
            }
            else
            {
                Log.Error("✗ Failed to start mediainfo process");
            }
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            Log.Error(ex, "✗ MediaInfo CLI tool not found. Make sure 'mediainfo' is installed and in PATH.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "✗ Error checking MediaInfo CLI: {Message}", ex.Message);
        }
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "Error checking MediaInfo CLI availability");
    }

    // Configure the HTTP request pipeline
    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "Optimarr API v1");
        });
    }

    app.UseCors("AllowAll");

    // Serve static files (web UI)
    app.UseDefaultFiles();
    app.UseStaticFiles();

    app.UseAuthorization();

    app.MapControllers();

    // Fallback to index.html for SPA routing
    app.MapFallbackToFile("index.html");

    Log.Information("optimarr application started successfully");
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

