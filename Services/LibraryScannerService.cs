using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Optimarr.Data;
using Optimarr.Models;

namespace Optimarr.Services
{
    public class LibraryScannerService
    {
        private readonly AppDbContext _dbContext;
        private readonly VideoAnalyzerService _videoAnalyzer;
        private readonly ILogger<LibraryScannerService> _logger;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly ConcurrentDictionary<int, CancellationTokenSource> _scanCancellationTokens = new();
        private DateTime _scanStartTime;

        private static readonly string[] VideoExtensions = { ".mp4", ".mkv", ".avi", ".ts", ".m2ts", ".webm", ".ogg", ".mov", ".m4v" };
        private static readonly string[] SubtitleExtensions = { ".srt", ".vtt", ".ass", ".ssa", ".sub", ".idx", ".sup" };

        public LibraryScannerService(
            AppDbContext dbContext,
            VideoAnalyzerService videoAnalyzer,
            ILogger<LibraryScannerService> logger,
            IServiceScopeFactory serviceScopeFactory)
        {
            _dbContext = dbContext;
            _videoAnalyzer = videoAnalyzer;
            _logger = logger;
            _serviceScopeFactory = serviceScopeFactory;
        }

        public async Task<LibraryScan> StartScanAsync(string libraryPath, string? libraryName = null, string? category = "Misc")
        {
            _logger.LogInformation("Starting library scan for path: {Path}", libraryPath);

            if (!Directory.Exists(libraryPath))
            {
                _logger.LogError("Library path does not exist: {Path}", libraryPath);
                throw new DirectoryNotFoundException($"Library path does not exist: {libraryPath}");
            }

            // Create or update LibraryPath record
            _logger.LogInformation("Checking for existing LibraryPath: {Path}", libraryPath);
            var libraryPathRecord = await _dbContext.LibraryPaths
                .FirstOrDefaultAsync(lp => lp.Path == libraryPath);
            
            if (libraryPathRecord == null)
            {
                _logger.LogInformation("Creating new LibraryPath record");
                libraryPathRecord = new LibraryPath
                {
                    Path = libraryPath,
                    Name = libraryName ?? Path.GetFileName(libraryPath) ?? libraryPath,
                    Category = category ?? "Misc",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };
                _dbContext.LibraryPaths.Add(libraryPathRecord);
            }
            else
            {
                _logger.LogInformation("Updating existing LibraryPath record (ID: {Id})", libraryPathRecord.Id);
                if (!string.IsNullOrEmpty(libraryName))
                {
                    libraryPathRecord.Name = libraryName;
                }
                if (!string.IsNullOrEmpty(category))
                {
                    libraryPathRecord.Category = category;
                }
            }

            _logger.LogInformation("Creating LibraryScan record");
            var scan = new LibraryScan
            {
                LibraryPath = libraryPath,
                StartedAt = DateTime.UtcNow,
                Status = ScanStatus.Pending
            };

            _logger.LogInformation("Adding scan to database context");
            _dbContext.LibraryScans.Add(scan);
            
            _logger.LogInformation("Saving scan to database");
            await _dbContext.SaveChangesAsync();
            _logger.LogInformation("Scan saved to database with ID: {ScanId}", scan.Id);

            // Create cancellation token source for this scan immediately so it can be cancelled at any time
            var cts = new CancellationTokenSource();
            _scanCancellationTokens.TryAdd(scan.Id, cts);
            _logger.LogInformation("Cancellation token created for scan ID: {ScanId}", scan.Id);

            // Start scan in background with proper error handling and DbContext scope
            _logger.LogInformation("Starting background scan task for scan ID: {ScanId}", scan.Id);
            var scanIdForTask = scan.Id; // Capture for closure
            _ = Task.Run(async () =>
            {
                // Immediate console output to verify task is running
                Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] BACKGROUND TASK STARTED for scan {scanIdForTask}");
                
                AppDbContext? scopedDbContext = null;
                try
                {
                    _logger.LogCritical(">>> BACKGROUND TASK: Task.Run callback EXECUTED for scan ID: {ScanId}", scanIdForTask);
                    
                    // Create a new scope for the background task to get a fresh DbContext
                    _logger.LogInformation(">>> BACKGROUND TASK: Creating service scope for scan ID: {ScanId}", scanIdForTask);
                    if (_serviceScopeFactory == null)
                    {
                        _logger.LogError(">>> BACKGROUND TASK: _serviceScopeFactory is NULL for scan ID: {ScanId}!", scanIdForTask);
                        throw new InvalidOperationException("ServiceScopeFactory is null");
                    }
                    
                    using var scope = _serviceScopeFactory.CreateScope();
                    _logger.LogInformation(">>> BACKGROUND TASK: Service scope created for scan ID: {ScanId}", scanIdForTask);
                    
                    _logger.LogInformation(">>> BACKGROUND TASK: Getting AppDbContext from scope for scan ID: {ScanId}", scanIdForTask);
                    scopedDbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    _logger.LogInformation(">>> BACKGROUND TASK: AppDbContext obtained for scan ID: {ScanId}", scanIdForTask);
                    
                    _logger.LogInformation(">>> BACKGROUND TASK: Getting VideoAnalyzerService from scope for scan ID: {ScanId}", scanIdForTask);
                    var scopedVideoAnalyzer = scope.ServiceProvider.GetRequiredService<VideoAnalyzerService>();
                    _logger.LogInformation(">>> BACKGROUND TASK: VideoAnalyzerService obtained for scan ID: {ScanId}", scanIdForTask);
                    
                    _logger.LogInformation(">>> BACKGROUND TASK: All services obtained, calling ExecuteScanAsync for scan ID: {ScanId}", scanIdForTask);
                    await ExecuteScanAsync(scanIdForTask, scopedDbContext, scopedVideoAnalyzer);
                    _logger.LogInformation(">>> BACKGROUND TASK: ExecuteScanAsync completed for scan ID: {ScanId}", scanIdForTask);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] BACKGROUND TASK ERROR for scan {scanIdForTask}: {ex.GetType().Name} - {ex.Message}");
                    _logger.LogError(ex, ">>> BACKGROUND TASK: FATAL ERROR in background scan task for scan ID: {ScanId}", scanIdForTask);
                    _logger.LogError(">>> BACKGROUND TASK: Exception type: {Type}, Message: {Message}", ex.GetType().Name, ex.Message);
                    _logger.LogError(">>> BACKGROUND TASK: Stack trace: {StackTrace}", ex.StackTrace);
                    
                    // Try to update scan status to failed using a new scope
                    try
                    {
                        using var errorScope = _serviceScopeFactory?.CreateScope();
                        if (errorScope != null)
                        {
                            var errorDbContext = errorScope.ServiceProvider.GetRequiredService<AppDbContext>();
                            var failedScan = await errorDbContext.LibraryScans.FindAsync(scanIdForTask);
                            if (failedScan != null)
                            {
                                failedScan.Status = ScanStatus.Failed;
                                failedScan.ErrorMessage = $"Background task error: {ex.Message}";
                                failedScan.CompletedAt = DateTime.UtcNow;
                                await errorDbContext.SaveChangesAsync();
                                _logger.LogInformation(">>> BACKGROUND TASK: Scan status updated to Failed for scan ID: {ScanId}", scanIdForTask);
                            }
                        }
                    }
                    catch (Exception dbEx)
                    {
                        _logger.LogError(dbEx, ">>> BACKGROUND TASK: Failed to update scan status after error for scan ID: {ScanId}", scanIdForTask);
                    }
                }
            }, CancellationToken.None);

            _logger.LogInformation("Background scan task initiated, returning scan record");
            return scan;
        }

        public async Task CancelScan(int scanId)
        {
            _logger.LogInformation("Cancelling scan: {ScanId}", scanId);
            
            // Cancel the token for this specific scan if it exists
            if (_scanCancellationTokens.TryGetValue(scanId, out var cts))
            {
                cts.Cancel();
                _logger.LogInformation("Cancellation token triggered for scan: {ScanId}", scanId);
            }
            else
            {
                _logger.LogWarning("No cancellation token found for scan: {ScanId} - scan may not have started yet or already completed", scanId);
            }
            
            // Always try to update scan status to Cancelled in database (even if token doesn't exist)
            // This handles cases where the scan hasn't started yet or the token was already cleaned up
            try
            {
                using var scope = _serviceScopeFactory?.CreateScope();
                if (scope != null)
                {
                    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var scan = await dbContext.LibraryScans.FindAsync(scanId);
                    if (scan != null && (scan.Status == ScanStatus.Running || scan.Status == ScanStatus.Pending))
                    {
                        scan.Status = ScanStatus.Cancelled;
                        scan.CompletedAt = DateTime.UtcNow;
                        scan.ErrorMessage = "Scan was cancelled by user";
                        await dbContext.SaveChangesAsync();
                        _logger.LogInformation("Scan {ScanId} status updated to Cancelled", scanId);
                    }
                    else if (scan != null)
                    {
                        _logger.LogInformation("Scan {ScanId} is already in status {Status}, cannot cancel", scanId, scan.Status);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating scan status to Cancelled for scan: {ScanId}", scanId);
            }
        }


        private async Task ExecuteScanAsync(int scanId, AppDbContext dbContext, VideoAnalyzerService videoAnalyzer)
        {
            Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] ExecuteScanAsync START for scan {scanId}");
            _logger.LogCritical("=== SCAN START: ExecuteScanAsync called for scan ID: {ScanId} ===", scanId);
            
            try
            {
                _logger.LogInformation("=== ExecuteScanAsync: Inside try block for scan ID: {ScanId} ===", scanId);
                _logger.LogInformation("=== ExecuteScanAsync: DbContext is null: {IsNull} ===", dbContext == null);
                
                if (dbContext == null)
                {
                    _logger.LogError("=== ExecuteScanAsync: DbContext is NULL for scan ID: {ScanId} ===", scanId);
                    throw new InvalidOperationException("DbContext is null");
                }
                
                _logger.LogInformation("=== ExecuteScanAsync: Attempting to find scan record in database for ID: {ScanId} ===", scanId);
                Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] Starting database query for scan {scanId}");
                
                // Add timeout to database query
                using var dbQueryCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var findStartTime = DateTime.UtcNow;
                
                LibraryScan? scan = null;
                try
                {
                    _logger.LogInformation("=== ExecuteScanAsync: Calling FindAsync with timeout ===");
                    scan = await dbContext.LibraryScans.FindAsync(new object[] { scanId }, dbQueryCts.Token);
                    var findDuration = DateTime.UtcNow - findStartTime;
                    
                    Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] Database query completed in {findDuration.TotalMilliseconds}ms for scan {scanId}");
                    _logger.LogInformation("=== ExecuteScanAsync: Database FindAsync completed in {Duration}ms for scan ID: {ScanId} ===", findDuration.TotalMilliseconds, scanId);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogError("=== ExecuteScanAsync: Database query TIMED OUT after 30 seconds for scan ID: {ScanId} ===", scanId);
                    throw new TimeoutException($"Database query timed out for scan ID: {scanId}");
                }
                catch (Exception dbEx)
                {
                    _logger.LogError(dbEx, "=== ExecuteScanAsync: Database query FAILED for scan ID: {ScanId} ===", scanId);
                    throw;
                }
                
                if (scan == null)
                {
                    _logger.LogError("=== ExecuteScanAsync: Scan not found in database for ID: {ScanId} ===", scanId);
                    return;
                }

                _logger.LogInformation("=== ExecuteScanAsync: Scan record found - Path: {Path}, Status: {Status}, Started: {Started} ===", 
                    scan.LibraryPath, scan.Status, scan.StartedAt);

                // Check if scan was already cancelled in the database
                if (scan.Status == ScanStatus.Cancelled)
                {
                    _logger.LogInformation("=== ExecuteScanAsync: Scan {ScanId} is already marked as Cancelled, exiting ===", scanId);
                    return;
                }

                // Get or create cancellation token source for this specific scan
                // (should already exist from StartScanAsync, but create if missing for backward compatibility)
                if (!_scanCancellationTokens.TryGetValue(scanId, out var cts))
                {
                    _logger.LogWarning("=== ExecuteScanAsync: Cancellation token not found, creating new one for scan ID: {ScanId} ===", scanId);
                    cts = new CancellationTokenSource();
                    _scanCancellationTokens.TryAdd(scanId, cts);
                }
                var cancellationToken = cts.Token;
                
                // Check if scan was already cancelled before we started processing
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogInformation("=== ExecuteScanAsync: Scan {ScanId} was cancelled before processing started ===", scanId);
                    scan.Status = ScanStatus.Cancelled;
                    scan.CompletedAt = DateTime.UtcNow;
                    scan.ErrorMessage = "Scan was cancelled before processing started";
                    await dbContext.SaveChangesAsync();
                    return;
                }
                
                _scanStartTime = DateTime.UtcNow;
                scan.CurrentProcessingFile = null;
                await dbContext.SaveChangesAsync();

                try
                {
                    _logger.LogInformation("=== STEP 1: Starting file discovery for path: {Path} ===", scan.LibraryPath);
                    scan.Status = ScanStatus.Running;
                    await dbContext.SaveChangesAsync();
                    _logger.LogInformation("Scan status updated to Running");

                    _logger.LogInformation("Calling GetVideoFiles for path: {Path}", scan.LibraryPath);
                    var videoFiles = GetVideoFiles(scan.LibraryPath).ToList();
                    scan.TotalFiles = videoFiles.Count;

                    _logger.LogInformation("=== STEP 2: File discovery complete - Found {Count} video files ===", videoFiles.Count);
                    if (videoFiles.Count > 0)
                    {
                        _logger.LogInformation("First file: {FirstFile}", videoFiles.First());
                        _logger.LogInformation("Last file: {LastFile}", videoFiles.Last());
                    }

                    await dbContext.SaveChangesAsync();
                    _logger.LogInformation("Total files count saved to database: {Count}", scan.TotalFiles);

                    // Thread-safe counters for parallel processing
                    var processedCount = 0;
                    var failedCount = 0;
                    var lockObject = new object();
                    var failedFiles = new ConcurrentBag<(string FilePath, string FileName, string ErrorMessage, string ErrorType, long? FileSize)>();

                    // Determine degree of parallelism (use CPU count, but cap at 8 to avoid overwhelming the system)
                    var maxParallelism = Math.Min(Environment.ProcessorCount, 8);
                    _logger.LogInformation("=== STEP 3: Starting parallel file processing - {Count} files, Max parallelism: {MaxParallelism} ===", 
                        videoFiles.Count, maxParallelism);

                    // Use SemaphoreSlim to limit concurrent operations
                    var semaphore = new SemaphoreSlim(maxParallelism, maxParallelism);
                    var tasks = new List<Task>();

                    // Process files in parallel using Task.Run
                    foreach (var filePath in videoFiles)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            _logger.LogWarning("Scan cancellation requested");
                            break;
                        }

                        // Wait for available slot (this will throw if cancelled)
                        try
                        {
                            await semaphore.WaitAsync(cancellationToken);
                        }
                        catch (OperationCanceledException)
                        {
                            _logger.LogWarning("Semaphore wait cancelled for scan {ScanId}", scanId);
                            break; // Exit the loop if cancelled
                        }

                        var task = Task.Run(async () =>
                        {
                            try
                            {
                                // Check cancellation before starting
                                cancellationToken.ThrowIfCancellationRequested();
                                
                                _logger.LogInformation(">>> FILE START: {FileName}", Path.GetFileName(filePath));

                                // Update current processing file in database
                                lock (lockObject)
                                {
                                    try
                                    {
                                        var trackedScan = dbContext.LibraryScans.Find(scanId);
                                        if (trackedScan != null)
                                        {
                                            trackedScan.CurrentProcessingFile = filePath;
                                            dbContext.SaveChanges();
                                        }
                                    }
                                    catch (Exception dbEx)
                                    {
                                        _logger.LogWarning(dbEx, "Error updating current processing file");
                                    }
                                }

                                // Check if file exists
                                if (!File.Exists(filePath))
                                {
                                    _logger.LogWarning("File does not exist, skipping: {FilePath}", filePath);
                                    var failed = Interlocked.Increment(ref failedCount);
                                    failedFiles.Add((filePath, Path.GetFileName(filePath), "File does not exist", "FileNotFoundException", null));
                                    
                                    // Update progress after failure
                                    lock (lockObject)
                                    {
                                        try
                                        {
                                            var trackedScan = dbContext.LibraryScans.Find(scanId);
                                            if (trackedScan != null)
                                            {
                                                trackedScan.ProcessedFiles = processedCount;
                                                trackedScan.FailedFiles = failed;
                                                dbContext.SaveChanges();
                                            }
                                        }
                                        catch (Exception dbEx)
                                        {
                                            _logger.LogWarning(dbEx, "Error saving progress update after failure");
                                        }
                                    }
                                    return;
                                }

                                // Create a new scope and DbContext for this parallel task
                                using (var scope = _serviceScopeFactory.CreateScope())
                                {
                                    var taskDbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                                    var taskVideoAnalyzer = scope.ServiceProvider.GetRequiredService<VideoAnalyzerService>();

                                    // Analyze the file (this is CPU/IO intensive work)
                                    var analysisStartTime = DateTime.UtcNow;
                                    await AnalyzeAndStoreAsync(filePath, scanId, taskDbContext, taskVideoAnalyzer);
                                    var analysisDuration = DateTime.UtcNow - analysisStartTime;

                                    // Increment processed count AFTER successful analysis
                                    var currentIndex = Interlocked.Increment(ref processedCount);
                                    var fileIndex = currentIndex;

                                    _logger.LogInformation(">>> ✓ Successfully analyzed file {Index}/{Total} in {Duration}ms: {FileName}", 
                                        fileIndex, scan.TotalFiles, analysisDuration.TotalMilliseconds, Path.GetFileName(filePath));
                                }

                                // Update progress periodically
                                if (processedCount % 10 == 0 || processedCount == 1)
                                {
                                    lock (lockObject)
                                    {
                                        try
                                        {
                                            // Reload the scan entity to ensure it's tracked by the current context
                                            var trackedScan = dbContext.LibraryScans.Find(scanId);
                                            if (trackedScan != null)
                                            {
                                                trackedScan.ProcessedFiles = processedCount;
                                                trackedScan.FailedFiles = failedCount;
                                                dbContext.SaveChanges();
                                            }
                                            else
                                            {
                                                // Fallback: try to update the original scan entity
                                                scan.ProcessedFiles = processedCount;
                                                scan.FailedFiles = failedCount;
                                                dbContext.Entry(scan).Property(s => s.ProcessedFiles).IsModified = true;
                                                dbContext.Entry(scan).Property(s => s.FailedFiles).IsModified = true;
                                                dbContext.SaveChanges();
                                            }
                                        }
                                        catch (Exception dbEx)
                                        {
                                            _logger.LogWarning(dbEx, "Error saving progress update");
                                        }
                                    }

                                    if (processedCount % 10 == 0)
                                    {
                                        _logger.LogInformation("=== MILESTONE: {Processed}/{Total} files processed ({Percent}%) ===", 
                                            processedCount, scan.TotalFiles, Math.Round((double)processedCount / scan.TotalFiles * 100, 1));
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                var failed = Interlocked.Increment(ref failedCount);
                                failedFiles.Add((
                                    filePath,
                                    Path.GetFileName(filePath),
                                    ex.Message,
                                    ex.GetType().Name,
                                    File.Exists(filePath) ? new FileInfo(filePath).Length : null
                                ));

                                _logger.LogError(ex, ">>> ✗ FAILED to analyze file: {FilePath}", filePath);
                                
                                // Update progress after exception
                                lock (lockObject)
                                {
                                    try
                                    {
                                        var trackedScan = dbContext.LibraryScans.Find(scanId);
                                        if (trackedScan != null)
                                        {
                                            trackedScan.ProcessedFiles = processedCount;
                                            trackedScan.FailedFiles = failed;
                                            dbContext.SaveChanges();
                                        }
                                    }
                                    catch (Exception dbEx)
                                    {
                                        _logger.LogWarning(dbEx, "Error saving progress update after exception");
                                    }
                                }
                            }
                            finally
                            {
                                semaphore.Release();
                            }
                        }, cancellationToken);

                        tasks.Add(task);
                    }

                    // Wait for all tasks to complete, but check for cancellation
                    try
                    {
                        await Task.WhenAll(tasks);
                    }
                    catch (OperationCanceledException)
                    {
                        _logger.LogWarning("Tasks cancelled, stopping scan {ScanId}", scanId);
                        // Cancel remaining tasks
                        foreach (var task in tasks.Where(t => !t.IsCompleted))
                        {
                            // Tasks will check cancellation token themselves
                        }
                        throw; // Re-throw to be caught by outer catch
                    }

                    // Save all failed files to database
                    if (failedFiles.Count > 0)
                    {
                        _logger.LogInformation("Saving {Count} failed file records to database", failedFiles.Count);
                        foreach (var failedFile in failedFiles)
                        {
                            try
                            {
                                var failedFileRecord = new FailedFile
                                {
                                    LibraryScanId = scanId,
                                    FilePath = failedFile.FilePath,
                                    FileName = failedFile.FileName,
                                    ErrorMessage = failedFile.ErrorMessage,
                                    ErrorType = failedFile.ErrorType,
                                    FailedAt = DateTime.UtcNow,
                                    FileSize = failedFile.FileSize
                                };
                                dbContext.FailedFiles.Add(failedFileRecord);
                            }
                            catch (Exception dbEx)
                            {
                                _logger.LogError(dbEx, "Failed to create failed file record for: {FilePath}", failedFile.FilePath);
                            }
                        }
                        await dbContext.SaveChangesAsync();
                    }

                    // Final progress update
                    var processed = processedCount;
                    var failed = failedCount;
                    scan.ProcessedFiles = processed;
                    scan.FailedFiles = failed;
                    await dbContext.SaveChangesAsync();

                    _logger.LogInformation("=== STEP 4: File processing loop completed ===");
                    _logger.LogInformation("Final stats - Processed: {Processed}, Failed: {Failed}, Total: {Total}", 
                        processed, failed, scan.TotalFiles);

                    _logger.LogInformation("=== STEP 5: Marking scan as completed ===");
                    scan.Status = ScanStatus.Completed;
                    scan.CompletedAt = DateTime.UtcNow;
                    var totalDuration = DateTime.UtcNow - scan.StartedAt;
                    await dbContext.SaveChangesAsync();

                    _logger.LogInformation("=== SCAN COMPLETE: Scan {ScanId} finished successfully ===", scanId);
                    _logger.LogInformation("Duration: {Duration}, Processed: {Processed}, Failed: {Failed}, Total: {Total}", 
                        totalDuration, processed, failed, scan.TotalFiles);
                    
                    // Update LibraryPath record with scan results
                    try
                    {
                        var libraryPathRecord = await dbContext.LibraryPaths
                            .FirstOrDefaultAsync(lp => lp.Path == scan.LibraryPath);
                        if (libraryPathRecord != null)
                        {
                            libraryPathRecord.LastScannedAt = DateTime.UtcNow;
                            libraryPathRecord.TotalFiles = scan.TotalFiles;
                            // Calculate total size from ALL video analyses for this library path
                            // (not just the current scan, since rescanning may reuse existing records)
                            // We need to join with LibraryScans to filter by LibraryPath
                            var totalSize = await dbContext.VideoAnalyses
                                .Include(va => va.LibraryScan)
                                .Where(va => va.LibraryScan != null && va.LibraryScan.LibraryPath == scan.LibraryPath)
                                .SumAsync(va => (long?)va.FileSize) ?? 0;
                            libraryPathRecord.TotalSize = totalSize;
                            await dbContext.SaveChangesAsync();
                            _logger.LogInformation("LibraryPath record updated with scan results - TotalSize: {TotalSize}", totalSize);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to update LibraryPath record after scan");
                    }
                }
                catch (Exception innerEx)
                {
                    _logger.LogError(innerEx, "=== File processing failed for scan {ScanId} ===", scanId);
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "=== SCAN FAILED: Scan {ScanId} encountered an error ===", scanId);
                _logger.LogError("Exception type: {Type}", ex.GetType().Name);
                _logger.LogError("Exception message: {Message}", ex.Message);
                _logger.LogError("Stack trace: {StackTrace}", ex.StackTrace);
                
                if (ex.InnerException != null)
                {
                    _logger.LogError("Inner exception: {Type} - {Message}", 
                        ex.InnerException.GetType().Name, ex.InnerException.Message);
                }
                
                try
                {
                    var failedScan = await dbContext.LibraryScans.FindAsync(scanId);
                    if (failedScan != null)
                    {
                        failedScan.Status = ScanStatus.Failed;
                        failedScan.ErrorMessage = $"{ex.GetType().Name}: {ex.Message}";
                        failedScan.CompletedAt = DateTime.UtcNow;
                        await dbContext.SaveChangesAsync();
                        _logger.LogInformation("Scan failure saved to database");
                    }
                }
                catch (Exception dbEx)
                {
                    _logger.LogError(dbEx, "Failed to update scan status in catch block");
                }
                
                // Re-throw to be caught by background task handler
                throw;
            }
            finally
            {
                // Clean up cancellation token
                _scanCancellationTokens.TryRemove(scanId, out _);
                
                try
                {
                    var finalScan = await dbContext.LibraryScans.FindAsync(scanId);
                    if (finalScan != null)
                    {
                        finalScan.CurrentProcessingFile = null;
                        await dbContext.SaveChangesAsync();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error clearing current processing file");
                }
                _logger.LogInformation("=== SCAN END: ExecuteScanAsync finished for scan ID: {ScanId} ===", scanId);
            }
        }

        private async Task AnalyzeAndStoreAsync(string filePath, int scanId, AppDbContext dbContext, VideoAnalyzerService videoAnalyzer)
        {
            _logger.LogInformation(">>> AnalyzeAndStoreAsync START for: {FileName}", Path.GetFileName(filePath));
            
            // Check if already analyzed
            _logger.LogDebug("Checking if file already analyzed in database");
            var existing = await dbContext.VideoAnalyses
                .FirstOrDefaultAsync(a => a.FilePath == filePath);

            if (existing != null)
            {
                _logger.LogInformation("File already analyzed (ID: {Id}), skipping: {FilePath}", existing.Id, filePath);
                return;
            }

            try
            {
                // Check if file exists before attempting analysis
                _logger.LogDebug("Checking file existence: {FilePath}", filePath);
                if (!File.Exists(filePath))
                {
                    _logger.LogWarning("File does not exist, skipping: {FilePath}", filePath);
                    return;
                }
                _logger.LogDebug("File exists and is accessible");

                _logger.LogInformation(">>> Starting video analysis for: {FilePath}", filePath);
                
                // Look for ALL external subtitle files (there can be multiple)
                var externalSubtitlePaths = FindAllExternalSubtitles(filePath);
                if (externalSubtitlePaths.Count > 0)
                {
                    _logger.LogInformation("Found {Count} external subtitle file(s): {SubtitlePaths}", 
                        externalSubtitlePaths.Count, string.Join(", ", externalSubtitlePaths));
                }
                
                // Run video analysis in a task to avoid blocking, with timeout
                _logger.LogDebug("Creating analysis task for: {FilePath}", filePath);
                var analysisTask = Task.Run(() => 
                {
                    try
                    {
                        _logger.LogDebug("Analysis task started, calling AnalyzeVideoStructured for: {FilePath}", filePath);
                        var result = videoAnalyzer.AnalyzeVideoStructured(filePath, externalSubtitlePaths);
                        _logger.LogDebug("Analysis task completed successfully for: {FilePath}", filePath);
                        return result;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error in video analysis task for: {FilePath}. Error: {Error}", filePath, ex.Message);
                        throw;
                    }
                });
                
                _logger.LogDebug("Waiting for analysis with 5 minute timeout for: {FilePath}", filePath);
                // Wait for analysis with timeout (5 minutes per file)
                var timeoutTask = Task.Delay(TimeSpan.FromMinutes(5));
                var completedTask = await Task.WhenAny(analysisTask, timeoutTask);
                
                if (completedTask == timeoutTask)
                {
                    _logger.LogError("Video analysis timed out after 5 minutes for: {FilePath}", filePath);
                    throw new TimeoutException($"Video analysis timed out for: {filePath}");
                }
                
                _logger.LogDebug("Analysis task completed, retrieving result for: {FilePath}", filePath);
                var (videoInfo, compatibilityResult) = await analysisTask;
                _logger.LogInformation(">>> Video analysis completed successfully for: {FilePath}", filePath);
                
                // Check if media information is valid (not broken)
                bool isBroken = false;
                string? brokenReason = null;
                
                // Check for critical missing fields that indicate broken/invalid media
                if (string.IsNullOrWhiteSpace(videoInfo.VideoCodec) && string.IsNullOrWhiteSpace(videoInfo.Container))
                {
                    isBroken = true;
                    brokenReason = "No video codec or container information found";
                }
                else if (videoInfo.Width == 0 || videoInfo.Height == 0)
                {
                    isBroken = true;
                    brokenReason = "Invalid video resolution (width or height is 0)";
                }
                else if (videoInfo.Duration <= 0)
                {
                    isBroken = true;
                    brokenReason = "Invalid duration (duration is 0 or negative)";
                }
                else if (videoInfo.FileSize == 0)
                {
                    isBroken = true;
                    brokenReason = "File size is 0 bytes";
                }
                
                if (isBroken)
                {
                    _logger.LogWarning(">>> Media file appears to be broken: {FilePath}. Reason: {Reason}", filePath, brokenReason);
                }
                
                // Generate report (this re-analyzes, but it's fast since it uses cached data)
                _logger.LogDebug(">>> Generating report for: {FilePath}", filePath);
                var reportStartTime = DateTime.UtcNow;
                var reportTask = Task.Run(() => videoAnalyzer.AnalyzeVideo(filePath));
                var reportTimeoutTask = Task.Delay(TimeSpan.FromMinutes(1));
                var reportCompletedTask = await Task.WhenAny(reportTask, reportTimeoutTask);
                
                string report;
                if (reportCompletedTask == reportTimeoutTask)
                {
                    _logger.LogWarning("Report generation timed out for: {FilePath}, using empty report", filePath);
                    report = "Report generation timed out";
                }
                else
                {
                    report = await reportTask;
                    var reportDuration = DateTime.UtcNow - reportStartTime;
                    _logger.LogDebug("Report generated in {Duration}ms for: {FilePath}", reportDuration.TotalMilliseconds, filePath);
                }
                
                _logger.LogInformation(">>> Creating VideoAnalysis database record");
                var analysis = new VideoAnalysis
                {
                    FilePath = filePath,
                    FileName = Path.GetFileName(filePath),
                    FileSize = new FileInfo(filePath).Length,
                    Duration = videoInfo.Duration,
                    Container = videoInfo.Container,
                    VideoCodec = videoInfo.VideoCodec,
                    VideoCodecTag = videoInfo.VideoCodecTag,
                    IsCodecTagCorrect = videoInfo.IsCodecTagCorrect,
                    BitDepth = videoInfo.BitDepth,
                    Width = videoInfo.Width,
                    Height = videoInfo.Height,
                    FrameRate = videoInfo.FrameRate,
                    IsHDR = videoInfo.IsHDR,
                    HDRType = videoInfo.HDRType,
                    IsFastStart = videoInfo.IsFastStart,
                    AudioCodecs = string.Join(",", videoInfo.AudioTracks.Select(a => a.Codec).Distinct()),
                    AudioTrackCount = videoInfo.AudioTracks.Count,
                    AudioTracksJson = System.Text.Json.JsonSerializer.Serialize(videoInfo.AudioTracks, new System.Text.Json.JsonSerializerOptions
                    {
                        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
                    }),
                    // Store subtitle formats, marking external ones with "(External)" suffix
                    SubtitleFormats = string.Join(",", videoInfo.SubtitleTracks.Select(s => 
                        s.IsEmbedded ? s.Format : $"{s.Format} (External)").Distinct()),
                    SubtitleTrackCount = videoInfo.SubtitleTracks.Count,
                    SubtitleTracksJson = System.Text.Json.JsonSerializer.Serialize(videoInfo.SubtitleTracks, new System.Text.Json.JsonSerializerOptions
                    {
                        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
                    }),
                    OverallScore = ParseScore(compatibilityResult.OverallScore),
                    CompatibilityRating = compatibilityResult.CompatibilityRating,
                    DirectPlayClients = compatibilityResult.ClientResults.Values.Count(r => r.Status == "Direct Play"),
                    RemuxClients = compatibilityResult.ClientResults.Values.Count(r => r.Status == "Remux"),
                    TranscodeClients = compatibilityResult.ClientResults.Values.Count(r => r.Status == "Transcode"),
                    Issues = System.Text.Json.JsonSerializer.Serialize(compatibilityResult.Issues),
                    Recommendations = System.Text.Json.JsonSerializer.Serialize(compatibilityResult.Recommendations),
                    ClientResults = System.Text.Json.JsonSerializer.Serialize(compatibilityResult.ClientResults),
                    FullReport = report,
                    IsBroken = isBroken,
                    BrokenReason = brokenReason,
                    AnalyzedAt = DateTime.UtcNow,
                    LibraryScanId = scanId
                };

                _logger.LogDebug(">>> Adding VideoAnalysis to database context");
                _logger.LogInformation(">>> Saving video with {SubtitleCount} subtitle track(s). Formats: {Formats}", 
                    videoInfo.SubtitleTracks.Count, string.Join(", ", videoInfo.SubtitleTracks.Select(s => s.Format).Distinct()));
                dbContext.VideoAnalyses.Add(analysis);
                
                _logger.LogDebug(">>> Saving VideoAnalysis to database");
                await dbContext.SaveChangesAsync();
                _logger.LogInformation(">>> VideoAnalysis saved successfully (ID: {Id}) for: {FileName}", 
                    analysis.Id, Path.GetFileName(filePath));
                _logger.LogInformation(">>> AnalyzeAndStoreAsync COMPLETE for: {FileName}", Path.GetFileName(filePath));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, ">>> ERROR in AnalyzeAndStoreAsync for: {FilePath}", filePath);
                _logger.LogError("Exception type: {Type}, Message: {Message}", ex.GetType().Name, ex.Message);
                _logger.LogError("Stack trace: {StackTrace}", ex.StackTrace);
                
                // Store broken file information instead of throwing
                try
                {
                    var brokenAnalysis = new VideoAnalysis
                    {
                        FilePath = filePath,
                        FileName = Path.GetFileName(filePath),
                        FileSize = File.Exists(filePath) ? new FileInfo(filePath).Length : 0,
                        IsBroken = true,
                        BrokenReason = $"{ex.GetType().Name}: {ex.Message}",
                        OverallScore = CompatibilityScore.Unknown,
                        AnalyzedAt = DateTime.UtcNow,
                        LibraryScanId = scanId
                    };
                    
                    // Check if already exists
                    var existingBroken = await dbContext.VideoAnalyses
                        .FirstOrDefaultAsync(a => a.FilePath == filePath);
                    
                    if (existingBroken != null)
                    {
                        // Update existing record
                        existingBroken.IsBroken = true;
                        existingBroken.BrokenReason = brokenAnalysis.BrokenReason;
                        existingBroken.AnalyzedAt = DateTime.UtcNow;
                    }
                    else
                    {
                        dbContext.VideoAnalyses.Add(brokenAnalysis);
                    }
                    
                    await dbContext.SaveChangesAsync();
                    _logger.LogWarning(">>> Stored broken file record for: {FilePath}", filePath);
                }
                catch (Exception saveEx)
                {
                    _logger.LogError(saveEx, ">>> Failed to save broken file record for: {FilePath}", filePath);
                    // Don't throw - we've logged the error
                }
            }
        }

        private IEnumerable<string> GetVideoFiles(string directory)
        {
            try
            {
                return Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories)
                    .Where(f => VideoExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enumerating files in directory: {Directory}", directory);
                return Enumerable.Empty<string>();
            }
        }

        /// <summary>
        /// Finds ALL external subtitle files that match the video file.
        /// Checks for common naming patterns:
        /// - video.srt (same name, different extension)
        /// - video.en.srt, video.eng.srt (with language code)
        /// - video.en-US.srt (with locale)
        /// - video.forced.srt, video.sdh.srt (with modifiers)
        /// </summary>
        private List<string> FindAllExternalSubtitles(string videoFilePath)
        {
            var foundSubtitles = new List<string>();
            
            try
            {
                var directory = Path.GetDirectoryName(videoFilePath);
                if (string.IsNullOrEmpty(directory))
                    return foundSubtitles;

                var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(videoFilePath);
                var baseName = Path.Combine(directory, fileNameWithoutExtension);

                // Find ALL subtitle files that match the video name
                // This includes:
                // 1. Exact match: video.srt
                // 2. With language code: video.en.srt, video.eng.srt, video.english.srt
                // 3. With locale: video.en-US.srt
                // 4. With modifiers: video.forced.srt, video.sdh.srt, video.cc.srt
                // 5. Any combination: video.en.forced.srt

                // Get all subtitle files in the directory
                var allSubtitleFiles = Directory.GetFiles(directory, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(f => SubtitleExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    .ToList();

                // Normalize video name for comparison (remove common separators and make lowercase)
                var normalizedVideoName = fileNameWithoutExtension.ToLowerInvariant()
                    .Replace(" ", "")
                    .Replace("_", "")
                    .Replace("-", "")
                    .Replace(".", "");
                
                foreach (var subtitleFile in allSubtitleFiles)
                {
                    var subtitleNameWithoutExt = Path.GetFileNameWithoutExtension(subtitleFile);
                    var subtitleFileName = Path.GetFileName(subtitleFile);
                    
                    _logger.LogDebug("Checking subtitle file: {SubtitleFile} (name without ext: {NameWithoutExt})", subtitleFileName, subtitleNameWithoutExt);
                    
                    // Check multiple matching patterns:
                    // 1. Exact match (case-insensitive)
                    if (subtitleNameWithoutExt.Equals(fileNameWithoutExtension, StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogDebug("Match found (exact): {SubtitleFile}", subtitleFileName);
                        foundSubtitles.Add(subtitleFile);
                        continue;
                    }
                    
                    // 2. Starts with video name followed by dot (e.g., video.en.srt, video.forced.srt, video.ger.srt)
                    // This handles cases like "video.ger.srt" where .ger is a language code
                    // Example: "Black Mirror (2011) - S07E01 - Common People.ger.srt"
                    //          should match "Black Mirror (2011) - S07E01 - Common People.mp4"
                    var videoNameWithDot = fileNameWithoutExtension + ".";
                    if (subtitleNameWithoutExt.StartsWith(videoNameWithDot, StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogDebug("Match found (dot separator): {SubtitleFile} (video: '{VideoName}', subtitle: '{SubtitleName}')", 
                            subtitleFileName, fileNameWithoutExtension, subtitleNameWithoutExt);
                        foundSubtitles.Add(subtitleFile);
                        continue;
                    }
                    
                    // 2b. Video name is a prefix of subtitle name followed by dot (handles language codes)
                    // This is a more explicit check for the same pattern
                    if (subtitleNameWithoutExt.Length > fileNameWithoutExtension.Length)
                    {
                        var prefix = subtitleNameWithoutExt.Substring(0, fileNameWithoutExtension.Length);
                        if (prefix.Equals(fileNameWithoutExtension, StringComparison.OrdinalIgnoreCase))
                        {
                            var nextChar = subtitleNameWithoutExt[fileNameWithoutExtension.Length];
                            if (nextChar == '.')
                            {
                                _logger.LogDebug("Match found (video name prefix with dot): {SubtitleFile} (video: '{VideoName}', subtitle: '{SubtitleName}')", 
                                    subtitleFileName, fileNameWithoutExtension, subtitleNameWithoutExt);
                                foundSubtitles.Add(subtitleFile);
                                continue;
                            }
                        }
                    }
                    
                    // 3. Starts with video name followed by space, dash, or underscore
                    // (e.g., video - subtitle.srt, video_subtitle.srt, video subtitle.srt)
                    var separators = new[] { " ", "-", "_", " - ", " -", "- " };
                    bool matchedSeparator = false;
                    foreach (var separator in separators)
                    {
                        if (subtitleNameWithoutExt.StartsWith(fileNameWithoutExtension + separator, StringComparison.OrdinalIgnoreCase))
                        {
                            _logger.LogDebug("Match found (separator '{Separator}'): {SubtitleFile}", separator, subtitleFileName);
                            foundSubtitles.Add(subtitleFile);
                            matchedSeparator = true;
                            break;
                        }
                    }
                    if (matchedSeparator) continue;
                    
                    // 4. Contains video name (more lenient matching)
                    // This catches cases where subtitle might have additional text before or after
                    if (subtitleNameWithoutExt.Contains(fileNameWithoutExtension, StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogDebug("Match found (contains video name): {SubtitleFile}", subtitleFileName);
                        foundSubtitles.Add(subtitleFile);
                        continue;
                    }
                    
                    // 5. Normalized name starts with normalized video name
                    // This catches variations like "Video Name" vs "video_name" vs "video-name"
                    var normalizedSubtitleName = subtitleNameWithoutExt.ToLowerInvariant()
                        .Replace(" ", "")
                        .Replace("_", "")
                        .Replace("-", "")
                        .Replace(".", "");
                    
                    if (normalizedSubtitleName.StartsWith(normalizedVideoName))
                    {
                        // If names are equal (after normalization), it's a match
                        if (normalizedSubtitleName == normalizedVideoName)
                        {
                            _logger.LogDebug("Match found (normalized exact): {SubtitleFile}", subtitleFileName);
                            foundSubtitles.Add(subtitleFile);
                            continue;
                        }
                        
                        // If subtitle name is longer, check if the next character is a separator
                        // This prevents "video1" from matching "video10"
                        var nextCharIndex = normalizedVideoName.Length;
                        if (nextCharIndex < normalizedSubtitleName.Length)
                        {
                            var nextChar = normalizedSubtitleName[nextCharIndex];
                            // Allow if next char is a separator (dot, space, dash, underscore, or parenthesis)
                            if (nextChar == '.' || nextChar == ' ' || nextChar == '-' || 
                                nextChar == '_' || nextChar == '(' || nextChar == '[')
                            {
                                _logger.LogDebug("Match found (normalized with separator): {SubtitleFile}", subtitleFileName);
                                foundSubtitles.Add(subtitleFile);
                                continue;
                            }
                        }
                    }
                    
                    // 6. Normalized name contains normalized video name (most lenient)
                    if (normalizedSubtitleName.Contains(normalizedVideoName) && normalizedVideoName.Length >= 3)
                    {
                        _logger.LogDebug("Match found (normalized contains): {SubtitleFile}", subtitleFileName);
                        foundSubtitles.Add(subtitleFile);
                        continue;
                    }
                }

                // Remove duplicates and sort
                foundSubtitles = foundSubtitles
                    .Distinct()
                    .OrderBy(f => f.Length) // Prefer shorter names (closer match)
                    .ThenBy(f => f) // Then alphabetically for consistency
                    .ToList();

                if (foundSubtitles.Count > 0)
                {
                    _logger.LogInformation("Found {Count} external subtitle file(s) for: {VideoPath}: {SubtitleFiles}", 
                        foundSubtitles.Count, videoFilePath, string.Join(", ", foundSubtitles.Select(f => Path.GetFileName(f))));
                }
                else
                {
                    _logger.LogDebug("No matching subtitle files found for: {VideoPath}", videoFilePath);
                }

                return foundSubtitles;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error searching for external subtitle files for: {VideoPath}", videoFilePath);
                return foundSubtitles;
            }
        }

        /// <summary>
        /// Legacy method - kept for backward compatibility but now returns first found subtitle.
        /// Use FindAllExternalSubtitles instead.
        /// </summary>
        private string? FindExternalSubtitle(string videoFilePath)
        {
            var allSubtitles = FindAllExternalSubtitles(videoFilePath);
            return allSubtitles.FirstOrDefault();
        }


        private CompatibilityScore ParseScore(string score)
        {
            return score.ToUpperInvariant() switch
            {
                "OPTIMAL" => CompatibilityScore.Optimal,
                "GOOD" => CompatibilityScore.Good,
                "POOR" => CompatibilityScore.Poor,
                _ => CompatibilityScore.Unknown
            };
        }
    }
}

