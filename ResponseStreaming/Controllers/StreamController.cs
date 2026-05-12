using Microsoft.AspNetCore.DataProtection.KeyManagement;
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
        [HttpPost("api/v1/streamResponse")]
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


        /// <summary>
        /// In v2, the request is a POST with a file stream as body. We read the stream in chunks and write progress to the response stream as we go. UI updates a label as progress is received.
        /// However, the UI doesn't receive any progress updates until the entire file is uploaded, and then it receives all the progress updates at once. This is because the server is buffering the response until the entire request body is read before it starts sending the response. We need to ensure that we flush the response stream after writing each progress update, and also that we are not blocking on reading the request body which would prevent the response from being sent until it's fully read.
        /// To summarize, this example hasn't solved full duplex streaming where we can read the request body and write to the response simultaneously. The server is still waiting to read the entire request body before it starts sending the response, which is why the UI doesn't see progress updates until the upload is complete. We need to find a way to read from the request body in a non-blocking way while still being able to write to the response stream and flush it so that progress updates are sent to the client immediately. This may involve using asynchronous reading of the request body and ensuring that we flush the response stream after each write.
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        [HttpPost("api/v2/streamResponse")]
        public async Task<IActionResult> StreamResponseV2(CancellationToken cancellationToken)
        {
            _logger.LogInformation($"StreamResponseV2: request received ..");

            // Disable response buffering so progress updates are flushed immediately to the client
            // without waiting for the entire request body to be read first.
            var responseBodyFeature = HttpContext.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpResponseBodyFeature>();
            responseBodyFeature?.DisableBuffering();

            //const int PartSize = 5 * 1024 * 1024; // 5 MB

            const int PartSize = 64 * 1024; // 64 KB
            const int ReadBufferSize = 64 * 1024; // 64 KB

            var fileName = Request.Headers["X-File-Name"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(fileName))
                return BadRequest("Missing X-File-Name header.");

            var contentType = Request.Headers["X-File-Content-Type"].FirstOrDefault()
                              ?? "application/octet-stream";
            long.TryParse(Request.Headers["X-File-Size"].FirstOrDefault(), out var totalBytes);

            Response.StatusCode = (int)HttpStatusCode.OK;
            Response.ContentType = "application/json";


            var partBuffer = new byte[PartSize];
            var partBufferUsed = 0;
            var partNumber = 1; 
            long bytesReceived = 0;
            var readBuffer = new byte[ReadBufferSize];
            int bytesRead;

            StreamWriter streamWriter;
            await using ((streamWriter = new StreamWriter(Response.Body)).ConfigureAwait(false))
            {
                // We read 64 Kb from the request stream at a time, and copy it into our partBuffer until it's full (5 MB). Once full, we can process the part (e.g., upload to S3) and then reset for the next part.
                while ((bytesRead = await Request.Body.ReadAsync(readBuffer)) > 0)
                {
                    await Task.Delay(100);

                    // A Single read from the request stream may contain less than 64 Kb, depending on what's available in the stream; or 0 if its the end of stream
                    var src = 0;
                    while (src < bytesRead)
                    {
                        var toCopy = Math.Min(bytesRead - src, PartSize - partBufferUsed);
                        Buffer.BlockCopy(readBuffer, src, partBuffer, partBufferUsed, toCopy);
                        partBufferUsed += toCopy;
                        src += toCopy;
                        bytesReceived += toCopy;

                        if (partBufferUsed == PartSize)
                        {
                            //var etag = await UploadPartAsync(
                            //    partBuffer, partBufferUsed, partNumber, s3Key, s3UploadId, isLast: false);
                            //partETags.Add(new PartETag(partNumber++, etag));

                            // Our partBuffer is full, we can process it (e.g., upload to S3). For this example, we'll just log it.
                            _logger.LogInformation($"Processed part {partNumber++}, size: {PartSize} bytes");

                            partBufferUsed = 0;
                        }
                    }

                    var uploadProgress = $"Downloaded {bytesReceived} of {totalBytes} \r\n";
                    _logger.LogInformation($"Upload progress to UI: {uploadProgress}");

                    await streamWriter.WriteLineAsync(uploadProgress).ConfigureAwait(false);
                    await streamWriter.FlushAsync().ConfigureAwait(false);
                }

            }

            return new EmptyResult();
        }
    }
}
