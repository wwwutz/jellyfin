using System;
using System.Collections.Generic;
using Jellyfin.Data.Entities;

namespace MediaBrowser.Controller
{
    /// <summary>
    /// Manages the storage and retrieval of display preferences.
    /// </summary>
    public interface IDisplayPreferencesManager
    {
        /// <summary>
        /// Gets the display preferences for the user and client.
        /// </summary>
        /// <param name="userId">The user's id.</param>
        /// <param name="client">The client string.</param>
        /// <returns>The associated display preferences.</returns>
        DisplayPreferences GetDisplayPreferences(Guid userId, string client);

        /// <summary>
        /// Gets the default item display preferences for the user and client.
        /// </summary>
        /// <param name="userId">The user id.</param>
        /// <param name="itemId">The item id.</param>
        /// <param name="client">The client string.</param>
        /// <returns>The item display preferences.</returns>
        ItemDisplayPreferences GetItemDisplayPreferences(Guid userId, Guid itemId, string client);

        /// <summary>
        /// Gets all of the item display preferences for the user and client.
        /// </summary>
        /// <param name="userId">The user id.</param>
        /// <param name="client">The client string.</param>
        /// <returns>A list of item display preferences.</returns>
        IList<ItemDisplayPreferences> ListItemDisplayPreferences(Guid userId, string client);

        /// <summary>
        /// Saves changes to the provided display preferences.
        /// </summary>
        /// <param name="preferences">The display preferences to save.</param>
        void SaveChanges(DisplayPreferences preferences);

        /// <summary>
        /// Saves changes to the provided item display preferences.
        /// </summary>
        /// <param name="preferences">The item display preferences to save.</param>
        void SaveChanges(ItemDisplayPreferences preferences);
    }
}
