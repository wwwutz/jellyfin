﻿using System;
using System.Buffers;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net.Mime;
using System.Threading.Tasks;
using Jellyfin.Api.Constants;
using Jellyfin.Api.Helpers;
using Jellyfin.Api.Models.MediaInfoDtos;
using Jellyfin.Api.Models.VideoDtos;
using MediaBrowser.Controller.Devices;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.MediaInfo;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Api.Controllers
{
    /// <summary>
    /// The media info controller.
    /// </summary>
    [Route("")]
    [Authorize(Policy = Policies.DefaultAuthorization)]
    public class MediaInfoController : BaseJellyfinApiController
    {
        private readonly IMediaSourceManager _mediaSourceManager;
        private readonly IDeviceManager _deviceManager;
        private readonly ILibraryManager _libraryManager;
        private readonly IAuthorizationContext _authContext;
        private readonly ILogger<MediaInfoController> _logger;
        private readonly MediaInfoHelper _mediaInfoHelper;

        /// <summary>
        /// Initializes a new instance of the <see cref="MediaInfoController"/> class.
        /// </summary>
        /// <param name="mediaSourceManager">Instance of the <see cref="IMediaSourceManager"/> interface.</param>
        /// <param name="deviceManager">Instance of the <see cref="IDeviceManager"/> interface.</param>
        /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
        /// <param name="authContext">Instance of the <see cref="IAuthorizationContext"/> interface.</param>
        /// <param name="logger">Instance of the <see cref="ILogger{MediaInfoController}"/> interface.</param>
        /// <param name="mediaInfoHelper">Instance of the <see cref="MediaInfoHelper"/>.</param>
        public MediaInfoController(
            IMediaSourceManager mediaSourceManager,
            IDeviceManager deviceManager,
            ILibraryManager libraryManager,
            IAuthorizationContext authContext,
            ILogger<MediaInfoController> logger,
            MediaInfoHelper mediaInfoHelper)
        {
            _mediaSourceManager = mediaSourceManager;
            _deviceManager = deviceManager;
            _libraryManager = libraryManager;
            _authContext = authContext;
            _logger = logger;
            _mediaInfoHelper = mediaInfoHelper;
        }

        /// <summary>
        /// Gets live playback media info for an item.
        /// </summary>
        /// <param name="itemId">The item id.</param>
        /// <param name="userId">The user id.</param>
        /// <response code="200">Playback info returned.</response>
        /// <returns>A <see cref="Task"/> containing a <see cref="PlaybackInfoResponse"/> with the playback information.</returns>
        [HttpGet("Items/{itemId}/PlaybackInfo")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<ActionResult<PlaybackInfoResponse>> GetPlaybackInfo([FromRoute] Guid itemId, [FromQuery, Required] Guid? userId)
        {
            return await _mediaInfoHelper.GetPlaybackInfo(
                    itemId,
                    userId)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Gets live playback media info for an item.
        /// </summary>
        /// <param name="itemId">The item id.</param>
        /// <param name="userId">The user id.</param>
        /// <param name="maxStreamingBitrate">The maximum streaming bitrate.</param>
        /// <param name="startTimeTicks">The start time in ticks.</param>
        /// <param name="audioStreamIndex">The audio stream index.</param>
        /// <param name="subtitleStreamIndex">The subtitle stream index.</param>
        /// <param name="maxAudioChannels">The maximum number of audio channels.</param>
        /// <param name="mediaSourceId">The media source id.</param>
        /// <param name="liveStreamId">The livestream id.</param>
        /// <param name="deviceProfile">The device profile.</param>
        /// <param name="autoOpenLiveStream">Whether to auto open the livestream.</param>
        /// <param name="enableDirectPlay">Whether to enable direct play. Default: true.</param>
        /// <param name="enableDirectStream">Whether to enable direct stream. Default: true.</param>
        /// <param name="enableTranscoding">Whether to enable transcoding. Default: true.</param>
        /// <param name="allowVideoStreamCopy">Whether to allow to copy the video stream. Default: true.</param>
        /// <param name="allowAudioStreamCopy">Whether to allow to copy the audio stream. Default: true.</param>
        /// <response code="200">Playback info returned.</response>
        /// <returns>A <see cref="Task"/> containing a <see cref="PlaybackInfoResponse"/> with the playback info.</returns>
        [HttpPost("Items/{itemId}/PlaybackInfo")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<ActionResult<PlaybackInfoResponse>> GetPostedPlaybackInfo(
            [FromRoute] Guid itemId,
            [FromQuery] Guid? userId,
            [FromQuery] long? maxStreamingBitrate,
            [FromQuery] long? startTimeTicks,
            [FromQuery] int? audioStreamIndex,
            [FromQuery] int? subtitleStreamIndex,
            [FromQuery] int? maxAudioChannels,
            [FromQuery] string? mediaSourceId,
            [FromQuery] string? liveStreamId,
            [FromBody] DeviceProfileDto? deviceProfile,
            [FromQuery] bool autoOpenLiveStream = false,
            [FromQuery] bool enableDirectPlay = true,
            [FromQuery] bool enableDirectStream = true,
            [FromQuery] bool enableTranscoding = true,
            [FromQuery] bool allowVideoStreamCopy = true,
            [FromQuery] bool allowAudioStreamCopy = true)
        {
            var authInfo = _authContext.GetAuthorizationInfo(Request);

            var profile = deviceProfile?.DeviceProfile;

            _logger.LogInformation("GetPostedPlaybackInfo profile: {@Profile}", profile);

            if (profile == null)
            {
                var caps = _deviceManager.GetCapabilities(authInfo.DeviceId);
                if (caps != null)
                {
                    profile = caps.DeviceProfile;
                }
            }

            var info = await _mediaInfoHelper.GetPlaybackInfo(
                    itemId,
                    userId,
                    mediaSourceId,
                    liveStreamId)
                .ConfigureAwait(false);

            if (profile != null)
            {
                // set device specific data
                var item = _libraryManager.GetItemById(itemId);

                foreach (var mediaSource in info.MediaSources)
                {
                    _mediaInfoHelper.SetDeviceSpecificData(
                        item,
                        mediaSource,
                        profile,
                        authInfo,
                        maxStreamingBitrate ?? profile.MaxStreamingBitrate,
                        startTimeTicks ?? 0,
                        mediaSourceId ?? string.Empty,
                        audioStreamIndex,
                        subtitleStreamIndex,
                        maxAudioChannels,
                        info!.PlaySessionId!,
                        userId ?? Guid.Empty,
                        enableDirectPlay,
                        enableDirectStream,
                        enableTranscoding,
                        allowVideoStreamCopy,
                        allowAudioStreamCopy,
                        Request.HttpContext.Connection.RemoteIpAddress.ToString());
                }

                _mediaInfoHelper.SortMediaSources(info, maxStreamingBitrate);
            }

            if (autoOpenLiveStream)
            {
                var mediaSource = string.IsNullOrWhiteSpace(mediaSourceId) ? info.MediaSources[0] : info.MediaSources.FirstOrDefault(i => string.Equals(i.Id, mediaSourceId, StringComparison.Ordinal));

                if (mediaSource != null && mediaSource.RequiresOpening && string.IsNullOrWhiteSpace(mediaSource.LiveStreamId))
                {
                    var openStreamResult = await _mediaInfoHelper.OpenMediaSource(
                        Request,
                        new LiveStreamRequest
                        {
                            AudioStreamIndex = audioStreamIndex,
                            DeviceProfile = deviceProfile?.DeviceProfile,
                            EnableDirectPlay = enableDirectPlay,
                            EnableDirectStream = enableDirectStream,
                            ItemId = itemId,
                            MaxAudioChannels = maxAudioChannels,
                            MaxStreamingBitrate = maxStreamingBitrate,
                            PlaySessionId = info.PlaySessionId,
                            StartTimeTicks = startTimeTicks,
                            SubtitleStreamIndex = subtitleStreamIndex,
                            UserId = userId ?? Guid.Empty,
                            OpenToken = mediaSource.OpenToken
                        }).ConfigureAwait(false);

                    info.MediaSources = new[] { openStreamResult.MediaSource };
                }
            }

            if (info.MediaSources != null)
            {
                foreach (var mediaSource in info.MediaSources)
                {
                    _mediaInfoHelper.NormalizeMediaSourceContainer(mediaSource, profile!, DlnaProfileType.Video);
                }
            }

            return info;
        }

        /// <summary>
        /// Opens a media source.
        /// </summary>
        /// <param name="openToken">The open token.</param>
        /// <param name="userId">The user id.</param>
        /// <param name="playSessionId">The play session id.</param>
        /// <param name="maxStreamingBitrate">The maximum streaming bitrate.</param>
        /// <param name="startTimeTicks">The start time in ticks.</param>
        /// <param name="audioStreamIndex">The audio stream index.</param>
        /// <param name="subtitleStreamIndex">The subtitle stream index.</param>
        /// <param name="maxAudioChannels">The maximum number of audio channels.</param>
        /// <param name="itemId">The item id.</param>
        /// <param name="openLiveStreamDto">The open live stream dto.</param>
        /// <param name="enableDirectPlay">Whether to enable direct play. Default: true.</param>
        /// <param name="enableDirectStream">Whether to enable direct stream. Default: true.</param>
        /// <response code="200">Media source opened.</response>
        /// <returns>A <see cref="Task"/> containing a <see cref="LiveStreamResponse"/>.</returns>
        [HttpPost("LiveStreams/Open")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<ActionResult<LiveStreamResponse>> OpenLiveStream(
            [FromQuery] string? openToken,
            [FromQuery] Guid? userId,
            [FromQuery] string? playSessionId,
            [FromQuery] long? maxStreamingBitrate,
            [FromQuery] long? startTimeTicks,
            [FromQuery] int? audioStreamIndex,
            [FromQuery] int? subtitleStreamIndex,
            [FromQuery] int? maxAudioChannels,
            [FromQuery] Guid? itemId,
            [FromBody] OpenLiveStreamDto openLiveStreamDto,
            [FromQuery] bool enableDirectPlay = true,
            [FromQuery] bool enableDirectStream = true)
        {
            var request = new LiveStreamRequest
            {
                OpenToken = openToken,
                UserId = userId ?? Guid.Empty,
                PlaySessionId = playSessionId,
                MaxStreamingBitrate = maxStreamingBitrate,
                StartTimeTicks = startTimeTicks,
                AudioStreamIndex = audioStreamIndex,
                SubtitleStreamIndex = subtitleStreamIndex,
                MaxAudioChannels = maxAudioChannels,
                ItemId = itemId ?? Guid.Empty,
                DeviceProfile = openLiveStreamDto?.DeviceProfile,
                EnableDirectPlay = enableDirectPlay,
                EnableDirectStream = enableDirectStream,
                DirectPlayProtocols = openLiveStreamDto?.DirectPlayProtocols ?? new[] { MediaProtocol.Http }
            };
            return await _mediaInfoHelper.OpenMediaSource(Request, request).ConfigureAwait(false);
        }

        /// <summary>
        /// Closes a media source.
        /// </summary>
        /// <param name="liveStreamId">The livestream id.</param>
        /// <response code="204">Livestream closed.</response>
        /// <returns>A <see cref="NoContentResult"/> indicating success.</returns>
        [HttpPost("LiveStreams/Close")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<ActionResult> CloseLiveStream([FromQuery, Required] string? liveStreamId)
        {
            await _mediaSourceManager.CloseLiveStream(liveStreamId).ConfigureAwait(false);
            return NoContent();
        }

        /// <summary>
        /// Tests the network with a request with the size of the bitrate.
        /// </summary>
        /// <param name="size">The bitrate. Defaults to 102400.</param>
        /// <response code="200">Test buffer returned.</response>
        /// <response code="400">Size has to be a numer between 0 and 10,000,000.</response>
        /// <returns>A <see cref="FileResult"/> with specified bitrate.</returns>
        [HttpGet("Playback/BitrateTest")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [Produces(MediaTypeNames.Application.Octet)]
        public ActionResult GetBitrateTestBytes([FromQuery] int size = 102400)
        {
            const int MaxSize = 10_000_000;

            if (size <= 0)
            {
                return BadRequest($"The requested size ({size}) is equal to or smaller than 0.");
            }

            if (size > MaxSize)
            {
                return BadRequest($"The requested size ({size}) is larger than the max allowed value ({MaxSize}).");
            }

            byte[] buffer = ArrayPool<byte>.Shared.Rent(size);
            try
            {
                new Random().NextBytes(buffer);
                return File(buffer, MediaTypeNames.Application.Octet);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }
}
