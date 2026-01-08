# Developer Guide
## Optimarr - Media Optimization Platform

**Version:** 1.1.0  
**Last Updated:** 2025-01-XX  
**Application Version:** See [CHANGELOG.md](CHANGELOG.md)

---

## 1. Getting Started

### 1.1 Prerequisites

- **.NET 8.0 SDK** or later
- **Docker** and **Docker Compose** (for containerized development)
- **MediaInfo CLI** installed and available in PATH
- **Git** for version control
- **IDE:** Visual Studio, VS Code, or Rider (recommended)

### 1.2 Development Environment Setup

#### Option 1: Local Development

```bash
# Clone the repository
git clone <repository-url>
cd Optimarr

# Restore dependencies
dotnet restore

# Build the project
dotnet build

# Run the application
dotnet run
```

The application will be available at `http://localhost:5000`

#### Option 2: Docker Development

```bash
# Build and run with Docker Compose
docker-compose -f docker-compose.dev.yml up --build

# Or use the development Dockerfile
docker build -f Dockerfile.dev -t optimarr-dev .
docker run -p 5000:8080 -v $(pwd)/config:/app/config -v $(pwd)/data:/app/data optimarr-dev
```

### 1.3 Project Structure

```
Optimarr/
├── Controllers/          # API Controllers
│   ├── AnalysisController.cs
│   ├── LibraryController.cs
│   ├── PlaybackController.cs
│   ├── ServarrController.cs
│   └── SystemController.cs
├── Services/            # Business Logic Services
│   ├── VideoAnalyzerService.cs
│   ├── LibraryScannerService.cs
│   ├── SonarrService.cs
│   ├── RadarrService.cs
│   ├── ServarrSyncService.cs
│   ├── JellyfinService.cs
│   ├── PlaybackSyncService.cs
│   └── DatabaseMigrationService.cs
├── Models/              # Data Models
│   ├── VideoAnalysis.cs
│   ├── LibraryScan.cs
│   ├── LibraryPath.cs
│   ├── PlaybackHistory.cs
│   └── ...
├── Data/                # Data Access
│   └── AppDbContext.cs
├── wwwroot/             # Frontend Assets
│   ├── index.html
│   ├── styles.css
│   └── js/
├── config/              # Configuration Files
│   └── appsettings.json
├── data/                # Database Files (SQLite)
├── logs/                # Log Files
├── Program.cs           # Application Entry Point
└── optimarr.csproj      # Project File
```

---

## 2. Code Organization & Patterns

### 2.1 Naming Conventions

- **Classes:** PascalCase (e.g., `VideoAnalyzerService`)
- **Methods:** PascalCase (e.g., `AnalyzeVideoAsync`)
- **Variables:** camelCase (e.g., `videoPath`)
- **Constants:** PascalCase (e.g., `MaxRetryCount`)
- **Private Fields:** `_camelCase` (e.g., `_logger`)

### 2.2 Service Pattern

All business logic is encapsulated in service classes:

```csharp
public class VideoAnalyzerService
{
    private readonly ILogger<VideoAnalyzerService> _logger;
    private readonly IConfiguration _configuration;

    public VideoAnalyzerService(
        IConfiguration configuration,
        ILogger<VideoAnalyzerService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<CompatibilityResult> AnalyzeVideoAsync(string videoPath)
    {
        // Implementation
    }
}
```

**Key Principles:**
- Services are registered in `Program.cs` via dependency injection
- Services use constructor injection for dependencies
- Services are scoped appropriately (Scoped, Singleton, Transient)

### 2.3 Controller Pattern

Controllers are thin and delegate to services:

```csharp
[ApiController]
[Route("api/[controller]")]
public class AnalysisController : ControllerBase
{
    private readonly VideoAnalyzerService _analyzer;
    private readonly ILogger<AnalysisController> _logger;

    public AnalysisController(
        VideoAnalyzerService analyzer,
        ILogger<AnalysisController> logger)
    {
        _analyzer = analyzer;
        _logger = logger;
    }

    [HttpPost("analyze")]
    public async Task<ActionResult<CompatibilityResult>> AnalyzeVideo([FromBody] AnalyzeRequest request)
    {
        try
        {
            var result = await _analyzer.AnalyzeVideoAsync(request.VideoPath);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing video");
            return StatusCode(500, new { error = ex.Message });
        }
    }
}
```

**Key Principles:**
- Controllers handle HTTP concerns only
- Business logic stays in services
- Consistent error handling and logging
- RESTful API design

### 2.4 Frontend Module Pattern

JavaScript modules are self-contained:

```javascript
// settings.js
async function loadSonarrSettings() {
    try {
        const response = await fetch('/api/servarr/sonarr/settings');
        const data = await response.json();
        // Update UI
    } catch (error) {
        console.error('Error loading Sonarr settings:', error);
    }
}

// Export for use in other modules
export { loadSonarrSettings };

// Make available globally for HTML onclick handlers
window.loadSonarrSettings = loadSonarrSettings;
```

**Key Principles:**
- ES6 modules with `export`/`import`
- Functions available on `window` for HTML event handlers
- Consistent error handling
- Async/await for API calls

---

## 3. Key Components Deep Dive

### 3.1 Video Analysis Flow

```
User Request
    ↓
AnalysisController.AnalyzeVideo()
    ↓
VideoAnalyzerService.AnalyzeVideoAsync()
    ↓
MediaInfo CLI (extract metadata)
    ↓
JellyfinCompatibilityData (match codecs)
    ↓
Calculate Compatibility Score
    ↓
Generate CompatibilityResult
    ↓
Return to Client
```

**Key Files:**
- `Services/VideoAnalyzerService.cs`: Main analysis logic
- `Services/JellyfinCompatibilityData.cs`: Compatibility matrix
- `Models/CompatibilityResult.cs`: Result data structure

### 3.2 Library Scanning Flow

```
User Initiates Scan
    ↓
LibraryController.StartScan()
    ↓
LibraryScannerService.StartScanAsync()
    ↓
Create LibraryScan Record
    ↓
Background Task:
    - Recursively scan directories
    - Queue files for analysis
    - Update progress
    ↓
VideoAnalyzerService.AnalyzeVideoAsync() (per file)
    ↓
Save VideoAnalysis Records
    ↓
Update LibraryScan Status
```

**Key Files:**
- `Services/LibraryScannerService.cs`: Scanning logic
- `Controllers/LibraryController.cs`: API endpoints
- `Models/LibraryScan.cs`: Scan tracking model

### 3.3 Servarr Synchronization Flow

```
User Clicks Sync Button
    ↓
ServarrController.SyncSonarr() / SyncRadarr()
    ↓
ServarrSyncService.SyncSonarrAsync() / SyncRadarrAsync()
    ↓
SonarrService.GetRootFolders() / RadarrService.GetRootFolders()
    ↓
Apply Path Mappings (if configured)
    ↓
Check for Existing LibraryPaths
    ↓
Create/Update LibraryPath Records
    ↓
Return Sync Result
```

**Key Files:**
- `Services/ServarrSyncService.cs`: Sync logic
- `Services/SonarrService.cs` / `Services/RadarrService.cs`: API clients
- `Controllers/ServarrController.cs`: API endpoints

---

## 4. Database Schema

### 4.1 Entity Relationships

```
LibraryScan (1) ──→ (Many) VideoAnalysis
LibraryScan (1) ──→ (Many) FailedFile
LibraryPath (Standalone)
PlaybackHistory (Standalone)
```

### 4.2 Key Constraints

- **LibraryPaths.Path:** UNIQUE constraint (prevents duplicates)
- **VideoAnalysis.LibraryScanId:** Foreign key with cascade delete
- **Indexes:** FilePath, LibraryScanId, AnalyzedAt, OverallScore

### 4.3 Migrations

Migrations are handled automatically by `DatabaseMigrationService`:

```csharp
public class DatabaseMigrationService : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _dbContext.Database.MigrateAsync(cancellationToken);
    }
}
```

**Note:** Entity Framework migrations are applied on startup.

---

## 5. Configuration Management

### 5.1 Reading Configuration

```csharp
// In service constructor
private readonly IConfiguration _configuration;

// Get value
var baseUrl = _configuration["Servarr:Sonarr:BaseUrl"];

// Get section
var sonarrSection = _configuration.GetSection("Servarr:Sonarr");
var baseUrl = sonarrSection["BaseUrl"];
var enabled = sonarrSection.GetValue<bool>("Enabled");
```

### 5.2 Writing Configuration

Configuration is written directly to `appsettings.json`:

```csharp
// Read JSON file
var jsonContent = await File.ReadAllTextAsync(appsettingsPath);
using var jsonDoc = JsonDocument.Parse(jsonContent);
var root = jsonDoc.RootElement;

// Modify JSON
var jsonObject = new JsonObject();
// ... modify structure ...

// Write back
var options = new JsonSerializerOptions { WriteIndented = true };
var newJson = jsonObject.ToJsonString(options);
await File.WriteAllTextAsync(appsettingsPath, newJson);

// Reload configuration
if (_configuration is IConfigurationRoot configRoot)
{
    configRoot.Reload();
}
```

**Key Files:**
- `Controllers/ServarrController.cs`: Settings endpoints
- `wwwroot/js/settings.js`: Frontend settings management

---

## 6. Path Mapping System

### 6.1 Purpose

Path mappings translate file paths between different environments, particularly useful in Docker scenarios where:
- Sonarr/Radarr see paths like `/tv` or `/movies`
- Optimarr needs paths like `/mnt/media/tv` or `/mnt/media/movies`

### 6.2 Implementation

```csharp
private string MapPath(string path, string servarrType)
{
    var section = _configuration.GetSection($"Servarr:{servarrType}:PathMappings");
    if (!section.Exists()) return path;

    foreach (var item in section.GetChildren())
    {
        var from = item["From"] ?? "";
        var to = item["To"] ?? "";
        if (!string.IsNullOrEmpty(from) && !string.IsNullOrEmpty(to) && path.StartsWith(from))
        {
            return path.Replace(from, to);
        }
    }
    return path;
}
```

**Usage:**
- Called in `ServarrSyncService` when processing root folders
- Applied before saving to database
- Preserves original path in `ServarrRootFolderPath` field

---

## 7. Error Handling Best Practices

### 7.1 Service Layer

```csharp
public async Task<Result> DoSomethingAsync()
{
    try
    {
        // Operation
        return success;
    }
    catch (SpecificException ex)
    {
        _logger.LogWarning(ex, "Recoverable error occurred");
        // Handle gracefully
        return fallback;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Unexpected error occurred");
        throw; // Re-throw for controller to handle
    }
}
```

### 7.2 Controller Layer

```csharp
[HttpPost("endpoint")]
public async Task<ActionResult> Endpoint()
{
    try
    {
        var result = await _service.DoSomethingAsync();
        return Ok(result);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error in endpoint");
        return StatusCode(500, new { error = ex.Message });
    }
}
```

### 7.3 Frontend

```javascript
async function doSomething() {
    try {
        const response = await fetch('/api/endpoint');
        if (!response.ok) {
            throw new Error(`HTTP ${response.status}`);
        }
        const data = await response.json();
        // Handle success
    } catch (error) {
        console.error('Error:', error);
        showErrorMessage('Operation failed: ' + error.message);
    }
}
```

---

## 8. Logging

### 8.1 Logging Levels

- **Information:** Normal operations, startup, configuration
- **Warning:** Recoverable issues, missing optional data
- **Error:** Exceptions, API failures, critical issues
- **Fatal:** Application startup failures

### 8.2 Logging Examples

```csharp
_logger.LogInformation("Starting library scan: {ScanId}", scanId);
_logger.LogWarning("Root folder not accessible: {Path}", path);
_logger.LogError(ex, "Error analyzing video: {Path}", videoPath);
```

### 8.3 Structured Logging

Serilog supports structured logging:

```csharp
_logger.LogInformation(
    "Scan progress: {Processed}/{Total} files processed",
    processed, total);
```

---

## 9. Testing

### 9.1 Manual Testing Checklist

- [ ] Video analysis works for all supported formats
- [ ] Library scanning completes successfully
- [ ] Sonarr/Radarr sync updates library paths
- [ ] Path mappings work correctly
- [ ] Settings can be saved and loaded
- [ ] Error messages are user-friendly
- [ ] UI is responsive on mobile devices

### 9.2 Integration Testing

Test with Docker Compose:

```bash
# Start all services
docker-compose up -d

# Test API endpoints
curl http://localhost:5000/api/system/status
curl http://localhost:5000/api/servarr/status
```

### 9.3 Future: Unit Testing

Recommended structure:

```csharp
[Fact]
public async Task AnalyzeVideo_ValidFile_ReturnsCompatibilityResult()
{
    // Arrange
    var service = new VideoAnalyzerService(...);
    
    // Act
    var result = await service.AnalyzeVideoAsync("test.mp4");
    
    // Assert
    Assert.NotNull(result);
    Assert.True(result.OverallScore >= 0 && result.OverallScore <= 11);
}
```

---

## 10. Common Tasks

### 10.1 Adding a New API Endpoint

1. **Add method to Controller:**
```csharp
[HttpGet("new-endpoint")]
public async Task<ActionResult> NewEndpoint()
{
    // Implementation
}
```

2. **Add service method if needed:**
```csharp
public async Task<Result> NewServiceMethod()
{
    // Implementation
}
```

3. **Update frontend if needed:**
```javascript
async function callNewEndpoint() {
    const response = await fetch('/api/controller/new-endpoint');
    // Handle response
}
```

### 10.2 Adding a New Database Entity

1. **Create Model:**
```csharp
public class NewEntity
{
    public int Id { get; set; }
    public string Name { get; set; }
}
```

2. **Add to DbContext:**
```csharp
public DbSet<NewEntity> NewEntities { get; set; }
```

3. **Create Migration:**
```bash
dotnet ef migrations add AddNewEntity
```

4. **Apply Migration:**
Migration is applied automatically on startup.

### 10.3 Adding a New Service

1. **Create Service Class:**
```csharp
public class NewService
{
    private readonly ILogger<NewService> _logger;
    
    public NewService(ILogger<NewService> logger)
    {
        _logger = logger;
    }
}
```

2. **Register in Program.cs:**
```csharp
builder.Services.AddScoped<NewService>();
```

3. **Inject where needed:**
```csharp
public class SomeController
{
    private readonly NewService _newService;
    
    public SomeController(NewService newService)
    {
        _newService = newService;
    }
}
```

---

## 11. Debugging Tips

### 11.1 Common Issues

**MediaInfo not found:**
- Ensure MediaInfo CLI is installed and in PATH
- Check logs for MediaInfo availability on startup

**Database locked:**
- SQLite doesn't support concurrent writes well
- Ensure only one instance is running

**Path mapping not working:**
- Check configuration format in `appsettings.json`
- Verify paths match exactly (case-sensitive on Linux)

**API connection failures:**
- Verify base URLs are correct
- Check API keys are valid
- Ensure services are accessible from Optimarr container

### 11.2 Debugging Tools

- **Swagger UI:** Available at `/swagger` in development
- **Logs:** Check `logs/optimarr-YYYY-MM-DD.log`
- **Browser DevTools:** Network tab for API calls
- **Docker Logs:** `docker-compose logs -f optimarr`

---

## 12. Contributing Guidelines

### 12.1 Code Style

- Follow existing code patterns
- Use meaningful variable names
- Add XML comments for public APIs
- Keep methods focused and small

### 12.2 Commit Messages

Use clear, descriptive commit messages:

```
Add path mapping support for Sonarr/Radarr

- Added path mapping configuration in appsettings.json
- Implemented MapPath() method in ServarrSyncService
- Added UI for managing path mappings in settings
```

### 12.3 Pull Request Process

1. Create feature branch
2. Make changes with tests
3. Update documentation if needed
4. Submit PR with description
5. Address review feedback

### 12.4 Version Management and Story Tracking

**Version Tracking:**
- Version number is stored in `VERSION` file in project root
- Version is displayed in UI sidebar (loaded from `/api/system/version`)
- Version follows Semantic Versioning (MAJOR.MINOR.PATCH)

**Story Tracking:**
- All development stories are tracked in `docs/CHANGELOG.md` and `docs/STORY_TRACKING.md`
- Each story has a unique ID (STORY-XXX)
- Stories include: description, database changes, UI changes, API changes
- Migration scripts are created for database changes

**When Making Changes:**
1. **Check Database Impact:**
   - If adding/modifying models, create migration script
   - Test migration on sample database
   - Update `Data/Migrations/` with new script

2. **Update Version:**
   - Increment version in `VERSION` file
   - Update `docs/CHANGELOG.md` with new story entry
   - Update `docs/STORY_TRACKING.md` with story details
   - Include all changes: features, database, UI, API

3. **Update Documentation:**
   - Update PRD.md if requirements change
   - Update ARCHITECTURE.md if architecture changes
   - Update DEVELOPER_GUIDE.md if development process changes

**Example Story Entry:**
```markdown
## [1.2.0] - 2025-01-XX

### Added - Story: Feature Name
**Story ID:** STORY-002
**Developer:** Developer Name
**Date:** 2025-01-XX

#### Features
- Description of new features

#### Database Impact
- **Breaking Change:** Yes/No
- **Migration Required:** Yes/No
- **Migration Script:** `Data/Migrations/AddFeature.sql`
```

---

## Document History

| Version | Date | Author | Changes |
|---------|------|--------|---------|
| 1.0 | 2025-01-XX | AI Assistant | Initial developer guide following B-MAD methodology |
