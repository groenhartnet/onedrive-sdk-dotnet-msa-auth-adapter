﻿// ------------------------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All Rights Reserved.  Licensed under the MIT License.  See License in the project root for license information.
// ------------------------------------------------------------------------------

namespace Microsoft.OneDrive.Sdk.Authentication
{
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.IO;

    using Microsoft.Graph;

    /// <summary>
    /// Notification delegate for cache access.
    /// </summary>
    /// <param name="args">The argument set for the notification.</param>
    public delegate void CredentialCacheNotification(CredentialCacheNotificationArgs args);

    public class CredentialCache
    {
        internal readonly IDictionary<CredentialCacheKey, AccountSession> cacheDictionary =
            new ConcurrentDictionary<CredentialCacheKey, AccountSession>();

        private const int CacheVersion = 1;

        /// <summary>
        /// Instantiates a new <see cref="CredentialCache"/>.
        /// </summary>
        /// <param name="serializer">The <see cref="ISerializer"/> for serializing cache contents.</param>
        public CredentialCache()
            : this(null, null)
        {
        }

        /// <summary>
        /// Instantiates a new <see cref="CredentialCache"/>.
        /// </summary>
        /// <param name="serializer">The <see cref="ISerializer"/> for serializing cache contents.</param>
        public CredentialCache(ISerializer serializer)
            : this(null, serializer)
        {
        }

        /// <summary>
        /// Instantiates a new <see cref="CredentialCache"/>.
        /// </summary>
        /// <param name="blob">The cache contents for initialization.</param>
        /// <param name="serializer">The <see cref="ISerializer"/> for serializing cache contents.</param>
        public CredentialCache(byte[] blob, ISerializer serializer = null)
        {
            this.Serializer = serializer ?? new Serializer();
            this.InitializeCacheFromBlob(blob);
        }

        /// <summary>
        /// Gets or sets the notification delegate for before accessing the cache.
        /// </summary>
        public virtual CredentialCacheNotification BeforeAccess { get; set; }

        /// <summary>
        /// Gets or sets the notification delegate for before writing to the cache.
        /// </summary>
        public virtual CredentialCacheNotification BeforeWrite { get; set; }

        /// <summary>
        /// Gets or sets the notification delegate for after accessing the cache.
        /// </summary>
        public virtual CredentialCacheNotification AfterAccess { get; set; }

        /// <summary>
        /// Gets or sets whether or not the cache state has changed.
        /// </summary>
        public virtual bool HasStateChanged { get; set; }

        protected ISerializer Serializer { get; private set; }

        /// <summary>
        /// Gets the contents of the cache.
        /// </summary>
        /// <returns>The cache contents.</returns>
        public virtual byte[] GetCacheBlob()
        {
            using (var stream = new MemoryStream())
            using (var binaryReader = new BinaryReader(stream))
            using (var binaryWriter = new BinaryWriter(stream))
            {
                binaryWriter.Write(CredentialCache.CacheVersion);
                binaryWriter.Write(this.cacheDictionary.Count);
                foreach (var cacheItem in this.cacheDictionary)
                {
                    binaryWriter.Write(this.Serializer.SerializeObject(cacheItem.Key));
                    binaryWriter.Write(this.Serializer.SerializeObject(cacheItem.Value));
                }

                var length = (int)stream.Position;
                stream.Position = 0;

                return binaryReader.ReadBytes(length);
            }
        }

        /// <summary>
        /// Initializes the cache from the specified contents.
        /// </summary>
        /// <param name="cacheBytes">The cache contents.</param>
        public virtual void InitializeCacheFromBlob(byte[] cacheBytes)
        {
            if (cacheBytes == null)
            {
                this.cacheDictionary.Clear();
            }
            else
            {
                using (var stream = new MemoryStream())
                using (var binaryReader = new BinaryReader(stream))
                using (var binaryWriter = new BinaryWriter(stream))
                {
                    binaryWriter.Write(cacheBytes);
                    stream.Position = 0;

                    this.cacheDictionary.Clear();

                    var version = binaryReader.ReadInt32();

                    if (version != CredentialCache.CacheVersion)
                    {
                        // If the cache version doesn't match, skip deserialization
                        return;
                    }

                    var count = binaryReader.ReadInt32();

                    for (int i=0; i < count; i++)
                    {
                        var keyString = binaryReader.ReadString();
                        var authResultString = binaryReader.ReadString();

                        if (!string.IsNullOrEmpty(keyString) && !string.IsNullOrEmpty(authResultString))
                        {
                            var credentialCacheKey = this.Serializer.DeserializeObject<CredentialCacheKey>(keyString);
                            var authResult = this.Serializer.DeserializeObject<AccountSession>(authResultString);

                            this.cacheDictionary.Add(credentialCacheKey, authResult);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Clears the cache contents.
        /// </summary>
        public virtual void Clear()
        {
            var cacheNotificationArgs = new CredentialCacheNotificationArgs { CredentialCache = this };

            this.OnBeforeAccess(cacheNotificationArgs);
            this.OnBeforeWrite(cacheNotificationArgs);

            this.cacheDictionary.Clear();

            this.HasStateChanged = true;
            this.OnAfterAccess(cacheNotificationArgs);
        }

        internal virtual void AddToCache(AccountSession accountSession)
        {
            var cacheNotificationArgs = new CredentialCacheNotificationArgs { CredentialCache = this };

            this.OnBeforeAccess(cacheNotificationArgs);
            this.OnBeforeWrite(cacheNotificationArgs);

            var cacheKey = this.GetKeyForAuthResult(accountSession);
            this.cacheDictionary[cacheKey] = accountSession;

            this.HasStateChanged = true;
            this.OnAfterAccess(cacheNotificationArgs);
        }

        internal virtual void DeleteFromCache(AccountSession accountSession)
        {
            if (accountSession != null)
            {
                var cacheNotificationArgs = new CredentialCacheNotificationArgs { CredentialCache = this };
                this.OnBeforeAccess(cacheNotificationArgs);
                this.OnBeforeWrite(cacheNotificationArgs);

                var credentialCacheKey = this.GetKeyForAuthResult(accountSession);
                this.cacheDictionary.Remove(credentialCacheKey);

                this.HasStateChanged = true;

                this.OnAfterAccess(cacheNotificationArgs);
            }
        }

        internal CredentialCacheKey GetKeyForAuthResult(AccountSession accountSession)
        {
            return new CredentialCacheKey
            {
                ClientId = accountSession.ClientId,
                UserId = accountSession.UserId,
            };
        }

        internal virtual AccountSession GetResultFromCache(string clientId, string userId)
        {
            var cacheNotificationArgs = new CredentialCacheNotificationArgs { CredentialCache = this };
            this.OnBeforeAccess(cacheNotificationArgs);

            var credentialCacheKey = new CredentialCacheKey
            {
                ClientId = clientId,
                UserId = userId,
            };

            AccountSession cacheResult = null;
            this.cacheDictionary.TryGetValue(credentialCacheKey, out cacheResult);

            this.OnAfterAccess(cacheNotificationArgs);

            return cacheResult;
        }

        protected void OnAfterAccess(CredentialCacheNotificationArgs args)
        {
            if (this.AfterAccess != null)
            {
                this.AfterAccess(args);
            }
        }

        protected void OnBeforeAccess(CredentialCacheNotificationArgs args)
        {
            if (this.BeforeAccess != null)
            {
                this.BeforeAccess(args);
            }
        }

        protected void OnBeforeWrite(CredentialCacheNotificationArgs args)
        {
            if (this.BeforeWrite != null)
            {
                this.BeforeWrite(args);
            }
        }
    }
}
