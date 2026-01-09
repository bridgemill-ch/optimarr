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
                var needsServarrMigration = await CheckIfServarrMigrationNeededAsync(database, cancellationToken);
                if (needsServarrMigration)
                {
                    progress.Status = "migrating";
                    progress.Message = "Applying SQL migration for Servarr fields...";
                    progress.PendingMigrations = new List<string> { "AddServarrFields" };
                    _logger.LogInformation("SQL migration needed for Servarr fields, applying...");
                    
                    await ApplyServarrMigrationAsync(database, cancellationToken);
                    
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
                            WHERE name='ProcessingStatus'";
                        
                        var columnExists = Convert.ToInt32(await verifyCommand.ExecuteScalarAsync(cancellationToken)) > 0;
                        
                        _logger.LogInformation("Migration verification: ProcessingStatus column exists = {Exists}", columnExists);
                        
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
                            _logger.LogError("ProcessingStatus column not found after migration. Existing columns: {Columns}", string.Join(", ", columns));
                            throw new InvalidOperationException($"SQL migration completed but ProcessingStatus column was not created. Existing columns: {string.Join(", ", columns)}");
                        }
                    }
                    finally
                    {
                        await connection.CloseAsync();
                    }
                    
                    progress.AppliedMigrations = new List<string> { "AddProcessingStatusFields" };
                    progress.Status = "completed";
                    progress.Message = "ProcessingStatus migration applied and verified successfully";
                    progress.EndTime = DateTime.UtcNow;
                    _logger.LogInformation("ProcessingStatus migration applied and verified successfully");
                    return;
                }

                // Check if ProcessingStatus migration needs to be applied
                var needsProcessingMigration = await CheckIfProcessingStatusMigrationNeededAsync(database, cancellationToken);
                if (needsProcessingMigration)
                {
                    progress.Status = "migrating";
                    progress.Message = "Applying SQL migration for ProcessingStatus fields...";
                    progress.PendingMigrations = new List<string> { "AddProcessingStatusFields" };
                    _logger.LogInformation("SQL migration needed for ProcessingStatus fields, applying...");
                    
                    await ApplyProcessingStatusMigrationAsync(database, cancellationToken);
                    
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
                            WHERE name='ProcessingStatus'";
                        
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

        private async Task<bool> CheckIfServarrMigrationNeededAsync(DatabaseFacade database, CancellationToken cancellationToken)
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
                    
                    // Check if Servarr-related columns exist in LibraryPaths
                    command.CommandText = @"
                        SELECT COUNT(*) FROM sqlite_master 
                        WHERE type='table' AND name='LibraryPaths'";
                    
                    var libraryPathsExists = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) > 0;
                    var libraryPathServarrColumnsExist = true; // Default to true if table doesn't exist
                    
                    if (libraryPathsExists)
                    {
                        // Check for all Servarr-related columns
                        command.CommandText = @"
                            SELECT COUNT(*) FROM pragma_table_info('LibraryPaths') 
                            WHERE name IN ('ServarrType', 'ServarrRootFolderId', 'ServarrRootFolderPath', 'LastSyncedAt')";
                        
                        var servarrColumnCount = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
                        libraryPathServarrColumnsExist = servarrColumnCount == 4; // All 4 columns must exist
                    }
                    
                    var migrationNeeded = !servarrColumnExists || 
                                         (libraryScansExists && !libraryScanColumnExists) ||
                                         (libraryPathsExists && !libraryPathServarrColumnsExist);
                    
                    _logger.LogDebug("Migration check: ServarrType exists={ServarrExists}, CurrentProcessingFile exists={CurrentFileExists}, LibraryPaths Servarr columns exist={LibraryPathExists}, Migration needed={Needed}",
                        servarrColumnExists, libraryScanColumnExists, libraryPathServarrColumnsExist, migrationNeeded);
                    
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

        private async Task<bool> CheckIfProcessingStatusMigrationNeededAsync(DatabaseFacade database, CancellationToken cancellationToken)
        {
            try
            {
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
                        return false;
                    }
                    
                    // Check if ProcessingStatus column exists
                    command.CommandText = @"
                        SELECT COUNT(*) FROM pragma_table_info('VideoAnalyses') 
                        WHERE name='ProcessingStatus'";
                    
                    var processingStatusExists = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) > 0;
                    
                    // Check if ProcessingStartedAt column exists
                    command.CommandText = @"
                        SELECT COUNT(*) FROM pragma_table_info('VideoAnalyses') 
                        WHERE name='ProcessingStartedAt'";
                    
                    var processingStartedAtExists = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) > 0;
                    
                    var migrationNeeded = !processingStatusExists || !processingStartedAtExists;
                    
                    _logger.LogDebug("ProcessingStatus migration check: ProcessingStatus exists={StatusExists}, ProcessingStartedAt exists={StartedAtExists}, Migration needed={Needed}",
                        processingStatusExists, processingStartedAtExists, migrationNeeded);
                    
                    return migrationNeeded;
                }
                finally
                {
                    await connection.CloseAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error checking if ProcessingStatus migration is needed, assuming it's not needed");
                return false;
            }
        }

        private async Task ApplyProcessingStatusMigrationAsync(DatabaseFacade database, CancellationToken cancellationToken)
        {
            // Find the SQL migration file - try multiple locations
            var possiblePaths = new[]
            {
                Path.Combine(_hostEnvironment.ContentRootPath, "Data", "Migrations", "AddProcessingStatusFields.sql"),
                Path.Combine(AppContext.BaseDirectory, "Data", "Migrations", "AddProcessingStatusFields.sql"),
                Path.Combine(Directory.GetCurrentDirectory(), "Data", "Migrations", "AddProcessingStatusFields.sql"),
                // Also try without Data subdirectory (in case it's copied to root)
                Path.Combine(_hostEnvironment.ContentRootPath, "Migrations", "AddProcessingStatusFields.sql"),
                Path.Combine(AppContext.BaseDirectory, "Migrations", "AddProcessingStatusFields.sql"),
                Path.Combine(Directory.GetCurrentDirectory(), "Migrations", "AddProcessingStatusFields.sql")
            };
            
            // Log all paths being checked for debugging
            _logger.LogDebug("Searching for ProcessingStatus migration file. ContentRootPath: {ContentRoot}, BaseDirectory: {BaseDir}, CurrentDirectory: {CurrentDir}",
                _hostEnvironment.ContentRootPath, AppContext.BaseDirectory, Directory.GetCurrentDirectory());
            
            string? migrationFile = null;
            foreach (var path in possiblePaths)
            {
                _logger.LogDebug("Checking path: {Path}, Exists: {Exists}", path, File.Exists(path));
                if (File.Exists(path))
                {
                    migrationFile = path;
                    _logger.LogInformation("Found ProcessingStatus migration file at: {Path}", path);
                    break;
                }
            }
            
            string sql;
            if (migrationFile == null)
            {
                var searchedPaths = string.Join(", ", possiblePaths);
                _logger.LogWarning("ProcessingStatus SQL migration file not found in filesystem. Searched paths: {Paths}. Using embedded SQL as fallback.", searchedPaths);
                // Fallback: Use embedded SQL if file not found
                sql = GetEmbeddedProcessingStatusMigrationSql();
            }
            else
            {
                _logger.LogInformation("Reading ProcessingStatus SQL migration from: {MigrationFile}", migrationFile);
                sql = await File.ReadAllTextAsync(migrationFile, cancellationToken);
            }
            
            if (string.IsNullOrWhiteSpace(sql))
            {
                throw new InvalidOperationException("ProcessingStatus SQL migration file is empty");
            }
            
            await ExecuteSqlMigrationAsync(database, sql, cancellationToken);
        }

        private async Task ApplyServarrMigrationAsync(DatabaseFacade database, CancellationToken cancellationToken)
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
            
            await ExecuteSqlMigrationAsync(database, sql, cancellationToken);
            
            // After VideoAnalyses migration, add CurrentProcessingFile to LibraryScans if needed
            var connection = database.GetDbConnection();
            await connection.OpenAsync(cancellationToken);
            
            try
            {
                using var transaction = await connection.BeginTransactionAsync(cancellationToken);
                try
                {
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
                    
                    // Add Servarr-related columns to LibraryPaths if needed
                    var libraryPathColumns = new[]
                    {
                        ("ServarrType", "TEXT"),
                        ("ServarrRootFolderId", "INTEGER"),
                        ("ServarrRootFolderPath", "TEXT"),
                        ("LastSyncedAt", "TEXT")
                    };
                    
                    foreach (var (columnName, columnType) in libraryPathColumns)
                    {
                        checkCommand.CommandText = $@"
                            SELECT COUNT(*) FROM pragma_table_info('LibraryPaths') 
                            WHERE name='{columnName}'";
                        
                        var columnExists = Convert.ToInt32(await checkCommand.ExecuteScalarAsync(cancellationToken)) > 0;
                        
                        if (!columnExists)
                        {
                            _logger.LogInformation("Adding {Column} column to LibraryPaths table", columnName);
                            using var addColumnCommand = connection.CreateCommand();
                            addColumnCommand.Transaction = transaction;
                            addColumnCommand.CommandText = $"ALTER TABLE LibraryPaths ADD COLUMN {columnName} {columnType}";
                            await addColumnCommand.ExecuteNonQueryAsync(cancellationToken);
                        }
                        else
                        {
                            _logger.LogDebug("{Column} column already exists in LibraryPaths", columnName);
                        }
                    }
                    
                    await transaction.CommitAsync(cancellationToken);
                    _logger.LogInformation("Servarr SQL migration applied successfully");
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    _logger.LogError(ex, "Error executing Servarr SQL migration, transaction rolled back");
                    throw;
                }
            }
            finally
            {
                await connection.CloseAsync();
            }
        }

        private async Task ExecuteSqlMigrationAsync(DatabaseFacade database, string sql, CancellationToken cancellationToken)
        {
            var connection = database.GetDbConnection();
            await connection.OpenAsync(cancellationToken);
            
            try
            {
                // Remove full-line comments (lines that are ONLY comments)
                // Remove inline comments from lines (everything after -- on a line)
                var lines = sql.Split('\n')
                    .Select(line => 
                    {
                        var trimmed = line.Trim();
                        // Remove lines that are entirely comments
                        if (trimmed.StartsWith("--", StringComparison.OrdinalIgnoreCase))
                        {
                            return string.Empty; // Remove comment-only lines
                        }
                        
                        // Remove inline comments (everything after --)
                        var commentIndex = line.IndexOf("--", StringComparison.OrdinalIgnoreCase);
                        if (commentIndex >= 0)
                        {
                            // Check if it's a real comment (not part of a string)
                            // Simple heuristic: if -- appears and is not in quotes, it's a comment
                            var beforeComment = line.Substring(0, commentIndex);
                            var singleQuoteCount = beforeComment.Count(c => c == '\'');
                            // If even number of quotes before --, then -- is not in a string
                            if (singleQuoteCount % 2 == 0)
                            {
                                return beforeComment.TrimEnd(); // Remove comment part
                            }
                        }
                        
                        return line; // Keep the line as-is
                    })
                    .Where(line => !string.IsNullOrWhiteSpace(line))
                    .ToList();
                
                // Join lines back together with newlines preserved, then normalize
                var cleanedSql = string.Join("\n", lines);
                
                // Remove BEGIN TRANSACTION and COMMIT as we handle them manually
                var sqlWithoutTransactions = cleanedSql
                    .Replace("BEGIN TRANSACTION", "", StringComparison.OrdinalIgnoreCase)
                    .Replace("COMMIT", "", StringComparison.OrdinalIgnoreCase);
                
                // Normalize whitespace: replace multiple spaces/newlines with single space, but preserve statement structure
                sqlWithoutTransactions = System.Text.RegularExpressions.Regex.Replace(
                    sqlWithoutTransactions, 
                    @"\s+", 
                    " ", 
                    System.Text.RegularExpressions.RegexOptions.Multiline);
                
                // Split by semicolon - SQLite statements end with semicolon
                // But be careful: semicolons can appear in strings, so we need to handle that
                var rawStatements = new List<string>();
                var currentStatement = new System.Text.StringBuilder();
                var inString = false;
                var stringChar = '\0';
                
                // Use for loop to allow peeking ahead for SQLite's '' escaped quotes
                for (int i = 0; i < sqlWithoutTransactions.Length; i++)
                {
                    var ch = sqlWithoutTransactions[i];
                    
                    if (!inString && (ch == '\'' || ch == '"'))
                    {
                        inString = true;
                        stringChar = ch;
                        currentStatement.Append(ch);
                    }
                    else if (inString && ch == stringChar)
                    {
                        // Check if it's escaped (SQLite uses '' for escaped single quotes)
                        if (stringChar == '\'' && i + 1 < sqlWithoutTransactions.Length && sqlWithoutTransactions[i + 1] == '\'')
                        {
                            // Escaped quote: append both quotes and skip the next one
                            currentStatement.Append(ch);
                            currentStatement.Append(ch);
                            i++; // Skip the next quote since we've already handled it
                        }
                        else
                        {
                            // Not escaped, end of string
                            inString = false;
                            stringChar = '\0';
                            currentStatement.Append(ch);
                        }
                    }
                    else if (!inString && ch == ';')
                    {
                        var statement = currentStatement.ToString().Trim();
                        if (!string.IsNullOrWhiteSpace(statement) && 
                            !statement.StartsWith("--", StringComparison.OrdinalIgnoreCase))
                        {
                            rawStatements.Add(statement);
                        }
                        currentStatement.Clear();
                    }
                    else
                    {
                        currentStatement.Append(ch);
                    }
                }
                
                // Add any remaining statement
                var finalStatement = currentStatement.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(finalStatement) && 
                    !finalStatement.StartsWith("--", StringComparison.OrdinalIgnoreCase))
                {
                    rawStatements.Add(finalStatement);
                }
                
                _logger.LogInformation("Split SQL into {Count} statements", rawStatements.Count);
                
                // Log preview of each statement for debugging
                for (int i = 0; i < rawStatements.Count; i++)
                {
                    var preview = rawStatements[i].Length > 200 
                        ? rawStatements[i].Substring(0, 200) + "..." 
                        : rawStatements[i];
                    _logger.LogInformation("Statement {Index}/{Total}: {Preview}", i + 1, rawStatements.Count, preview);
                }
                
                var statements = rawStatements;
                
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
                    
                    await transaction.CommitAsync(cancellationToken);
                    _logger.LogInformation("SQL migration executed successfully");
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

        private string GetEmbeddedProcessingStatusMigrationSql()
        {
            // Embedded SQL migration as fallback if file is not found
            // This handles ProcessingStatus and ProcessingStartedAt fields
            return @"BEGIN TRANSACTION;

-- Create new table with ProcessingStatus fields
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
    ProcessingStatus TEXT NOT NULL DEFAULT 'None',
    ProcessingStartedAt TEXT,
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
    'None', NULL,
    AnalyzedAt, LibraryScanId,
    ServarrType, SonarrSeriesId, SonarrSeriesTitle, SonarrEpisodeId, SonarrEpisodeNumber,
    SonarrSeasonNumber, RadarrMovieId, RadarrMovieTitle, RadarrYear, ServarrMatchedAt
FROM VideoAnalyses;

DROP TABLE VideoAnalyses;

ALTER TABLE VideoAnalyses_new RENAME TO VideoAnalyses;

CREATE INDEX IF NOT EXISTS IX_VideoAnalyses_FilePath ON VideoAnalyses(FilePath);
CREATE INDEX IF NOT EXISTS IX_VideoAnalyses_LibraryScanId ON VideoAnalyses(LibraryScanId);
CREATE INDEX IF NOT EXISTS IX_VideoAnalyses_AnalyzedAt ON VideoAnalyses(AnalyzedAt);
CREATE INDEX IF NOT EXISTS IX_VideoAnalyses_OverallScore ON VideoAnalyses(OverallScore);
CREATE INDEX IF NOT EXISTS IX_VideoAnalyses_ProcessingStatus ON VideoAnalyses(ProcessingStatus);
CREATE INDEX IF NOT EXISTS IX_VideoAnalyses_ProcessingStartedAt ON VideoAnalyses(ProcessingStartedAt);

COMMIT;";
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

