using System;
using System.Collections.Generic;
using System.Reflection;
using Emby.Drawing;
using Emby.Server.Implementations;
using Jellyfin.Drawing.Skia;
using Jellyfin.Server.Implementations;
using Jellyfin.Server.Implementations.Activity;
using Jellyfin.Server.Implementations.Events;
using Jellyfin.Server.Implementations.Users;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Drawing;
using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Activity;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Server
{
    /// <summary>
    /// Implementation of the abstract <see cref="ApplicationHost" /> class.
    /// </summary>
    public class CoreAppHost : ApplicationHost
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="CoreAppHost" /> class.
        /// </summary>
        /// <param name="applicationPaths">The <see cref="ServerApplicationPaths" /> to be used by the <see cref="CoreAppHost" />.</param>
        /// <param name="loggerFactory">The <see cref="ILoggerFactory" /> to be used by the <see cref="CoreAppHost" />.</param>
        /// <param name="options">The <see cref="StartupOptions" /> to be used by the <see cref="CoreAppHost" />.</param>
        /// <param name="fileSystem">The <see cref="IFileSystem" /> to be used by the <see cref="CoreAppHost" />.</param>
        /// <param name="networkManager">The <see cref="INetworkManager" /> to be used by the <see cref="CoreAppHost" />.</param>
        /// <param name="collection">The <see cref="IServiceCollection"/> to be used by the <see cref="CoreAppHost"/>.</param>
        public CoreAppHost(
            IServerApplicationPaths applicationPaths,
            ILoggerFactory loggerFactory,
            IStartupOptions options,
            IFileSystem fileSystem,
            INetworkManager networkManager,
            IServiceCollection collection)
            : base(
                applicationPaths,
                loggerFactory,
                options,
                fileSystem,
                networkManager,
                collection)
        {
        }

        /// <inheritdoc/>
        protected override void RegisterServices()
        {
            // Register an image encoder
            bool useSkiaEncoder = SkiaEncoder.IsNativeLibAvailable();
            Type imageEncoderType = useSkiaEncoder
                ? typeof(SkiaEncoder)
                : typeof(NullImageEncoder);
            ServiceCollection.AddSingleton(typeof(IImageEncoder), imageEncoderType);

            // Log a warning if the Skia encoder could not be used
            if (!useSkiaEncoder)
            {
                Logger.LogWarning($"Skia not available. Will fallback to {nameof(NullImageEncoder)}.");
            }

            // TODO: Set up scoping and use AddDbContextPool,
            // can't register as Transient since tracking transient in GC is funky
            // serviceCollection.AddDbContext<JellyfinDb>(
            //     options => options
            //         .UseSqlite($"Filename={Path.Combine(ApplicationPaths.DataPath, "jellyfin.db")}"),
            //     ServiceLifetime.Transient);

            ServiceCollection.AddEventServices();
            ServiceCollection.AddSingleton<IEventManager, EventManager>();
            ServiceCollection.AddSingleton<JellyfinDbProvider>();

            ServiceCollection.AddSingleton<IActivityManager, ActivityManager>();
            ServiceCollection.AddSingleton<IUserManager, UserManager>();
            ServiceCollection.AddSingleton<IDisplayPreferencesManager, DisplayPreferencesManager>();

            base.RegisterServices();
        }

        /// <inheritdoc />
        protected override void RestartInternal() => Program.Restart();

        /// <inheritdoc />
        protected override IEnumerable<Assembly> GetAssembliesWithPartsInternal()
        {
            // Jellyfin.Server
            yield return typeof(CoreAppHost).Assembly;

            // Jellyfin.Server.Implementations
            yield return typeof(JellyfinDb).Assembly;
        }

        /// <inheritdoc />
        protected override void ShutdownInternal() => Program.Shutdown();
    }
}
