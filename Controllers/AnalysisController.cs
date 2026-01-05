using Microsoft.AspNetCore.Mvc;
using Optimarr.Models;
using Optimarr.Services;

namespace Optimarr.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AnalysisController : ControllerBase
    {
        private readonly VideoAnalyzerService _analyzerService;
        private readonly ILogger<AnalysisController> _logger;

        public AnalysisController(VideoAnalyzerService analyzerService, ILogger<AnalysisController> logger)
        {
            _analyzerService = analyzerService;
            _logger = logger;
        }

        [HttpPost("analyze")]
        public async Task<ActionResult<AnalysisResponse>> AnalyzeVideo([FromBody] AnalysisRequest request)
        {
            _logger.LogInformation("Analysis request received for video: {VideoPath}", request.VideoPath);
            
            try
            {
                if (string.IsNullOrEmpty(request.VideoPath) || !System.IO.File.Exists(request.VideoPath))
                {
                    _logger.LogWarning("Video file not found: {VideoPath}", request.VideoPath);
                    return BadRequest(new { error = "Video file not found" });
                }

                var startTime = DateTime.UtcNow;
                var report = await Task.Run(() => 
                    _analyzerService.AnalyzeVideo(request.VideoPath, request.SubtitlePath));
                var duration = (DateTime.UtcNow - startTime).TotalSeconds;

                _logger.LogInformation("Analysis completed successfully in {Duration:F2}s for: {VideoPath}", duration, request.VideoPath);

                return Ok(new AnalysisResponse
                {
                    Report = report,
                    Success = true
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analyzing video: {VideoPath}", request.VideoPath);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("analyze-file")]
        public async Task<ActionResult<AnalysisResponse>> AnalyzeUploadedFile(IFormFile videoFile, IFormFile? subtitleFile)
        {
            _logger.LogInformation("File upload analysis request received. Video: {FileName} ({Size} bytes), Subtitle: {SubtitleFileName}",
                videoFile?.FileName, videoFile?.Length, subtitleFile?.FileName ?? "None");
            
            try
            {
                if (videoFile == null || videoFile.Length == 0)
                {
                    _logger.LogWarning("No video file uploaded or file is empty");
                    return BadRequest(new { error = "No video file uploaded" });
                }

                // Save uploaded file temporarily
                var tempVideoPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + Path.GetExtension(videoFile.FileName));
                _logger.LogDebug("Saving uploaded video to temporary path: {TempPath}", tempVideoPath);
                
                using (var stream = new FileStream(tempVideoPath, FileMode.Create))
                {
                    await videoFile.CopyToAsync(stream);
                }

                string? tempSubtitlePath = null;
                if (subtitleFile != null && subtitleFile.Length > 0)
                {
                    tempSubtitlePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + Path.GetExtension(subtitleFile.FileName));
                    _logger.LogDebug("Saving uploaded subtitle to temporary path: {TempPath}", tempSubtitlePath);
                    
                    using (var stream = new FileStream(tempSubtitlePath, FileMode.Create))
                    {
                        await subtitleFile.CopyToAsync(stream);
                    }
                }

                try
                {
                    var startTime = DateTime.UtcNow;
                    var report = await Task.Run(() => 
                        _analyzerService.AnalyzeVideo(tempVideoPath, tempSubtitlePath));
                    var duration = (DateTime.UtcNow - startTime).TotalSeconds;

                    _logger.LogInformation("Uploaded file analysis completed successfully in {Duration:F2}s", duration);

                    return Ok(new AnalysisResponse
                    {
                        Report = report,
                        Success = true
                    });
                }
                finally
                {
                    // Cleanup temp files
                    try
                    {
                        if (System.IO.File.Exists(tempVideoPath))
                        {
                            System.IO.File.Delete(tempVideoPath);
                            _logger.LogDebug("Cleaned up temporary video file: {TempPath}", tempVideoPath);
                        }
                        if (tempSubtitlePath != null && System.IO.File.Exists(tempSubtitlePath))
                        {
                            System.IO.File.Delete(tempSubtitlePath);
                            _logger.LogDebug("Cleaned up temporary subtitle file: {TempPath}", tempSubtitlePath);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error cleaning up temporary files");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analyzing uploaded file: {FileName}", videoFile?.FileName);
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }

    public class AnalysisRequest
    {
        public string VideoPath { get; set; } = string.Empty;
        public string? SubtitlePath { get; set; }
    }

    public class AnalysisResponse
    {
        public string Report { get; set; } = string.Empty;
        public bool Success { get; set; }
    }
}

