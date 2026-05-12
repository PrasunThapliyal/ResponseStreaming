using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Net;

namespace ResponseStreaming.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class StreamController : ControllerBase
    {
        private readonly ILogger<StreamController> _logger;

        public StreamController(ILogger<StreamController> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// The request is a POST with no body. Response is streamed to UI. UI updates a label as progress is received.
        /// Status: Working
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        [HttpPost("streamResponse")]
        public async Task<IActionResult> StreamResponse(CancellationToken cancellationToken)
        {
            _logger.LogInformation($"StreamResponse: request received ..");

            Response.StatusCode = (int)HttpStatusCode.OK;
            Response.ContentType = "application/json";

            StreamWriter streamWriter;
            await using ((streamWriter = new StreamWriter(Response.Body)).ConfigureAwait(false)) 
            {
                for (int i = 0; i < 100; i++)
                {
                    await Task.Delay(100);

                    var uploadProgress = $"Uploaded {i+1} % \r\n";
                    _logger.LogInformation($"Upload progress to UI: {uploadProgress}");

                    await streamWriter.WriteLineAsync(uploadProgress).ConfigureAwait(false);
                    await streamWriter.FlushAsync().ConfigureAwait(false);
                }
            }

            return new EmptyResult();
        }
    }
}
