﻿using Newtonsoft.Json.Linq;

namespace Raven.Client
{
    /// <summary>
    /// Hook for users to provide additional logic on store operations
    /// </summary>
    public interface IDocumentStoreListener
    {
        /// <summary>
        /// Invoked before the store request is sent to the server.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="entityInstance">The entity instance.</param>
        /// <param name="metadata">The metadata.</param>
        void BeforeStore(string key, object entityInstance, JObject metadata);

        /// <summary>
        /// Invoked after the store request is sent to the server.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="entityInstance">The entity instance.</param>
        /// <param name="metadata">The metadata.</param>
        void AfterStore(string key, object entityInstance, JObject metadata);
    }
}