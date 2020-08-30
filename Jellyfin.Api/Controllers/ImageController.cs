﻿using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Api.Constants;
using Jellyfin.Api.Helpers;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Drawing;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace Jellyfin.Api.Controllers
{
    /// <summary>
    /// Image controller.
    /// </summary>
    [Route("")]
    public class ImageController : BaseJellyfinApiController
    {
        private readonly IUserManager _userManager;
        private readonly ILibraryManager _libraryManager;
        private readonly IProviderManager _providerManager;
        private readonly IImageProcessor _imageProcessor;
        private readonly IFileSystem _fileSystem;
        private readonly IAuthorizationContext _authContext;
        private readonly ILogger<ImageController> _logger;
        private readonly IServerConfigurationManager _serverConfigurationManager;

        /// <summary>
        /// Initializes a new instance of the <see cref="ImageController"/> class.
        /// </summary>
        /// <param name="userManager">Instance of the <see cref="IUserManager"/> interface.</param>
        /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
        /// <param name="providerManager">Instance of the <see cref="IProviderManager"/> interface.</param>
        /// <param name="imageProcessor">Instance of the <see cref="IImageProcessor"/> interface.</param>
        /// <param name="fileSystem">Instance of the <see cref="IFileSystem"/> interface.</param>
        /// <param name="authContext">Instance of the <see cref="IAuthorizationContext"/> interface.</param>
        /// <param name="logger">Instance of the <see cref="ILogger{ImageController}"/> interface.</param>
        /// <param name="serverConfigurationManager">Instance of the <see cref="IServerConfigurationManager"/> interface.</param>
        public ImageController(
            IUserManager userManager,
            ILibraryManager libraryManager,
            IProviderManager providerManager,
            IImageProcessor imageProcessor,
            IFileSystem fileSystem,
            IAuthorizationContext authContext,
            ILogger<ImageController> logger,
            IServerConfigurationManager serverConfigurationManager)
        {
            _userManager = userManager;
            _libraryManager = libraryManager;
            _providerManager = providerManager;
            _imageProcessor = imageProcessor;
            _fileSystem = fileSystem;
            _authContext = authContext;
            _logger = logger;
            _serverConfigurationManager = serverConfigurationManager;
        }

        /// <summary>
        /// Sets the user image.
        /// </summary>
        /// <param name="userId">User Id.</param>
        /// <param name="imageType">(Unused) Image type.</param>
        /// <param name="index">(Unused) Image index.</param>
        /// <response code="204">Image updated.</response>
        /// <response code="403">User does not have permission to delete the image.</response>
        /// <returns>A <see cref="NoContentResult"/>.</returns>
        [HttpPost("Users/{userId}/Images/{imageType}")]
        [HttpPost("Users/{userId}/Images/{imageType}/{index?}", Name = "PostUserImage_2")]
        [Authorize(Policy = Policies.DefaultAuthorization)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [SuppressMessage("Microsoft.Performance", "CA1801:ReviewUnusedParameters", MessageId = "imageType", Justification = "Imported from ServiceStack")]
        [SuppressMessage("Microsoft.Performance", "CA1801:ReviewUnusedParameters", MessageId = "index", Justification = "Imported from ServiceStack")]
        public async Task<ActionResult> PostUserImage(
            [FromRoute] Guid userId,
            [FromRoute] ImageType imageType,
            [FromRoute] int? index = null)
        {
            if (!RequestHelpers.AssertCanUpdateUser(_authContext, HttpContext.Request, userId, true))
            {
                return Forbid("User is not allowed to update the image.");
            }

            var user = _userManager.GetUserById(userId);
            await using var memoryStream = await GetMemoryStream(Request.Body).ConfigureAwait(false);

            // Handle image/png; charset=utf-8
            var mimeType = Request.ContentType.Split(';').FirstOrDefault();
            var userDataPath = Path.Combine(_serverConfigurationManager.ApplicationPaths.UserConfigurationDirectoryPath, user.Username);
            if (user.ProfileImage != null)
            {
                _userManager.ClearProfileImage(user);
            }

            user.ProfileImage = new Data.Entities.ImageInfo(Path.Combine(userDataPath, "profile" + MimeTypes.ToExtension(mimeType)));

            await _providerManager
                .SaveImage(memoryStream, mimeType, user.ProfileImage.Path)
                .ConfigureAwait(false);
            await _userManager.UpdateUserAsync(user).ConfigureAwait(false);

            return NoContent();
        }

        /// <summary>
        /// Delete the user's image.
        /// </summary>
        /// <param name="userId">User Id.</param>
        /// <param name="imageType">(Unused) Image type.</param>
        /// <param name="index">(Unused) Image index.</param>
        /// <response code="204">Image deleted.</response>
        /// <response code="403">User does not have permission to delete the image.</response>
        /// <returns>A <see cref="NoContentResult"/>.</returns>
        [HttpDelete("Users/{userId}/Images/{itemType}")]
        [HttpDelete("Users/{userId}/Images/{itemType}/{index?}", Name = "DeleteUserImage_2")]
        [Authorize(Policy = Policies.DefaultAuthorization)]
        [SuppressMessage("Microsoft.Performance", "CA1801:ReviewUnusedParameters", MessageId = "imageType", Justification = "Imported from ServiceStack")]
        [SuppressMessage("Microsoft.Performance", "CA1801:ReviewUnusedParameters", MessageId = "index", Justification = "Imported from ServiceStack")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public ActionResult DeleteUserImage(
            [FromRoute] Guid userId,
            [FromRoute] ImageType imageType,
            [FromRoute] int? index = null)
        {
            if (!RequestHelpers.AssertCanUpdateUser(_authContext, HttpContext.Request, userId, true))
            {
                return Forbid("User is not allowed to delete the image.");
            }

            var user = _userManager.GetUserById(userId);
            try
            {
                System.IO.File.Delete(user.ProfileImage.Path);
            }
            catch (IOException e)
            {
                _logger.LogError(e, "Error deleting user profile image:");
            }

            _userManager.ClearProfileImage(user);
            return NoContent();
        }

        /// <summary>
        /// Delete an item's image.
        /// </summary>
        /// <param name="itemId">Item id.</param>
        /// <param name="imageType">Image type.</param>
        /// <param name="imageIndex">The image index.</param>
        /// <response code="204">Image deleted.</response>
        /// <response code="404">Item not found.</response>
        /// <returns>A <see cref="NoContentResult"/> on success, or a <see cref="NotFoundResult"/> if item not found.</returns>
        [HttpDelete("Items/{itemId}/Images/{imageType}")]
        [HttpDelete("Items/{itemId}/Images/{imageType}/{imageIndex?}", Name = "DeleteItemImage_2")]
        [Authorize(Policy = Policies.RequiresElevation)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult> DeleteItemImage(
            [FromRoute] Guid itemId,
            [FromRoute] ImageType imageType,
            [FromRoute] int? imageIndex = null)
        {
            var item = _libraryManager.GetItemById(itemId);
            if (item == null)
            {
                return NotFound();
            }

            await item.DeleteImageAsync(imageType, imageIndex ?? 0).ConfigureAwait(false);
            return NoContent();
        }

        /// <summary>
        /// Set item image.
        /// </summary>
        /// <param name="itemId">Item id.</param>
        /// <param name="imageType">Image type.</param>
        /// <param name="imageIndex">(Unused) Image index.</param>
        /// <response code="204">Image saved.</response>
        /// <response code="404">Item not found.</response>
        /// <returns>A <see cref="NoContentResult"/> on success, or a <see cref="NotFoundResult"/> if item not found.</returns>
        [HttpPost("Items/{itemId}/Images/{imageType}")]
        [HttpPost("Items/{itemId}/Images/{imageType}/{imageIndex?}", Name = "SetItemImage_2")]
        [Authorize(Policy = Policies.RequiresElevation)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [SuppressMessage("Microsoft.Performance", "CA1801:ReviewUnusedParameters", MessageId = "index", Justification = "Imported from ServiceStack")]
        public async Task<ActionResult> SetItemImage(
            [FromRoute] Guid itemId,
            [FromRoute] ImageType imageType,
            [FromRoute] int? imageIndex = null)
        {
            var item = _libraryManager.GetItemById(itemId);
            if (item == null)
            {
                return NotFound();
            }

            // Handle image/png; charset=utf-8
            var mimeType = Request.ContentType.Split(';').FirstOrDefault();
            await _providerManager.SaveImage(item, Request.Body, mimeType, imageType, null, CancellationToken.None).ConfigureAwait(false);
            await item.UpdateToRepositoryAsync(ItemUpdateType.ImageUpdate, CancellationToken.None).ConfigureAwait(false);

            return NoContent();
        }

        /// <summary>
        /// Updates the index for an item image.
        /// </summary>
        /// <param name="itemId">Item id.</param>
        /// <param name="imageType">Image type.</param>
        /// <param name="imageIndex">Old image index.</param>
        /// <param name="newIndex">New image index.</param>
        /// <response code="204">Image index updated.</response>
        /// <response code="404">Item not found.</response>
        /// <returns>A <see cref="NoContentResult"/> on success, or a <see cref="NotFoundResult"/> if item not found.</returns>
        [HttpPost("Items/{itemId}/Images/{imageType}/{imageIndex}/Index")]
        [Authorize(Policy = Policies.RequiresElevation)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult> UpdateItemImageIndex(
            [FromRoute] Guid itemId,
            [FromRoute] ImageType imageType,
            [FromRoute] int imageIndex,
            [FromQuery] int newIndex)
        {
            var item = _libraryManager.GetItemById(itemId);
            if (item == null)
            {
                return NotFound();
            }

            await item.SwapImagesAsync(imageType, imageIndex, newIndex).ConfigureAwait(false);
            return NoContent();
        }

        /// <summary>
        /// Get item image infos.
        /// </summary>
        /// <param name="itemId">Item id.</param>
        /// <response code="200">Item images returned.</response>
        /// <response code="404">Item not found.</response>
        /// <returns>The list of image infos on success, or <see cref="NotFoundResult"/> if item not found.</returns>
        [HttpGet("Items/{itemId}/Images")]
        [Authorize(Policy = Policies.DefaultAuthorization)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<IEnumerable<ImageInfo>>> GetItemImageInfos([FromRoute] Guid itemId)
        {
            var item = _libraryManager.GetItemById(itemId);
            if (item == null)
            {
                return NotFound();
            }

            var list = new List<ImageInfo>();
            var itemImages = item.ImageInfos;

            if (itemImages.Length == 0)
            {
                // short-circuit
                return list;
            }

            await _libraryManager.UpdateImagesAsync(item).ConfigureAwait(false); // this makes sure dimensions and hashes are correct

            foreach (var image in itemImages)
            {
                if (!item.AllowsMultipleImages(image.Type))
                {
                    var info = GetImageInfo(item, image, null);

                    if (info != null)
                    {
                        list.Add(info);
                    }
                }
            }

            foreach (var imageType in itemImages.Select(i => i.Type).Distinct().Where(item.AllowsMultipleImages))
            {
                var index = 0;

                // Prevent implicitly captured closure
                var currentImageType = imageType;

                foreach (var image in itemImages.Where(i => i.Type == currentImageType))
                {
                    var info = GetImageInfo(item, image, index);

                    if (info != null)
                    {
                        list.Add(info);
                    }

                    index++;
                }
            }

            return list;
        }

        /// <summary>
        /// Gets the item's image.
        /// </summary>
        /// <param name="itemId">Item id.</param>
        /// <param name="imageType">Image type.</param>
        /// <param name="maxWidth">The maximum image width to return.</param>
        /// <param name="maxHeight">The maximum image height to return.</param>
        /// <param name="width">The fixed image width to return.</param>
        /// <param name="height">The fixed image height to return.</param>
        /// <param name="quality">Optional. Quality setting, from 0-100. Defaults to 90 and should suffice in most cases.</param>
        /// <param name="tag">Optional. Supply the cache tag from the item object to receive strong caching headers.</param>
        /// <param name="cropWhitespace">Optional. Specify if whitespace should be cropped out of the image. True/False. If unspecified, whitespace will be cropped from logos and clear art.</param>
        /// <param name="format">Determines the output format of the image - original,gif,jpg,png.</param>
        /// <param name="addPlayedIndicator">Optional. Add a played indicator.</param>
        /// <param name="percentPlayed">Optional. Percent to render for the percent played overlay.</param>
        /// <param name="unplayedCount">Optional. Unplayed count overlay to render.</param>
        /// <param name="blur">Optional. Blur image.</param>
        /// <param name="backgroundColor">Optional. Apply a background color for transparent images.</param>
        /// <param name="foregroundLayer">Optional. Apply a foreground layer on top of the image.</param>
        /// <param name="imageIndex">Image index.</param>
        /// <response code="200">Image stream returned.</response>
        /// <response code="404">Item not found.</response>
        /// <returns>
        /// A <see cref="FileStreamResult"/> containing the file stream on success,
        /// or a <see cref="NotFoundResult"/> if item not found.
        /// </returns>
        [HttpGet("Items/{itemId}/Images/{imageType}")]
        [HttpHead("Items/{itemId}/Images/{imageType}", Name = "HeadItemImage")]
        [HttpGet("Items/{itemId}/Images/{imageType}/{imageIndex?}", Name = "GetItemImage_2")]
        [HttpHead("Items/{itemId}/Images/{imageType}/{imageIndex?}", Name = "HeadItemImage_2")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult> GetItemImage(
            [FromRoute] Guid itemId,
            [FromRoute] ImageType imageType,
            [FromRoute] int? maxWidth,
            [FromRoute] int? maxHeight,
            [FromQuery] int? width,
            [FromQuery] int? height,
            [FromQuery] int? quality,
            [FromQuery] string? tag,
            [FromQuery] bool? cropWhitespace,
            [FromQuery] string? format,
            [FromQuery] bool? addPlayedIndicator,
            [FromQuery] double? percentPlayed,
            [FromQuery] int? unplayedCount,
            [FromQuery] int? blur,
            [FromQuery] string? backgroundColor,
            [FromQuery] string? foregroundLayer,
            [FromRoute] int? imageIndex = null)
        {
            var item = _libraryManager.GetItemById(itemId);
            if (item == null)
            {
                return NotFound();
            }

            return await GetImageInternal(
                    itemId,
                    imageType,
                    imageIndex,
                    tag,
                    format,
                    maxWidth,
                    maxHeight,
                    percentPlayed,
                    unplayedCount,
                    width,
                    height,
                    quality,
                    cropWhitespace,
                    addPlayedIndicator,
                    blur,
                    backgroundColor,
                    foregroundLayer,
                    item,
                    Request.Method.Equals(HttpMethods.Head, StringComparison.OrdinalIgnoreCase))
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Gets the item's image.
        /// </summary>
        /// <param name="itemId">Item id.</param>
        /// <param name="imageType">Image type.</param>
        /// <param name="maxWidth">The maximum image width to return.</param>
        /// <param name="maxHeight">The maximum image height to return.</param>
        /// <param name="width">The fixed image width to return.</param>
        /// <param name="height">The fixed image height to return.</param>
        /// <param name="quality">Optional. Quality setting, from 0-100. Defaults to 90 and should suffice in most cases.</param>
        /// <param name="tag">Optional. Supply the cache tag from the item object to receive strong caching headers.</param>
        /// <param name="cropWhitespace">Optional. Specify if whitespace should be cropped out of the image. True/False. If unspecified, whitespace will be cropped from logos and clear art.</param>
        /// <param name="format">Determines the output format of the image - original,gif,jpg,png.</param>
        /// <param name="addPlayedIndicator">Optional. Add a played indicator.</param>
        /// <param name="percentPlayed">Optional. Percent to render for the percent played overlay.</param>
        /// <param name="unplayedCount">Optional. Unplayed count overlay to render.</param>
        /// <param name="blur">Optional. Blur image.</param>
        /// <param name="backgroundColor">Optional. Apply a background color for transparent images.</param>
        /// <param name="foregroundLayer">Optional. Apply a foreground layer on top of the image.</param>
        /// <param name="imageIndex">Image index.</param>
        /// <response code="200">Image stream returned.</response>
        /// <response code="404">Item not found.</response>
        /// <returns>
        /// A <see cref="FileStreamResult"/> containing the file stream on success,
        /// or a <see cref="NotFoundResult"/> if item not found.
        /// </returns>
        [HttpGet("Items/{itemId}/Images/{imageType}/{imageIndex}/{tag}/{format}/{maxWidth}/{maxHeight}/{percentPlayed}/{unplayedCount}")]
        [HttpHead("Items/{itemId}/Images/{imageType}/{imageIndex}/{tag}/{format}/{maxWidth}/{maxHeight}/{percentPlayed}/{unplayedCount}", Name = "HeadItemImage2")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult> GetItemImage2(
            [FromRoute] Guid itemId,
            [FromRoute] ImageType imageType,
            [FromRoute] int? maxWidth,
            [FromRoute] int? maxHeight,
            [FromQuery] int? width,
            [FromQuery] int? height,
            [FromQuery] int? quality,
            [FromRoute] string tag,
            [FromQuery] bool? cropWhitespace,
            [FromRoute] string format,
            [FromQuery] bool? addPlayedIndicator,
            [FromRoute] double? percentPlayed,
            [FromRoute] int? unplayedCount,
            [FromQuery] int? blur,
            [FromQuery] string? backgroundColor,
            [FromQuery] string? foregroundLayer,
            [FromRoute] int? imageIndex = null)
        {
            var item = _libraryManager.GetItemById(itemId);
            if (item == null)
            {
                return NotFound();
            }

            return await GetImageInternal(
                    itemId,
                    imageType,
                    imageIndex,
                    tag,
                    format,
                    maxWidth,
                    maxHeight,
                    percentPlayed,
                    unplayedCount,
                    width,
                    height,
                    quality,
                    cropWhitespace,
                    addPlayedIndicator,
                    blur,
                    backgroundColor,
                    foregroundLayer,
                    item,
                    Request.Method.Equals(HttpMethods.Head, StringComparison.OrdinalIgnoreCase))
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Get artist image by name.
        /// </summary>
        /// <param name="name">Artist name.</param>
        /// <param name="imageType">Image type.</param>
        /// <param name="tag">Optional. Supply the cache tag from the item object to receive strong caching headers.</param>
        /// <param name="format">Determines the output format of the image - original,gif,jpg,png.</param>
        /// <param name="maxWidth">The maximum image width to return.</param>
        /// <param name="maxHeight">The maximum image height to return.</param>
        /// <param name="percentPlayed">Optional. Percent to render for the percent played overlay.</param>
        /// <param name="unplayedCount">Optional. Unplayed count overlay to render.</param>
        /// <param name="width">The fixed image width to return.</param>
        /// <param name="height">The fixed image height to return.</param>
        /// <param name="quality">Optional. Quality setting, from 0-100. Defaults to 90 and should suffice in most cases.</param>
        /// <param name="cropWhitespace">Optional. Specify if whitespace should be cropped out of the image. True/False. If unspecified, whitespace will be cropped from logos and clear art.</param>
        /// <param name="addPlayedIndicator">Optional. Add a played indicator.</param>
        /// <param name="blur">Optional. Blur image.</param>
        /// <param name="backgroundColor">Optional. Apply a background color for transparent images.</param>
        /// <param name="foregroundLayer">Optional. Apply a foreground layer on top of the image.</param>
        /// <param name="imageIndex">Image index.</param>
        /// <response code="200">Image stream returned.</response>
        /// <response code="404">Item not found.</response>
        /// <returns>
        /// A <see cref="FileStreamResult"/> containing the file stream on success,
        /// or a <see cref="NotFoundResult"/> if item not found.
        /// </returns>
        [HttpGet("Artists/{name}/Images/{imageType}/{imageIndex?}")]
        [HttpHead("Artists/{name}/Images/{imageType}/{imageIndex?}", Name = "HeadArtistImage")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult> GetArtistImage(
            [FromRoute] string name,
            [FromRoute] ImageType imageType,
            [FromRoute] string tag,
            [FromRoute] string format,
            [FromRoute] int? maxWidth,
            [FromRoute] int? maxHeight,
            [FromRoute] double? percentPlayed,
            [FromRoute] int? unplayedCount,
            [FromQuery] int? width,
            [FromQuery] int? height,
            [FromQuery] int? quality,
            [FromQuery] bool? cropWhitespace,
            [FromQuery] bool? addPlayedIndicator,
            [FromQuery] int? blur,
            [FromQuery] string? backgroundColor,
            [FromQuery] string? foregroundLayer,
            [FromRoute] int? imageIndex = null)
        {
            var item = _libraryManager.GetArtist(name);
            if (item == null)
            {
                return NotFound();
            }

            return await GetImageInternal(
                    item.Id,
                    imageType,
                    imageIndex,
                    tag,
                    format,
                    maxWidth,
                    maxHeight,
                    percentPlayed,
                    unplayedCount,
                    width,
                    height,
                    quality,
                    cropWhitespace,
                    addPlayedIndicator,
                    blur,
                    backgroundColor,
                    foregroundLayer,
                    item,
                    Request.Method.Equals(HttpMethods.Head, StringComparison.OrdinalIgnoreCase))
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Get genre image by name.
        /// </summary>
        /// <param name="name">Genre name.</param>
        /// <param name="imageType">Image type.</param>
        /// <param name="tag">Optional. Supply the cache tag from the item object to receive strong caching headers.</param>
        /// <param name="format">Determines the output format of the image - original,gif,jpg,png.</param>
        /// <param name="maxWidth">The maximum image width to return.</param>
        /// <param name="maxHeight">The maximum image height to return.</param>
        /// <param name="percentPlayed">Optional. Percent to render for the percent played overlay.</param>
        /// <param name="unplayedCount">Optional. Unplayed count overlay to render.</param>
        /// <param name="width">The fixed image width to return.</param>
        /// <param name="height">The fixed image height to return.</param>
        /// <param name="quality">Optional. Quality setting, from 0-100. Defaults to 90 and should suffice in most cases.</param>
        /// <param name="cropWhitespace">Optional. Specify if whitespace should be cropped out of the image. True/False. If unspecified, whitespace will be cropped from logos and clear art.</param>
        /// <param name="addPlayedIndicator">Optional. Add a played indicator.</param>
        /// <param name="blur">Optional. Blur image.</param>
        /// <param name="backgroundColor">Optional. Apply a background color for transparent images.</param>
        /// <param name="foregroundLayer">Optional. Apply a foreground layer on top of the image.</param>
        /// <param name="imageIndex">Image index.</param>
        /// <response code="200">Image stream returned.</response>
        /// <response code="404">Item not found.</response>
        /// <returns>
        /// A <see cref="FileStreamResult"/> containing the file stream on success,
        /// or a <see cref="NotFoundResult"/> if item not found.
        /// </returns>
        [HttpGet("Genres/{name}/Images/{imageType}/{imageIndex?}")]
        [HttpHead("Genres/{name}/Images/{imageType}/{imageIndex?}", Name = "HeadGenreImage")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult> GetGenreImage(
            [FromRoute] string name,
            [FromRoute] ImageType imageType,
            [FromRoute] string tag,
            [FromRoute] string format,
            [FromRoute] int? maxWidth,
            [FromRoute] int? maxHeight,
            [FromRoute] double? percentPlayed,
            [FromRoute] int? unplayedCount,
            [FromQuery] int? width,
            [FromQuery] int? height,
            [FromQuery] int? quality,
            [FromQuery] bool? cropWhitespace,
            [FromQuery] bool? addPlayedIndicator,
            [FromQuery] int? blur,
            [FromQuery] string? backgroundColor,
            [FromQuery] string? foregroundLayer,
            [FromRoute] int? imageIndex = null)
        {
            var item = _libraryManager.GetGenre(name);
            if (item == null)
            {
                return NotFound();
            }

            return await GetImageInternal(
                    item.Id,
                    imageType,
                    imageIndex,
                    tag,
                    format,
                    maxWidth,
                    maxHeight,
                    percentPlayed,
                    unplayedCount,
                    width,
                    height,
                    quality,
                    cropWhitespace,
                    addPlayedIndicator,
                    blur,
                    backgroundColor,
                    foregroundLayer,
                    item,
                    Request.Method.Equals(HttpMethods.Head, StringComparison.OrdinalIgnoreCase))
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Get music genre image by name.
        /// </summary>
        /// <param name="name">Music genre name.</param>
        /// <param name="imageType">Image type.</param>
        /// <param name="tag">Optional. Supply the cache tag from the item object to receive strong caching headers.</param>
        /// <param name="format">Determines the output format of the image - original,gif,jpg,png.</param>
        /// <param name="maxWidth">The maximum image width to return.</param>
        /// <param name="maxHeight">The maximum image height to return.</param>
        /// <param name="percentPlayed">Optional. Percent to render for the percent played overlay.</param>
        /// <param name="unplayedCount">Optional. Unplayed count overlay to render.</param>
        /// <param name="width">The fixed image width to return.</param>
        /// <param name="height">The fixed image height to return.</param>
        /// <param name="quality">Optional. Quality setting, from 0-100. Defaults to 90 and should suffice in most cases.</param>
        /// <param name="cropWhitespace">Optional. Specify if whitespace should be cropped out of the image. True/False. If unspecified, whitespace will be cropped from logos and clear art.</param>
        /// <param name="addPlayedIndicator">Optional. Add a played indicator.</param>
        /// <param name="blur">Optional. Blur image.</param>
        /// <param name="backgroundColor">Optional. Apply a background color for transparent images.</param>
        /// <param name="foregroundLayer">Optional. Apply a foreground layer on top of the image.</param>
        /// <param name="imageIndex">Image index.</param>
        /// <response code="200">Image stream returned.</response>
        /// <response code="404">Item not found.</response>
        /// <returns>
        /// A <see cref="FileStreamResult"/> containing the file stream on success,
        /// or a <see cref="NotFoundResult"/> if item not found.
        /// </returns>
        [HttpGet("MusicGenres/{name}/Images/{imageType}/{imageIndex?}")]
        [HttpHead("MusicGenres/{name}/Images/{imageType}/{imageIndex?}", Name = "HeadMusicGenreImage")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult> GetMusicGenreImage(
            [FromRoute] string name,
            [FromRoute] ImageType imageType,
            [FromRoute] string tag,
            [FromRoute] string format,
            [FromRoute] int? maxWidth,
            [FromRoute] int? maxHeight,
            [FromRoute] double? percentPlayed,
            [FromRoute] int? unplayedCount,
            [FromQuery] int? width,
            [FromQuery] int? height,
            [FromQuery] int? quality,
            [FromQuery] bool? cropWhitespace,
            [FromQuery] bool? addPlayedIndicator,
            [FromQuery] int? blur,
            [FromQuery] string? backgroundColor,
            [FromQuery] string? foregroundLayer,
            [FromRoute] int? imageIndex = null)
        {
            var item = _libraryManager.GetMusicGenre(name);
            if (item == null)
            {
                return NotFound();
            }

            return await GetImageInternal(
                    item.Id,
                    imageType,
                    imageIndex,
                    tag,
                    format,
                    maxWidth,
                    maxHeight,
                    percentPlayed,
                    unplayedCount,
                    width,
                    height,
                    quality,
                    cropWhitespace,
                    addPlayedIndicator,
                    blur,
                    backgroundColor,
                    foregroundLayer,
                    item,
                    Request.Method.Equals(HttpMethods.Head, StringComparison.OrdinalIgnoreCase))
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Get person image by name.
        /// </summary>
        /// <param name="name">Person name.</param>
        /// <param name="imageType">Image type.</param>
        /// <param name="tag">Optional. Supply the cache tag from the item object to receive strong caching headers.</param>
        /// <param name="format">Determines the output format of the image - original,gif,jpg,png.</param>
        /// <param name="maxWidth">The maximum image width to return.</param>
        /// <param name="maxHeight">The maximum image height to return.</param>
        /// <param name="percentPlayed">Optional. Percent to render for the percent played overlay.</param>
        /// <param name="unplayedCount">Optional. Unplayed count overlay to render.</param>
        /// <param name="width">The fixed image width to return.</param>
        /// <param name="height">The fixed image height to return.</param>
        /// <param name="quality">Optional. Quality setting, from 0-100. Defaults to 90 and should suffice in most cases.</param>
        /// <param name="cropWhitespace">Optional. Specify if whitespace should be cropped out of the image. True/False. If unspecified, whitespace will be cropped from logos and clear art.</param>
        /// <param name="addPlayedIndicator">Optional. Add a played indicator.</param>
        /// <param name="blur">Optional. Blur image.</param>
        /// <param name="backgroundColor">Optional. Apply a background color for transparent images.</param>
        /// <param name="foregroundLayer">Optional. Apply a foreground layer on top of the image.</param>
        /// <param name="imageIndex">Image index.</param>
        /// <response code="200">Image stream returned.</response>
        /// <response code="404">Item not found.</response>
        /// <returns>
        /// A <see cref="FileStreamResult"/> containing the file stream on success,
        /// or a <see cref="NotFoundResult"/> if item not found.
        /// </returns>
        [HttpGet("Persons/{name}/Images/{imageType}/{imageIndex?}")]
        [HttpHead("Persons/{name}/Images/{imageType}/{imageIndex?}", Name = "HeadPersonImage")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult> GetPersonImage(
            [FromRoute] string name,
            [FromRoute] ImageType imageType,
            [FromRoute] string tag,
            [FromRoute] string format,
            [FromRoute] int? maxWidth,
            [FromRoute] int? maxHeight,
            [FromRoute] double? percentPlayed,
            [FromRoute] int? unplayedCount,
            [FromQuery] int? width,
            [FromQuery] int? height,
            [FromQuery] int? quality,
            [FromQuery] bool? cropWhitespace,
            [FromQuery] bool? addPlayedIndicator,
            [FromQuery] int? blur,
            [FromQuery] string? backgroundColor,
            [FromQuery] string? foregroundLayer,
            [FromRoute] int? imageIndex = null)
        {
            var item = _libraryManager.GetPerson(name);
            if (item == null)
            {
                return NotFound();
            }

            return await GetImageInternal(
                    item.Id,
                    imageType,
                    imageIndex,
                    tag,
                    format,
                    maxWidth,
                    maxHeight,
                    percentPlayed,
                    unplayedCount,
                    width,
                    height,
                    quality,
                    cropWhitespace,
                    addPlayedIndicator,
                    blur,
                    backgroundColor,
                    foregroundLayer,
                    item,
                    Request.Method.Equals(HttpMethods.Head, StringComparison.OrdinalIgnoreCase))
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Get studio image by name.
        /// </summary>
        /// <param name="name">Studio name.</param>
        /// <param name="imageType">Image type.</param>
        /// <param name="tag">Optional. Supply the cache tag from the item object to receive strong caching headers.</param>
        /// <param name="format">Determines the output format of the image - original,gif,jpg,png.</param>
        /// <param name="maxWidth">The maximum image width to return.</param>
        /// <param name="maxHeight">The maximum image height to return.</param>
        /// <param name="percentPlayed">Optional. Percent to render for the percent played overlay.</param>
        /// <param name="unplayedCount">Optional. Unplayed count overlay to render.</param>
        /// <param name="width">The fixed image width to return.</param>
        /// <param name="height">The fixed image height to return.</param>
        /// <param name="quality">Optional. Quality setting, from 0-100. Defaults to 90 and should suffice in most cases.</param>
        /// <param name="cropWhitespace">Optional. Specify if whitespace should be cropped out of the image. True/False. If unspecified, whitespace will be cropped from logos and clear art.</param>
        /// <param name="addPlayedIndicator">Optional. Add a played indicator.</param>
        /// <param name="blur">Optional. Blur image.</param>
        /// <param name="backgroundColor">Optional. Apply a background color for transparent images.</param>
        /// <param name="foregroundLayer">Optional. Apply a foreground layer on top of the image.</param>
        /// <param name="imageIndex">Image index.</param>
        /// <response code="200">Image stream returned.</response>
        /// <response code="404">Item not found.</response>
        /// <returns>
        /// A <see cref="FileStreamResult"/> containing the file stream on success,
        /// or a <see cref="NotFoundResult"/> if item not found.
        /// </returns>
        [HttpGet("Studios/{name}/Images/{imageType}/{imageIndex?}")]
        [HttpHead("Studios/{name}/Images/{imageType}/{imageIndex?}", Name = "HeadStudioImage")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult> GetStudioImage(
            [FromRoute] string name,
            [FromRoute] ImageType imageType,
            [FromRoute] string tag,
            [FromRoute] string format,
            [FromRoute] int? maxWidth,
            [FromRoute] int? maxHeight,
            [FromRoute] double? percentPlayed,
            [FromRoute] int? unplayedCount,
            [FromQuery] int? width,
            [FromQuery] int? height,
            [FromQuery] int? quality,
            [FromQuery] bool? cropWhitespace,
            [FromQuery] bool? addPlayedIndicator,
            [FromQuery] int? blur,
            [FromQuery] string? backgroundColor,
            [FromQuery] string? foregroundLayer,
            [FromRoute] int? imageIndex = null)
        {
            var item = _libraryManager.GetStudio(name);
            if (item == null)
            {
                return NotFound();
            }

            return await GetImageInternal(
                    item.Id,
                    imageType,
                    imageIndex,
                    tag,
                    format,
                    maxWidth,
                    maxHeight,
                    percentPlayed,
                    unplayedCount,
                    width,
                    height,
                    quality,
                    cropWhitespace,
                    addPlayedIndicator,
                    blur,
                    backgroundColor,
                    foregroundLayer,
                    item,
                    Request.Method.Equals(HttpMethods.Head, StringComparison.OrdinalIgnoreCase))
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Get user profile image.
        /// </summary>
        /// <param name="userId">User id.</param>
        /// <param name="imageType">Image type.</param>
        /// <param name="tag">Optional. Supply the cache tag from the item object to receive strong caching headers.</param>
        /// <param name="format">Determines the output format of the image - original,gif,jpg,png.</param>
        /// <param name="maxWidth">The maximum image width to return.</param>
        /// <param name="maxHeight">The maximum image height to return.</param>
        /// <param name="percentPlayed">Optional. Percent to render for the percent played overlay.</param>
        /// <param name="unplayedCount">Optional. Unplayed count overlay to render.</param>
        /// <param name="width">The fixed image width to return.</param>
        /// <param name="height">The fixed image height to return.</param>
        /// <param name="quality">Optional. Quality setting, from 0-100. Defaults to 90 and should suffice in most cases.</param>
        /// <param name="cropWhitespace">Optional. Specify if whitespace should be cropped out of the image. True/False. If unspecified, whitespace will be cropped from logos and clear art.</param>
        /// <param name="addPlayedIndicator">Optional. Add a played indicator.</param>
        /// <param name="blur">Optional. Blur image.</param>
        /// <param name="backgroundColor">Optional. Apply a background color for transparent images.</param>
        /// <param name="foregroundLayer">Optional. Apply a foreground layer on top of the image.</param>
        /// <param name="imageIndex">Image index.</param>
        /// <response code="200">Image stream returned.</response>
        /// <response code="404">Item not found.</response>
        /// <returns>
        /// A <see cref="FileStreamResult"/> containing the file stream on success,
        /// or a <see cref="NotFoundResult"/> if item not found.
        /// </returns>
        [HttpGet("Users/{userId}/Images/{imageType}/{imageIndex?}")]
        [HttpHead("Users/{userId}/Images/{imageType}/{imageIndex?}", Name = "HeadUserImage")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult> GetUserImage(
            [FromRoute] Guid userId,
            [FromRoute] ImageType imageType,
            [FromQuery] string? tag,
            [FromQuery] string? format,
            [FromQuery] int? maxWidth,
            [FromQuery] int? maxHeight,
            [FromQuery] double? percentPlayed,
            [FromQuery] int? unplayedCount,
            [FromQuery] int? width,
            [FromQuery] int? height,
            [FromQuery] int? quality,
            [FromQuery] bool? cropWhitespace,
            [FromQuery] bool? addPlayedIndicator,
            [FromQuery] int? blur,
            [FromQuery] string? backgroundColor,
            [FromQuery] string? foregroundLayer,
            [FromRoute] int? imageIndex = null)
        {
            var user = _userManager.GetUserById(userId);
            if (user == null)
            {
                return NotFound();
            }

            var info = new ItemImageInfo
            {
                Path = user.ProfileImage.Path,
                Type = ImageType.Profile,
                DateModified = user.ProfileImage.LastModified
            };

            if (width.HasValue)
            {
                info.Width = width.Value;
            }

            if (height.HasValue)
            {
                info.Height = height.Value;
            }

            return await GetImageInternal(
                    user.Id,
                    imageType,
                    imageIndex,
                    tag,
                    format,
                    maxWidth,
                    maxHeight,
                    percentPlayed,
                    unplayedCount,
                    width,
                    height,
                    quality,
                    cropWhitespace,
                    addPlayedIndicator,
                    blur,
                    backgroundColor,
                    foregroundLayer,
                    null,
                    Request.Method.Equals(HttpMethods.Head, StringComparison.OrdinalIgnoreCase),
                    info)
                .ConfigureAwait(false);
        }

        private static async Task<MemoryStream> GetMemoryStream(Stream inputStream)
        {
            using var reader = new StreamReader(inputStream);
            var text = await reader.ReadToEndAsync().ConfigureAwait(false);

            var bytes = Convert.FromBase64String(text);
            return new MemoryStream(bytes, 0, bytes.Length, false, true);
        }

        private ImageInfo? GetImageInfo(BaseItem item, ItemImageInfo info, int? imageIndex)
        {
            int? width = null;
            int? height = null;
            string? blurhash = null;
            long length = 0;

            try
            {
                if (info.IsLocalFile)
                {
                    var fileInfo = _fileSystem.GetFileInfo(info.Path);
                    length = fileInfo.Length;

                    blurhash = info.BlurHash;
                    width = info.Width;
                    height = info.Height;

                    if (width <= 0 || height <= 0)
                    {
                        width = null;
                        height = null;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting image information for {Item}", item.Name);
            }

            try
            {
                return new ImageInfo
                {
                    Path = info.Path,
                    ImageIndex = imageIndex,
                    ImageType = info.Type,
                    ImageTag = _imageProcessor.GetImageCacheTag(item, info),
                    Size = length,
                    BlurHash = blurhash,
                    Width = width,
                    Height = height
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting image information for {Path}", info.Path);
                return null;
            }
        }

        private async Task<ActionResult> GetImageInternal(
            Guid itemId,
            ImageType imageType,
            int? imageIndex,
            string? tag,
            string? format,
            int? maxWidth,
            int? maxHeight,
            double? percentPlayed,
            int? unplayedCount,
            int? width,
            int? height,
            int? quality,
            bool? cropWhitespace,
            bool? addPlayedIndicator,
            int? blur,
            string? backgroundColor,
            string? foregroundLayer,
            BaseItem? item,
            bool isHeadRequest,
            ItemImageInfo? imageInfo = null)
        {
            if (percentPlayed.HasValue)
            {
                if (percentPlayed.Value <= 0)
                {
                    percentPlayed = null;
                }
                else if (percentPlayed.Value >= 100)
                {
                    percentPlayed = null;
                    addPlayedIndicator = true;
                }
            }

            if (percentPlayed.HasValue)
            {
                unplayedCount = null;
            }

            if (unplayedCount.HasValue
                && unplayedCount.Value <= 0)
            {
                unplayedCount = null;
            }

            if (imageInfo == null)
            {
                imageInfo = item?.GetImageInfo(imageType, imageIndex ?? 0);
                if (imageInfo == null)
                {
                    return NotFound(string.Format(NumberFormatInfo.InvariantInfo, "{0} does not have an image of type {1}", item?.Name, imageType));
                }
            }

            cropWhitespace ??= imageType == ImageType.Logo || imageType == ImageType.Art;

            var outputFormats = GetOutputFormats(format);

            TimeSpan? cacheDuration = null;

            if (!string.IsNullOrEmpty(tag))
            {
                cacheDuration = TimeSpan.FromDays(365);
            }

            var responseHeaders = new Dictionary<string, string>
            {
                { "transferMode.dlna.org", "Interactive" },
                { "realTimeInfo.dlna.org", "DLNA.ORG_TLAG=*" }
            };

            return await GetImageResult(
                item,
                itemId,
                imageIndex,
                height,
                maxHeight,
                maxWidth,
                quality,
                width,
                addPlayedIndicator,
                percentPlayed,
                unplayedCount,
                blur,
                backgroundColor,
                foregroundLayer,
                imageInfo,
                cropWhitespace.Value,
                outputFormats,
                cacheDuration,
                responseHeaders,
                isHeadRequest).ConfigureAwait(false);
        }

        private ImageFormat[] GetOutputFormats(string? format)
        {
            if (!string.IsNullOrWhiteSpace(format)
                && Enum.TryParse(format, true, out ImageFormat parsedFormat))
            {
                return new[] { parsedFormat };
            }

            return GetClientSupportedFormats();
        }

        private ImageFormat[] GetClientSupportedFormats()
        {
            var acceptTypes = Request.Headers[HeaderNames.Accept];
            var supportedFormats = new List<string>();
            if (acceptTypes.Count > 0)
            {
                foreach (var type in acceptTypes)
                {
                    int index = type.IndexOf(';', StringComparison.Ordinal);
                    if (index != -1)
                    {
                        supportedFormats.Add(type.Substring(0, index));
                    }
                }
            }

            var acceptParam = Request.Query[HeaderNames.Accept];

            var supportsWebP = SupportsFormat(supportedFormats, acceptParam, "webp", false);

            if (!supportsWebP)
            {
                var userAgent = Request.Headers[HeaderNames.UserAgent].ToString();
                if (userAgent.IndexOf("crosswalk", StringComparison.OrdinalIgnoreCase) != -1 &&
                    userAgent.IndexOf("android", StringComparison.OrdinalIgnoreCase) != -1)
                {
                    supportsWebP = true;
                }
            }

            var formats = new List<ImageFormat>(4);

            if (supportsWebP)
            {
                formats.Add(ImageFormat.Webp);
            }

            formats.Add(ImageFormat.Jpg);
            formats.Add(ImageFormat.Png);

            if (SupportsFormat(supportedFormats, acceptParam, "gif", true))
            {
                formats.Add(ImageFormat.Gif);
            }

            return formats.ToArray();
        }

        private bool SupportsFormat(IReadOnlyCollection<string> requestAcceptTypes, string acceptParam, string format, bool acceptAll)
        {
            var mimeType = "image/" + format;

            if (requestAcceptTypes.Contains(mimeType))
            {
                return true;
            }

            if (acceptAll && requestAcceptTypes.Contains("*/*"))
            {
                return true;
            }

            return string.Equals(acceptParam, format, StringComparison.OrdinalIgnoreCase);
        }

        private async Task<ActionResult> GetImageResult(
            BaseItem? item,
            Guid itemId,
            int? index,
            int? height,
            int? maxHeight,
            int? maxWidth,
            int? quality,
            int? width,
            bool? addPlayedIndicator,
            double? percentPlayed,
            int? unplayedCount,
            int? blur,
            string? backgroundColor,
            string? foregroundLayer,
            ItemImageInfo imageInfo,
            bool cropWhitespace,
            IReadOnlyCollection<ImageFormat> supportedFormats,
            TimeSpan? cacheDuration,
            IDictionary<string, string> headers,
            bool isHeadRequest)
        {
            if (!imageInfo.IsLocalFile && item != null)
            {
                imageInfo = await _libraryManager.ConvertImageToLocal(item, imageInfo, index ?? 0).ConfigureAwait(false);
            }

            var options = new ImageProcessingOptions
            {
                CropWhiteSpace = cropWhitespace,
                Height = height,
                ImageIndex = index ?? 0,
                Image = imageInfo,
                Item = item,
                ItemId = itemId,
                MaxHeight = maxHeight,
                MaxWidth = maxWidth,
                Quality = quality ?? 100,
                Width = width,
                AddPlayedIndicator = addPlayedIndicator ?? false,
                PercentPlayed = percentPlayed ?? 0,
                UnplayedCount = unplayedCount,
                Blur = blur,
                BackgroundColor = backgroundColor,
                ForegroundLayer = foregroundLayer,
                SupportedOutputFormats = supportedFormats
            };

            var (imagePath, imageContentType, dateImageModified) = await _imageProcessor.ProcessImage(options).ConfigureAwait(false);

            var disableCaching = Request.Headers[HeaderNames.CacheControl].Contains("no-cache");
            var parsingSuccessful = DateTime.TryParse(Request.Headers[HeaderNames.IfModifiedSince], out var ifModifiedSinceHeader);

            // if the parsing of the IfModifiedSince header was not successful, disable caching
            if (!parsingSuccessful)
            {
                // disableCaching = true;
            }

            foreach (var (key, value) in headers)
            {
                Response.Headers.Add(key, value);
            }

            Response.ContentType = imageContentType;
            Response.Headers.Add(HeaderNames.Age, Convert.ToInt64((DateTime.UtcNow - dateImageModified).TotalSeconds).ToString(CultureInfo.InvariantCulture));
            Response.Headers.Add(HeaderNames.Vary, HeaderNames.Accept);

            if (disableCaching)
            {
                Response.Headers.Add(HeaderNames.CacheControl, "no-cache, no-store, must-revalidate");
                Response.Headers.Add(HeaderNames.Pragma, "no-cache, no-store, must-revalidate");
            }
            else
            {
                if (cacheDuration.HasValue)
                {
                    Response.Headers.Add(HeaderNames.CacheControl, "public, max-age=" + cacheDuration.Value.TotalSeconds);
                }
                else
                {
                    Response.Headers.Add(HeaderNames.CacheControl, "public");
                }

                Response.Headers.Add(HeaderNames.LastModified, dateImageModified.ToUniversalTime().ToString("ddd, dd MMM yyyy HH:mm:ss \"GMT\"", new CultureInfo("en-US", false)));

                // if the image was not modified since "ifModifiedSinceHeader"-header, return a HTTP status code 304 not modified
                if (!(dateImageModified > ifModifiedSinceHeader))
                {
                    if (ifModifiedSinceHeader.Add(cacheDuration!.Value) < DateTime.UtcNow)
                    {
                        Response.StatusCode = StatusCodes.Status304NotModified;
                        return new ContentResult();
                    }
                }
            }

            // if the request is a head request, return a NoContent result with the same headers as it would with a GET request
            if (isHeadRequest)
            {
                return NoContent();
            }

            var stream = new FileStream(imagePath, FileMode.Open, FileAccess.Read);
            return File(stream, imageContentType);
        }
    }
}
