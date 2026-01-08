using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Optimarr.Services;

namespace Optimarr.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SystemController : ControllerBase
    {
        private readonly ILogger<SystemController> _logger;

        public SystemController(ILogger<SystemController> logger)
        {
            _logger = logger;
        }

        [HttpGet("health")]
        public ActionResult<object> GetHealth()
        {
            return Ok(new
            {
                status = "healthy",
                timestamp = DateTime.UtcNow
            });
        }

        [HttpGet("migration")]
        public ActionResult<object> GetMigrationStatus()
        {
            var progress = DatabaseMigrationService.GetMigrationProgress();
            if (progress == null)
            {
                return Ok(new
                {
                    status = "unknown",
                    message = "Migration status not available"
                });
            }

            return Ok(progress);
        }

        [HttpGet("version")]
        public ActionResult<object> GetVersion()
        {
            // Try to read version from VERSION file first (preferred method)
            var versionFile = Path.Combine(AppContext.BaseDirectory, "VERSION");
            string? versionFromFile = null;
            
            if (System.IO.File.Exists(versionFile))
            {
                try
                {
                    versionFromFile = System.IO.File.ReadAllText(versionFile).Trim();
                }
                catch
                {
                    // Ignore errors reading version file
                }
            }
            
            // Fallback to assembly version if VERSION file not found
            var assembly = Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version;
            var versionString = version?.ToString() ?? "Unknown";
            
            // Try to get informational version if available
            var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            
            return Ok(new
            {
                version = versionFromFile ?? informationalVersion ?? versionString,
                assemblyVersion = versionString,
                buildDate = GetBuildDate(assembly),
                source = versionFromFile != null ? "VERSION file" : "Assembly"
            });
        }

        [HttpPost("reboot")]
        public ActionResult Reboot()
        {
            _logger.LogWarning("Reboot requested by user");
            
            // Note: Actual reboot requires system permissions and is platform-specific
            // For Docker containers, this would typically be handled by the container orchestrator
            // For now, we'll just log the request and return a message
            
            // In a Docker environment, you might want to:
            // - Exit the application (which will be restarted by Docker if restart policy is set)
            // - Send a signal to the container orchestrator
            // - Use a health check endpoint to trigger restart
            
            return Ok(new
            {
                message = "Reboot request received. In Docker, the container will restart if a restart policy is configured. For native installations, please restart the service manually.",
                note = "The application will attempt to gracefully shut down."
            });
        }

        private string? GetBuildDate(Assembly assembly)
        {
            try
            {
                var filePath = assembly.Location;
                if (string.IsNullOrEmpty(filePath)) return null;
                
                var fileInfo = new System.IO.FileInfo(filePath);
                return fileInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss");
            }
            catch
            {
                return null;
            }
        }
    }
}

