﻿using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Emby.Server.Implementations.Data;
using Emby.Server.Implementations.Serialization;
using Jellyfin.Data.Entities;
using Jellyfin.Data.Enums;
using Jellyfin.Server.Implementations;
using Jellyfin.Server.Implementations.Users;
using MediaBrowser.Common.Json;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Users;
using Microsoft.Extensions.Logging;
using SQLitePCL.pretty;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Jellyfin.Server.Migrations.Routines
{
    /// <summary>
    /// The migration routine for migrating the user database to EF Core.
    /// </summary>
    public class MigrateUserDb : IMigrationRoutine
    {
        private const string DbFilename = "users.db";

        private readonly ILogger<MigrateUserDb> _logger;
        private readonly IServerApplicationPaths _paths;
        private readonly JellyfinDbProvider _provider;
        private readonly MyXmlSerializer _xmlSerializer;

        /// <summary>
        /// Initializes a new instance of the <see cref="MigrateUserDb"/> class.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="paths">The server application paths.</param>
        /// <param name="provider">The database provider.</param>
        /// <param name="xmlSerializer">The xml serializer.</param>
        public MigrateUserDb(
            ILogger<MigrateUserDb> logger,
            IServerApplicationPaths paths,
            JellyfinDbProvider provider,
            MyXmlSerializer xmlSerializer)
        {
            _logger = logger;
            _paths = paths;
            _provider = provider;
            _xmlSerializer = xmlSerializer;
        }

        /// <inheritdoc/>
        public Guid Id => Guid.Parse("5C4B82A2-F053-4009-BD05-B6FCAD82F14C");

        /// <inheritdoc/>
        public string Name => "MigrateUserDatabase";

        /// <inheritdoc/>
        public bool PerformOnNewInstall => false;

        /// <inheritdoc/>
        public void Perform()
        {
            var dataPath = _paths.DataPath;
            _logger.LogInformation("Migrating the user database may take a while, do not stop Jellyfin.");

            using (var connection = SQLite3.Open(Path.Combine(dataPath, DbFilename), ConnectionFlags.ReadOnly, null))
            {
                var dbContext = _provider.CreateContext();

                var queryResult = connection.Query("SELECT * FROM LocalUsersv2");

                dbContext.RemoveRange(dbContext.Users);
                dbContext.SaveChanges();

                foreach (var entry in queryResult)
                {
                    UserMockup? mockup = JsonSerializer.Deserialize<UserMockup>(entry[2].ToBlob(), JsonDefaults.GetOptions());
                    if (mockup == null)
                    {
                        continue;
                    }

                    var userDataDir = Path.Combine(_paths.UserConfigurationDirectoryPath, mockup.Name);

                    var config = File.Exists(Path.Combine(userDataDir, "config.xml"))
                        ? (UserConfiguration)_xmlSerializer.DeserializeFromFile(typeof(UserConfiguration), Path.Combine(userDataDir, "config.xml"))
                        : new UserConfiguration();
                    var policy = File.Exists(Path.Combine(userDataDir, "policy.xml"))
                        ? (UserPolicy)_xmlSerializer.DeserializeFromFile(typeof(UserPolicy), Path.Combine(userDataDir, "policy.xml"))
                        : new UserPolicy();
                    policy.AuthenticationProviderId = policy.AuthenticationProviderId?.Replace(
                        "Emby.Server.Implementations.Library",
                        "Jellyfin.Server.Implementations.Users",
                        StringComparison.Ordinal)
                        ?? typeof(DefaultAuthenticationProvider).FullName;

                    policy.PasswordResetProviderId = typeof(DefaultPasswordResetProvider).FullName;
                    int? maxLoginAttempts = policy.LoginAttemptsBeforeLockout switch
                    {
                        -1 => null,
                        0 => 3,
                        _ => policy.LoginAttemptsBeforeLockout
                    };

                    var user = new User(mockup.Name, policy.AuthenticationProviderId, policy.PasswordResetProviderId)
                    {
                        Id = entry[1].ReadGuidFromBlob(),
                        InternalId = entry[0].ToInt64(),
                        MaxParentalAgeRating = policy.MaxParentalRating,
                        EnableUserPreferenceAccess = policy.EnableUserPreferenceAccess,
                        RemoteClientBitrateLimit = policy.RemoteClientBitrateLimit,
                        InvalidLoginAttemptCount = policy.InvalidLoginAttemptCount,
                        LoginAttemptsBeforeLockout = maxLoginAttempts,
                        SubtitleMode = config.SubtitleMode,
                        HidePlayedInLatest = config.HidePlayedInLatest,
                        EnableLocalPassword = config.EnableLocalPassword,
                        PlayDefaultAudioTrack = config.PlayDefaultAudioTrack,
                        DisplayCollectionsView = config.DisplayCollectionsView,
                        DisplayMissingEpisodes = config.DisplayMissingEpisodes,
                        AudioLanguagePreference = config.AudioLanguagePreference,
                        RememberAudioSelections = config.RememberAudioSelections,
                        EnableNextEpisodeAutoPlay = config.EnableNextEpisodeAutoPlay,
                        RememberSubtitleSelections = config.RememberSubtitleSelections,
                        SubtitleLanguagePreference = config.SubtitleLanguagePreference,
                        Password = mockup.Password,
                        EasyPassword = mockup.EasyPassword,
                        LastLoginDate = mockup.LastLoginDate,
                        LastActivityDate = mockup.LastActivityDate
                    };

                    if (mockup.ImageInfos.Length > 0)
                    {
                        ItemImageInfo info = mockup.ImageInfos[0];

                        user.ProfileImage = new ImageInfo(info.Path)
                        {
                            LastModified = info.DateModified
                        };
                    }

                    user.SetPermission(PermissionKind.IsAdministrator, policy.IsAdministrator);
                    user.SetPermission(PermissionKind.IsHidden, policy.IsHidden);
                    user.SetPermission(PermissionKind.IsDisabled, policy.IsDisabled);
                    user.SetPermission(PermissionKind.EnableSharedDeviceControl, policy.EnableSharedDeviceControl);
                    user.SetPermission(PermissionKind.EnableRemoteAccess, policy.EnableRemoteAccess);
                    user.SetPermission(PermissionKind.EnableLiveTvManagement, policy.EnableLiveTvManagement);
                    user.SetPermission(PermissionKind.EnableLiveTvAccess, policy.EnableLiveTvAccess);
                    user.SetPermission(PermissionKind.EnableMediaPlayback, policy.EnableMediaPlayback);
                    user.SetPermission(PermissionKind.EnableAudioPlaybackTranscoding, policy.EnableAudioPlaybackTranscoding);
                    user.SetPermission(PermissionKind.EnableVideoPlaybackTranscoding, policy.EnableVideoPlaybackTranscoding);
                    user.SetPermission(PermissionKind.EnableContentDeletion, policy.EnableContentDeletion);
                    user.SetPermission(PermissionKind.EnableContentDownloading, policy.EnableContentDownloading);
                    user.SetPermission(PermissionKind.EnableSyncTranscoding, policy.EnableSyncTranscoding);
                    user.SetPermission(PermissionKind.EnableMediaConversion, policy.EnableMediaConversion);
                    user.SetPermission(PermissionKind.EnableAllChannels, policy.EnableAllChannels);
                    user.SetPermission(PermissionKind.EnableAllDevices, policy.EnableAllDevices);
                    user.SetPermission(PermissionKind.EnableAllFolders, policy.EnableAllFolders);
                    user.SetPermission(PermissionKind.EnableRemoteControlOfOtherUsers, policy.EnableRemoteControlOfOtherUsers);
                    user.SetPermission(PermissionKind.EnablePlaybackRemuxing, policy.EnablePlaybackRemuxing);
                    user.SetPermission(PermissionKind.ForceRemoteSourceTranscoding, policy.ForceRemoteSourceTranscoding);
                    user.SetPermission(PermissionKind.EnablePublicSharing, policy.EnablePublicSharing);

                    foreach (var policyAccessSchedule in policy.AccessSchedules)
                    {
                        user.AccessSchedules.Add(policyAccessSchedule);
                    }

                    user.SetPreference(PreferenceKind.BlockedTags, policy.BlockedTags);
                    user.SetPreference(PreferenceKind.EnabledChannels, policy.EnabledChannels?.Select(i => i.ToString("N", CultureInfo.InvariantCulture)).ToArray());
                    user.SetPreference(PreferenceKind.EnabledDevices, policy.EnabledDevices);
                    user.SetPreference(PreferenceKind.EnabledFolders, policy.EnabledFolders?.Select(i => i.ToString("N", CultureInfo.InvariantCulture)).ToArray());
                    user.SetPreference(PreferenceKind.EnableContentDeletionFromFolders, policy.EnableContentDeletionFromFolders);
                    user.SetPreference(PreferenceKind.OrderedViews, config.OrderedViews);
                    user.SetPreference(PreferenceKind.GroupedFolders, config.GroupedFolders);
                    user.SetPreference(PreferenceKind.MyMediaExcludes, config.MyMediaExcludes);
                    user.SetPreference(PreferenceKind.LatestItemExcludes, config.LatestItemsExcludes);

                    dbContext.Users.Add(user);
                }

                dbContext.SaveChanges();
            }

            try
            {
                File.Move(Path.Combine(dataPath, DbFilename), Path.Combine(dataPath, DbFilename + ".old"));

                var journalPath = Path.Combine(dataPath, DbFilename + "-journal");
                if (File.Exists(journalPath))
                {
                    File.Move(journalPath, Path.Combine(dataPath, DbFilename + ".old-journal"));
                }
            }
            catch (IOException e)
            {
                _logger.LogError(e, "Error renaming legacy user database to 'users.db.old'");
            }
        }

#nullable disable
        internal class UserMockup
        {
            public string Password { get; set; }

            public string EasyPassword { get; set; }

            public DateTime? LastLoginDate { get; set; }

            public DateTime? LastActivityDate { get; set; }

            public string Name { get; set; }

            public ItemImageInfo[] ImageInfos { get; set; }
        }
    }
}
