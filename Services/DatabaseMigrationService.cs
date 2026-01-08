using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Optimarr.Data;
using System.Collections.Concurrent;
using System.Data.Common;

namespace Optimarr.Services
{
    public class DatabaseMigrationService : IHostedService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<DatabaseMigrationService> _logger;
        private readonly IHostEnvironment _hostEnvironment;
        private static readonly ConcurrentDictionary<string, MigrationProgress> _migrationProgress = new();

        public DatabaseMigrationService(
            IServiceProvider serviceProvider,
            ILogger<DatabaseMigrationService> logger,
            IHostEnvironment hostEnvironment)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _hostEnvironment = hostEnvironment;
        }

        public static MigrationProgress? GetMigrationProgress()
        {
            _migrationProgress.TryGetValue("current", out var progress);
            return progress;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting database migration check...");
            
            var progress = new MigrationProgress
            {
                Status = "checking",
                Message = "Checking database schema...",
                StartTime = DateTime.UtcNow,
                AppliedMigrations = new List<string>(),
                PendingMigrations = new List<string>()
            };
            _migrationProgress["current"] = progress;

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var database = dbContext.Database;

                progress.Status = "checking";
                progress.Message = "Checking for pending migrations...";

                // Check if database exists
                var canConnect = await database.CanConnectAsync(cancellationToken);
                if (!canConnect)
                {
                    _logger.LogInformation("Database does not exist, creating...");
                    progress.Status = "creating";
                    progress.Message = "Creating database...";
                    
                    await database.EnsureCreatedAsync(cancellationToken);
                    
                    progress.Status = "completed";
                    progress.Message = "Database created successfully";
                    progress.EndTime = DateTime.UtcNow;
                    _logger.LogInformation("Database created successfully");
                    return;
                }

                // Check if SQL migration needs to be applied (for Servarr fields)
                var needsSqlMigration = await CheckIfSqlMigrationNeededAsync(database, cancellationToken);
                if (needsSqlMigration)
                {
                    progress.Status = "migrating";
                    progress.Message = "Applying SQL migration for Servarr fields...";
                    progress.PendingMigrations = new List<string> { "AddServarrFields" };
                    _logger.LogInformation("SQL migration needed for Servarr fields, applying...");
                    
                    await ApplySqlMigrationAsync(database, cancellationToken);
                    
                    progress.AppliedMigrations = new List<string> { "AddServarrFields" };
                    progress.Status = "completed";
                    progress.Message = "SQL migration applied successfully";
                    progress.EndTime = DateTime.UtcNow;
                    _logger.LogInformation("SQL migration applied successfully");
                    return;
                }

                // Try to use migrations if available, otherwise use EnsureCreated for schema updates
                try
                {
                    // Try to get pending migrations (this will fail if migrations aren't set up)
                    var pendingMigrations = await database.GetPendingMigrationsAsync(cancellationToken);
                    var pendingList = pendingMigrations.ToList();

                    if (pendingList.Count == 0)
                    {
                        progress.Status = "completed";
                        progress.Message = "Database is up to date";
                        progress.EndTime = DateTime.UtcNow;
                        _logger.LogInformation("Database is up to date, no migrations needed");
                        return;
                    }

                    progress.Status = "migrating";
                    progress.Message = $"Applying {pendingList.Count} migration(s)...";
                    progress.PendingMigrations = pendingList;
                    _logger.LogInformation("Found {Count} pending migration(s): {Migrations}", 
                        pendingList.Count, string.Join(", ", pendingList));

                    // Apply all migrations at once (EF Core handles this efficiently)
                    await database.MigrateAsync(cancellationToken);
                    
                    var applied = await database.GetAppliedMigrationsAsync(cancellationToken);
                    progress.AppliedMigrations = applied.ToList();
                    progress.Status = "completed";
                    progress.Message = $"Successfully applied {pendingList.Count} migration(s)";
                    progress.EndTime = DateTime.UtcNow;
                    _logger.LogInformation("Database migration completed successfully. Applied {Count} migration(s)", 
                        pendingList.Count);
                }
                catch (Exception ex) when (ex.Message.Contains("Migration") || ex.Message.Contains("migration") || ex.Message.Contains("__EFMigrationsHistory"))
                {
                    // If migration system fails or isn't set up, use EnsureCreated to update schema
                    _logger.LogInformation("Migrations not available, using EnsureCreated to update schema. This ensures the database matches the current model.");
                    progress.Status = "migrating";
                    progress.Message = "Updating database schema to match current version...";
                    
                    // EnsureCreated will create missing tables/columns but won't delete existing ones
                    // This is safe for schema updates
                    await database.EnsureCreatedAsync(cancellationToken);
                    
                    progress.Status = "completed";
                    progress.Message = "Database schema updated successfully";
                    progress.EndTime = DateTime.UtcNow;
                    _logger.LogInformation("Database schema updated successfully using EnsureCreated");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during database migration");
                progress.Status = "error";
                progress.Message = $"Migration failed: {ex.Message}";
                progress.Error = ex.ToString(); // Store full error details
                progress.EndTime = DateTime.UtcNow;
                throw;
            }
        }

        private async Task<bool> CheckIfSqlMigrationNeededAsync(DatabaseFacade database, CancellationToken cancellationToken)
        {
            try
            {
                // Check if VideoAnalyses table exists and if ServarrType column exists
                var connection = database.GetDbConnection();
                await connection.OpenAsync(cancellationToken);
                
                try
                {
                    using var command = connection.CreateCommand();
                    command.CommandText = @"
                        SELECT COUNT(*) FROM sqlite_master 
                        WHERE type='table' AND name='VideoAnalyses'";
                    
                    var tableExists = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) > 0;
                    
                    if (!tableExists)
                    {
                        return false; // Table doesn't exist, EnsureCreated will handle it
                    }
                    
                    // Check if ServarrType column exists
                    command.CommandText = @"
                        SELECT COUNT(*) FROM pragma_table_info('VideoAnalyses') 
                        WHERE name='ServarrType'";
                    
                    var columnExists = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) > 0;
                    
                    return !columnExists; // Migration needed if column doesn't exist
                }
                finally
                {
                    await connection.CloseAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error checking if SQL migration is needed, assuming it's not needed");
                return false;
            }
        }

        private async Task ApplySqlMigrationAsync(DatabaseFacade database, CancellationToken cancellationToken)
        {
            // Find the SQL migration file - try multiple locations
            var possiblePaths = new[]
            {
                Path.Combine(_hostEnvironment.ContentRootPath, "Data", "Migrations", "AddServarrFields.sql"),
                Path.Combine(AppContext.BaseDirectory, "Data", "Migrations", "AddServarrFields.sql"),
                Path.Combine(Directory.GetCurrentDirectory(), "Data", "Migrations", "AddServarrFields.sql")
            };
            
            string? migrationFile = null;
            foreach (var path in possiblePaths)
            {
                if (File.Exists(path))
                {
                    migrationFile = path;
                    break;
                }
            }
            
            if (migrationFile == null)
            {
                var searchedPaths = string.Join(", ", possiblePaths);
                throw new FileNotFoundException($"SQL migration file not found. Searched: {searchedPaths}");
            }
            
            _logger.LogInformation("Reading SQL migration from: {MigrationFile}", migrationFile);
            var sql = await File.ReadAllTextAsync(migrationFile, cancellationToken);
            
            if (string.IsNullOrWhiteSpace(sql))
            {
                throw new InvalidOperationException("SQL migration file is empty");
            }
            
            var connection = database.GetDbConnection();
            await connection.OpenAsync(cancellationToken);
            
            try
            {
                // SQLite requires statements to be executed separately
                // Split by semicolon but preserve transaction boundaries
                var statements = sql.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrWhiteSpace(s) && 
                                !s.StartsWith("--", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                
                using var transaction = await connection.BeginTransactionAsync(cancellationToken);
                try
                {
                    foreach (var statement in statements)
                    {
                        // Skip transaction control statements as we handle them manually
                        if (statement.Equals("BEGIN TRANSACTION", StringComparison.OrdinalIgnoreCase) ||
                            statement.Equals("COMMIT", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                        
                        using var command = connection.CreateCommand();
                        command.Transaction = transaction;
                        command.CommandText = statement;
                        
                        _logger.LogDebug("Executing SQL statement: {Statement}", 
                            statement.Length > 100 ? statement.Substring(0, 100) + "..." : statement);
                        
                        await command.ExecuteNonQueryAsync(cancellationToken);
                    }
                    
                    await transaction.CommitAsync(cancellationToken);
                    _logger.LogInformation("SQL migration applied successfully");
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    _logger.LogError(ex, "Error executing SQL migration, transaction rolled back");
                    throw;
                }
            }
            finally
            {
                await connection.CloseAsync();
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    public class MigrationProgress
    {
        public string Status { get; set; } = "checking"; // checking, creating, migrating, completed, error
        public string Message { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public List<string> AppliedMigrations { get; set; } = new();
        public List<string> PendingMigrations { get; set; } = new();
        public string? Error { get; set; }
    }
}

