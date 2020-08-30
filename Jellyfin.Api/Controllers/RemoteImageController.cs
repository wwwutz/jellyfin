using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Api.Constants;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Net;
using MediaBrowser.Model.Providers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Api.Controllers
{
    /// <summary>
    /// Remote Images Controller.
    /// </summary>
    [Route("")]
    public class RemoteImageController : BaseJellyfinApiController
    {
        private readonly IProviderManager _providerManager;
        private readonly IServerApplicationPaths _applicationPaths;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILibraryManager _libraryManager;

        /// <summary>
        /// Initializes a new instance of the <see cref="RemoteImageController"/> class.
        /// </summary>
        /// <param name="providerManager">Instance of the <see cref="IProviderManager"/> interface.</param>
        /// <param name="applicationPaths">Instance of the <see cref="IServerApplicationPaths"/> interface.</param>
        /// <param name="httpClientFactory">Instance of the <see cref="IHttpClientFactory"/> interface.</param>
        /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
        public RemoteImageController(
            IProviderManager providerManager,
            IServerApplicationPaths applicationPaths,
            IHttpClientFactory httpClientFactory,
            ILibraryManager libraryManager)
        {
            _providerManager = providerManager;
            _applicationPaths = applicationPaths;
            _httpClientFactory = httpClientFactory;
            _libraryManager = libraryManager;
        }

        /// <summary>
        /// Gets available remote images for an item.
        /// </summary>
        /// <param name="itemId">Item Id.</param>
        /// <param name="type">The image type.</param>
        /// <param name="startIndex">Optional. The record index to start at. All items with a lower index will be dropped from the results.</param>
        /// <param name="limit">Optional. The maximum number of records to return.</param>
        /// <param name="providerName">Optional. The image provider to use.</param>
        /// <param name="includeAllLanguages">Optional. Include all languages.</param>
        /// <response code="200">Remote Images returned.</response>
        /// <response code="404">Item not found.</response>
        /// <returns>Remote Image Result.</returns>
        [HttpGet("Items/{itemId}/RemoteImages")]
        [Authorize(Policy = Policies.DefaultAuthorization)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<RemoteImageResult>> GetRemoteImages(
            [FromRoute] Guid itemId,
            [FromQuery] ImageType? type,
            [FromQuery] int? startIndex,
            [FromQuery] int? limit,
            [FromQuery] string? providerName,
            [FromQuery] bool includeAllLanguages = false)
        {
            var item = _libraryManager.GetItemById(itemId);
            if (item == null)
            {
                return NotFound();
            }

            var images = await _providerManager.GetAvailableRemoteImages(
                    item,
                    new RemoteImageQuery(providerName ?? string.Empty)
                    {
                        IncludeAllLanguages = includeAllLanguages,
                        IncludeDisabledProviders = true,
                        ImageType = type
                    }, CancellationToken.None)
                .ConfigureAwait(false);

            var imageArray = images.ToArray();
            var allProviders = _providerManager.GetRemoteImageProviderInfo(item);
            if (type.HasValue)
            {
                allProviders = allProviders.Where(o => o.SupportedImages.Contains(type.Value));
            }

            var result = new RemoteImageResult
            {
                TotalRecordCount = imageArray.Length,
                Providers = allProviders.Select(o => o.Name)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            };

            if (startIndex.HasValue)
            {
                imageArray = imageArray.Skip(startIndex.Value).ToArray();
            }

            if (limit.HasValue)
            {
                imageArray = imageArray.Take(limit.Value).ToArray();
            }

            result.Images = imageArray;
            return result;
        }

        /// <summary>
        /// Gets available remote image providers for an item.
        /// </summary>
        /// <param name="itemId">Item Id.</param>
        /// <response code="200">Returned remote image providers.</response>
        /// <response code="404">Item not found.</response>
        /// <returns>List of remote image providers.</returns>
        [HttpGet("Items/{itemId}/RemoteImages/Providers")]
        [Authorize(Policy = Policies.DefaultAuthorization)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public ActionResult<IEnumerable<ImageProviderInfo>> GetRemoteImageProviders([FromRoute] Guid itemId)
        {
            var item = _libraryManager.GetItemById(itemId);
            if (item == null)
            {
                return NotFound();
            }

            return Ok(_providerManager.GetRemoteImageProviderInfo(item));
        }

        /// <summary>
        /// Gets a remote image.
        /// </summary>
        /// <param name="imageUrl">The image url.</param>
        /// <response code="200">Remote image returned.</response>
        /// <response code="404">Remote image not found.</response>
        /// <returns>Image Stream.</returns>
        [HttpGet("Images/Remote")]
        [Produces(MediaTypeNames.Application.Octet)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult> GetRemoteImage([FromQuery, Required] string imageUrl)
        {
            var urlHash = imageUrl.GetMD5();
            var pointerCachePath = GetFullCachePath(urlHash.ToString());

            string? contentPath = null;
            var hasFile = false;

            try
            {
                contentPath = await System.IO.File.ReadAllTextAsync(pointerCachePath).ConfigureAwait(false);
                if (System.IO.File.Exists(contentPath))
                {
                    hasFile = true;
                }
            }
            catch (FileNotFoundException)
            {
                // The file isn't cached yet
            }
            catch (IOException)
            {
                // The file isn't cached yet
            }

            if (!hasFile)
            {
                await DownloadImage(imageUrl, urlHash, pointerCachePath).ConfigureAwait(false);
                contentPath = await System.IO.File.ReadAllTextAsync(pointerCachePath).ConfigureAwait(false);
            }

            if (string.IsNullOrEmpty(contentPath))
            {
                return NotFound();
            }

            var contentType = MimeTypes.GetMimeType(contentPath);
            return File(System.IO.File.OpenRead(contentPath), contentType);
        }

        /// <summary>
        /// Downloads a remote image for an item.
        /// </summary>
        /// <param name="itemId">Item Id.</param>
        /// <param name="type">The image type.</param>
        /// <param name="imageUrl">The image url.</param>
        /// <response code="204">Remote image downloaded.</response>
        /// <response code="404">Remote image not found.</response>
        /// <returns>Download status.</returns>
        [HttpPost("Items/{itemId}/RemoteImages/Download")]
        [Authorize(Policy = Policies.RequiresElevation)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult> DownloadRemoteImage(
            [FromRoute] Guid itemId,
            [FromQuery, Required] ImageType type,
            [FromQuery] string? imageUrl)
        {
            var item = _libraryManager.GetItemById(itemId);
            if (item == null)
            {
                return NotFound();
            }

            await _providerManager.SaveImage(item, imageUrl, type, null, CancellationToken.None)
                .ConfigureAwait(false);

            await item.UpdateToRepositoryAsync(ItemUpdateType.ImageUpdate, CancellationToken.None).ConfigureAwait(false);
            return NoContent();
        }

        /// <summary>
        /// Gets the full cache path.
        /// </summary>
        /// <param name="filename">The filename.</param>
        /// <returns>System.String.</returns>
        private string GetFullCachePath(string filename)
        {
            return Path.Combine(_applicationPaths.CachePath, "remote-images", filename.Substring(0, 1), filename);
        }

        /// <summary>
        /// Downloads the image.
        /// </summary>
        /// <param name="url">The URL.</param>
        /// <param name="urlHash">The URL hash.</param>
        /// <param name="pointerCachePath">The pointer cache path.</param>
        /// <returns>Task.</returns>
        private async Task DownloadImage(string url, Guid urlHash, string pointerCachePath)
        {
            var httpClient = _httpClientFactory.CreateClient();
            using var response = await httpClient.GetAsync(url).ConfigureAwait(false);
            var ext = response.Content.Headers.ContentType.MediaType.Split('/').Last();
            var fullCachePath = GetFullCachePath(urlHash + "." + ext);

            Directory.CreateDirectory(Path.GetDirectoryName(fullCachePath));
            await using var fileStream = new FileStream(fullCachePath, FileMode.Create, FileAccess.Write, FileShare.Read, IODefaults.FileStreamBufferSize, true);
            await response.Content.CopyToAsync(fileStream).ConfigureAwait(false);
            Directory.CreateDirectory(Path.GetDirectoryName(pointerCachePath));
            await System.IO.File.WriteAllTextAsync(pointerCachePath, fullCachePath, CancellationToken.None)
                .ConfigureAwait(false);
        }
    }
}
