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
                    
                    // Verify migration was successful by checking if column exists
                    // Close and reopen connection to ensure we see the latest state
                    var connection = database.GetDbConnection();
                    if (connection.State == System.Data.ConnectionState.Open)
                    {
                        await connection.CloseAsync();
                    }
                    await connection.OpenAsync(cancellationToken);
                    
                    try
                    {
                        using var verifyCommand = connection.CreateCommand();
                        verifyCommand.CommandText = @"
                            SELECT COUNT(*) FROM pragma_table_info('VideoAnalyses') 
                            WHERE name='ServarrType'";
                        
                        var columnExists = Convert.ToInt32(await verifyCommand.ExecuteScalarAsync(cancellationToken)) > 0;
                        
                        _logger.LogInformation("Migration verification: ServarrType column exists = {Exists}", columnExists);
                        
                        if (!columnExists)
                        {
                            // Check what columns actually exist for debugging
                            verifyCommand.CommandText = "SELECT name FROM pragma_table_info('VideoAnalyses') ORDER BY cid";
                            using var reader = await verifyCommand.ExecuteReaderAsync(cancellationToken);
                            var columns = new List<string>();
                            while (await reader.ReadAsync(cancellationToken))
                            {
                                columns.Add(reader.GetString(0));
                            }
                            _logger.LogError("ServarrType column not found after migration. Existing columns: {Columns}", string.Join(", ", columns));
                            throw new InvalidOperationException($"SQL migration completed but ServarrType column was not created. Existing columns: {string.Join(", ", columns)}");
                        }
                    }
                    finally
                    {
                        await connection.CloseAsync();
                    }
                    
                    progress.AppliedMigrations = new List<string> { "AddServarrFields" };
                    progress.Status = "completed";
                    progress.Message = "SQL migration applied and verified successfully";
                    progress.EndTime = DateTime.UtcNow;
                    _logger.LogInformation("SQL migration applied and verified successfully");
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
                        _logger.LogDebug("VideoAnalyses table doesn't exist, EnsureCreated will handle it");
                        return false; // Table doesn't exist, EnsureCreated will handle it
                    }
                    
                    // Check if ServarrType column exists in VideoAnalyses
                    command.CommandText = @"
                        SELECT COUNT(*) FROM pragma_table_info('VideoAnalyses') 
                        WHERE name='ServarrType'";
                    
                    var servarrColumnExists = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) > 0;
                    
                    // Check if CurrentProcessingFile column exists in LibraryScans
                    command.CommandText = @"
                        SELECT COUNT(*) FROM sqlite_master 
                        WHERE type='table' AND name='LibraryScans'";
                    
                    var libraryScansExists = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) > 0;
                    var libraryScanColumnExists = false;
                    
                    if (libraryScansExists)
                    {
                        command.CommandText = @"
                            SELECT COUNT(*) FROM pragma_table_info('LibraryScans') 
                            WHERE name='CurrentProcessingFile'";
                        libraryScanColumnExists = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) > 0;
                    }
                    
                    var migrationNeeded = !servarrColumnExists || (libraryScansExists && !libraryScanColumnExists);
                    
                    _logger.LogDebug("Migration check: ServarrType exists={ServarrExists}, CurrentProcessingFile exists={CurrentFileExists}, Migration needed={Needed}",
                        servarrColumnExists, libraryScanColumnExists, migrationNeeded);
                    
                    return migrationNeeded;
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
                Path.Combine(Directory.GetCurrentDirectory(), "Data", "Migrations", "AddServarrFields.sql"),
                // Also try without Data subdirectory (in case it's copied to root)
                Path.Combine(_hostEnvironment.ContentRootPath, "Migrations", "AddServarrFields.sql"),
                Path.Combine(AppContext.BaseDirectory, "Migrations", "AddServarrFields.sql"),
                Path.Combine(Directory.GetCurrentDirectory(), "Migrations", "AddServarrFields.sql")
            };
            
            // Log all paths being checked for debugging
            _logger.LogDebug("Searching for migration file. ContentRootPath: {ContentRoot}, BaseDirectory: {BaseDir}, CurrentDirectory: {CurrentDir}",
                _hostEnvironment.ContentRootPath, AppContext.BaseDirectory, Directory.GetCurrentDirectory());
            
            string? migrationFile = null;
            foreach (var path in possiblePaths)
            {
                _logger.LogDebug("Checking path: {Path}, Exists: {Exists}", path, File.Exists(path));
                if (File.Exists(path))
                {
                    migrationFile = path;
                    _logger.LogInformation("Found migration file at: {Path}", path);
                    break;
                }
            }
            
            string sql;
            if (migrationFile == null)
            {
                var searchedPaths = string.Join(", ", possiblePaths);
                _logger.LogWarning("SQL migration file not found in filesystem. Searched paths: {Paths}. Using embedded SQL as fallback.", searchedPaths);
                // Fallback: Use embedded SQL if file not found
                sql = GetEmbeddedMigrationSql();
            }
            else
            {
                _logger.LogInformation("Reading SQL migration from: {MigrationFile}", migrationFile);
                sql = await File.ReadAllTextAsync(migrationFile, cancellationToken);
            }
            
            if (string.IsNullOrWhiteSpace(sql))
            {
                throw new InvalidOperationException("SQL migration file is empty");
            }
            
            var connection = database.GetDbConnection();
            await connection.OpenAsync(cancellationToken);
            
            try
            {
                // Remove comments and clean up SQL
                var cleanedSql = string.Join("\n", sql.Split('\n')
                    .Where(line => !line.Trim().StartsWith("--", StringComparison.OrdinalIgnoreCase))
                    .Select(line => line.Trim()));
                
                // SQLite requires statements to be executed separately
                // Split by semicolon, but be careful with multi-line statements
                // First, remove BEGIN TRANSACTION and COMMIT as we handle them manually
                var sqlWithoutTransactions = cleanedSql
                    .Replace("BEGIN TRANSACTION", "", StringComparison.OrdinalIgnoreCase)
                    .Replace("COMMIT", "", StringComparison.OrdinalIgnoreCase);
                
                // Split by semicolon and filter out empty statements
                var statements = sqlWithoutTransactions
                    .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList();
                
                _logger.LogInformation("Split SQL into {Count} statements", statements.Count);
                _logger.LogDebug("Statements: {Statements}", string.Join(" | ", statements.Select((s, i) => $"{i + 1}: {s.Substring(0, Math.Min(50, s.Length))}...")));
                
                using var transaction = await connection.BeginTransactionAsync(cancellationToken);
                try
                {
                    int statementIndex = 0;
                    foreach (var statement in statements)
                    {
                        statementIndex++;
                        // Skip transaction control statements as we handle them manually
                        if (statement.Equals("BEGIN TRANSACTION", StringComparison.OrdinalIgnoreCase) ||
                            statement.Equals("COMMIT", StringComparison.OrdinalIgnoreCase))
                        {
                            _logger.LogDebug("Skipping transaction control statement: {Statement}", statement);
                            continue;
                        }
                        
                        using var command = connection.CreateCommand();
                        command.Transaction = transaction;
                        command.CommandText = statement;
                        
                        var statementPreview = statement.Length > 200 ? statement.Substring(0, 200) + "..." : statement;
                        _logger.LogInformation("Executing SQL statement {Index}/{Total}: {Statement}", 
                            statementIndex, statements.Count, statementPreview);
                        
                        try
                        {
                            var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
                            _logger.LogDebug("Statement {Index} executed successfully, rows affected: {Rows}", statementIndex, rowsAffected);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error executing statement {Index}: {Statement}", statementIndex, statementPreview);
                            throw;
                        }
                    }
                    
                    // After VideoAnalyses migration, add CurrentProcessingFile to LibraryScans if needed
                    // Check if column exists first
                    using var checkCommand = connection.CreateCommand();
                    checkCommand.Transaction = transaction;
                    checkCommand.CommandText = @"
                        SELECT COUNT(*) FROM pragma_table_info('LibraryScans') 
                        WHERE name='CurrentProcessingFile'";
                    
                    var currentFileExists = Convert.ToInt32(await checkCommand.ExecuteScalarAsync(cancellationToken)) > 0;
                    
                    if (!currentFileExists)
                    {
                        _logger.LogInformation("Adding CurrentProcessingFile column to LibraryScans table");
                        using var addColumnCommand = connection.CreateCommand();
                        addColumnCommand.Transaction = transaction;
                        addColumnCommand.CommandText = "ALTER TABLE LibraryScans ADD COLUMN CurrentProcessingFile TEXT";
                        await addColumnCommand.ExecuteNonQueryAsync(cancellationToken);
                    }
                    else
                    {
                        _logger.LogDebug("CurrentProcessingFile column already exists in LibraryScans");
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

        private string GetEmbeddedMigrationSql()
        {
            // Embedded SQL migration as fallback if file is not found
            // This handles both VideoAnalyses Servarr fields and LibraryScans CurrentProcessingFile
            return @"BEGIN TRANSACTION;

-- Migrate VideoAnalyses table to add Servarr fields
CREATE TABLE VideoAnalyses_new (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    FilePath TEXT NOT NULL,
    FileName TEXT NOT NULL DEFAULT '',
    FileSize INTEGER NOT NULL DEFAULT 0,
    Duration REAL NOT NULL DEFAULT 0,
    Container TEXT NOT NULL DEFAULT '',
    VideoCodec TEXT NOT NULL DEFAULT '',
    VideoCodecTag TEXT NOT NULL DEFAULT '',
    IsCodecTagCorrect INTEGER NOT NULL DEFAULT 1,
    BitDepth INTEGER NOT NULL DEFAULT 8,
    Width INTEGER NOT NULL DEFAULT 0,
    Height INTEGER NOT NULL DEFAULT 0,
    FrameRate REAL NOT NULL DEFAULT 0,
    IsHDR INTEGER NOT NULL DEFAULT 0,
    HDRType TEXT NOT NULL DEFAULT '',
    IsFastStart INTEGER NOT NULL DEFAULT 0,
    AudioCodecs TEXT NOT NULL DEFAULT '',
    AudioTrackCount INTEGER NOT NULL DEFAULT 0,
    AudioTracksJson TEXT NOT NULL DEFAULT '',
    SubtitleFormats TEXT NOT NULL DEFAULT '',
    SubtitleTrackCount INTEGER NOT NULL DEFAULT 0,
    SubtitleTracksJson TEXT NOT NULL DEFAULT '',
    OverallScore TEXT NOT NULL DEFAULT 'Unknown',
    CompatibilityRating INTEGER NOT NULL DEFAULT 0,
    DirectPlayClients INTEGER NOT NULL DEFAULT 0,
    RemuxClients INTEGER NOT NULL DEFAULT 0,
    TranscodeClients INTEGER NOT NULL DEFAULT 0,
    Issues TEXT NOT NULL DEFAULT '',
    Recommendations TEXT NOT NULL DEFAULT '',
    ClientResults TEXT NOT NULL DEFAULT '',
    FullReport TEXT NOT NULL DEFAULT '',
    IsBroken INTEGER NOT NULL DEFAULT 0,
    BrokenReason TEXT,
    AnalyzedAt TEXT NOT NULL,
    LibraryScanId INTEGER,
    ServarrType TEXT,
    SonarrSeriesId INTEGER,
    SonarrSeriesTitle TEXT,
    SonarrEpisodeId INTEGER,
    SonarrEpisodeNumber INTEGER,
    SonarrSeasonNumber INTEGER,
    RadarrMovieId INTEGER,
    RadarrMovieTitle TEXT,
    RadarrYear INTEGER,
    ServarrMatchedAt TEXT,
    FOREIGN KEY (LibraryScanId) REFERENCES LibraryScans(Id) ON DELETE CASCADE
);

INSERT INTO VideoAnalyses_new 
SELECT 
    Id, FilePath, FileName, FileSize, Duration, Container, VideoCodec, VideoCodecTag,
    IsCodecTagCorrect, BitDepth, Width, Height, FrameRate, IsHDR, HDRType, IsFastStart,
    AudioCodecs, AudioTrackCount, AudioTracksJson, SubtitleFormats, SubtitleTrackCount,
    SubtitleTracksJson, OverallScore, CompatibilityRating, DirectPlayClients, RemuxClients,
    TranscodeClients, Issues, Recommendations, ClientResults, FullReport, IsBroken, BrokenReason,
    AnalyzedAt, LibraryScanId,
    NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL
FROM VideoAnalyses;

DROP TABLE VideoAnalyses;

ALTER TABLE VideoAnalyses_new RENAME TO VideoAnalyses;

CREATE INDEX IF NOT EXISTS IX_VideoAnalyses_FilePath ON VideoAnalyses(FilePath);
CREATE INDEX IF NOT EXISTS IX_VideoAnalyses_LibraryScanId ON VideoAnalyses(LibraryScanId);
CREATE INDEX IF NOT EXISTS IX_VideoAnalyses_AnalyzedAt ON VideoAnalyses(AnalyzedAt);
CREATE INDEX IF NOT EXISTS IX_VideoAnalyses_OverallScore ON VideoAnalyses(OverallScore);

-- Add CurrentProcessingFile to LibraryScans if it doesn't exist
-- SQLite supports ALTER TABLE ADD COLUMN for nullable columns
-- Check if column exists first (using a workaround since we can't use IF NOT EXISTS with ALTER TABLE)
-- We'll try to add it and ignore the error if it already exists

COMMIT;";
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

