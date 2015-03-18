﻿/*
 * ChordInstance.Storage.cs:
 * 
 *  Contains private data structure and public methods for
 *  key-value data storage.
 * 
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace NChordLib
{
    public partial class ChordInstance : MarshalByRefObject
    {
        /// <summary>
        /// The data structure used to store string data given a 
        /// 64-bit (ulong) key value.
        /// </summary>
        private SortedList<ulong, string> m_DataStore = new SortedList<ulong, string>();

        /// <summary>
        /// Add a key-value pair to the ring.
        /// </summary>
        /// <param name="value">The value to be saved.</param>
        public void AddKey(string value)
        {
            ulong key = ChordServer.GetHash(value);

            // Replicate the key to the local datastore
            ReplicateKey(key, value);

            // Replicate the key to a remote datastore
            ReplicateRemote(value, ChordServer.GetSuccessor(ChordServer.LocalNode), ChordServer.LocalNode);
        }

        /// <summary>
        /// Add a key-value pair to the ring.
        /// </summary>
        /// <param name="value">The value to be saved.</param>
        /// <param name="sourceNode">The node which initiated the request.</param>
        public void AddKey(string value, ChordNode sourceNode)
        {
            ulong key = ChordServer.GetHash(value);

            // Replicate the key to the local datastore
            ReplicateKey(key, value);

            // Replicate the key to a remote datastore if the current local node isn't 
            // the node which initiated the request; otherwise stop the request.
            if (sourceNode.ID != ChordServer.LocalNode.ID)
            {
                ReplicateRemote(value, ChordServer.GetSuccessor(ChordServer.LocalNode), sourceNode);
            }
        }

        /// <summary>
        /// Retrieve a value based on the key.
        /// </summary>
        /// <param name="key">The key for the value we want to fetch.</param>
        /// <returns>The value if it exists in the ring; otherwise an empty string.</returns>
        public string FindKey(ulong key)
        {
            // First check if the local datastore contains the key
            if (this.m_DataStore.ContainsKey(key))
            {
                ChordServer.Log(LogLevel.Info, "Local invoker", "Found key {0} on node {1}", key, ChordServer.LocalNode);
                return m_DataStore[key];
            }

            // If the local datastore doesn't contain the specified 
            // key, search for the key on a remote datastore
            return FindKeyRemote(key, ChordServer.GetSuccessor(ChordServer.LocalNode), ChordServer.LocalNode);
        }

        /// <summary>
        /// Retrieve a value based on the key.
        /// </summary>
        /// <param name="key">The key for the value we want to fetch.</param>
        /// <param name="sourceNode">The node which initiated the request.</param>
        /// <returns>The value if it exists in the ring; otherwise an empty string.</returns>
        public string FindKey(ulong key, ChordNode sourceNode)
        {
            // First check if the local datastore contains the key
            if (this.m_DataStore.ContainsKey(key))
            {
                ChordServer.Log(LogLevel.Info, "Local invoker", "Found key {0} on node {1}", key, ChordServer.LocalNode);
                return m_DataStore[key];
            }

            // Search for the key on a remote datastore if the current local node isn't 
            // the node which initiated the request; otherwise stop the request and return
            // an empty string.
            if (sourceNode.ID != ChordServer.LocalNode.ID)
            {
                return FindKeyRemote(key, ChordServer.GetSuccessor(ChordServer.LocalNode), sourceNode);
            }
            else
            {
                ChordServer.Log(LogLevel.Info, "Local invoker", "Couldn't find key {0} on any node", key);
                return string.Empty;
            }
        }

        /// <summary>
        /// Add the given key/value pair as replicas to the local store.
        /// </summary>
        /// <param name="key">The key to replicate.</param>
        /// <param name="value">The value to replicate.</param>
        public void ReplicateKey(ulong key, string value)
        {
            // add the key/value pair to the local
            // data store regardless of ownership
            if (!this.m_DataStore.ContainsKey(key))
            {
                ChordServer.Log(LogLevel.Info, "Local invoker", "Replicating value {0} to local datastore", value);
                this.m_DataStore.Add(key, value);
            }
        }

        public string FindKeyRemote(ulong key, ChordNode remoteNode, ChordNode sourceNode)
        {
            ChordServer.Log(LogLevel.Info, "Local Invoker", "Searching for key {0} on node {1}", key, remoteNode);
            return ChordServer.CallFindKey(remoteNode, sourceNode, key);
        }

        /// <summary>
        /// Add the given value to a remote datastore.
        /// </summary>
        /// <param name="value">The value to replicate.</param>
        /// <param name="remoteNode">The node to send the request to.</param>
        /// <param name="sourceNode">The node which initiated the request.</param>
        public void ReplicateRemote(string value, ChordNode remoteNode, ChordNode sourceNode)
        {
            ChordServer.Log(LogLevel.Info, "Local Invoker", "Replicating value {0} on node {1}", value, remoteNode);
            ChordServer.CallAddKey(remoteNode, sourceNode, value);
        }

        public byte[] GetFile(string value)
        {
            ulong key = ChordServer.GetHash(value);

            byte[] fileContent = null;
            string[] files = Directory.GetFiles("files");

            foreach (string file in files)
            {
                string shortenedFile = file.Substring(file.IndexOf("\\") + 1);
                if (key == ChordServer.GetHash(shortenedFile))
                {
                    fileContent = File.ReadAllBytes(file);
                }
            }

            if (fileContent == null)
            {
                byte[] remoteContent = GetFileRemote(value, ChordServer.GetSuccessor(ChordServer.LocalNode), ChordServer.LocalNode);
                if (remoteContent != null)
                {
                    string path = Environment.CurrentDirectory + "/files/" + value;
                    File.WriteAllBytes(path, remoteContent);
                }
                return remoteContent;
            }
            ChordServer.Log(LogLevel.Info, "Local Invoker", "Found file with key {0} on node {1}", key, ChordServer.LocalNode);
            return fileContent;
        }

        public byte[] GetFile(string value, ChordNode sourceNode)
        {
            if (sourceNode.ID == ChordServer.LocalNode.ID)
            {
                ChordServer.Log(LogLevel.Info, "Local Invoker", "Couldn't find file with value {0} on any node", value);
                return null;
            }

            ulong key = ChordServer.GetHash(value);

            byte[] fileContent = null;
            string[] files = Directory.GetFiles("files");

            foreach (string file in files)
            {
                string shortenedFile = file.Substring(file.IndexOf("\\") + 1);
                if (key == ChordServer.GetHash(shortenedFile))
                {
                    fileContent = File.ReadAllBytes(file);
                }
            }

            if (fileContent == null)
            {
                byte[] remoteContent = GetFileRemote(value, ChordServer.GetSuccessor(ChordServer.LocalNode), sourceNode);
                if (remoteContent != null)
                {
                    string path = Environment.CurrentDirectory + "/files/" + value;
                    File.WriteAllBytes(path, remoteContent);
                }
                return remoteContent;
            }
            ChordServer.Log(LogLevel.Info, "Local Invoker", "Found file with key {0} on node {1}", key, ChordServer.LocalNode);
            return fileContent;
        }

        public byte[] GetFileRemote(string value, ChordNode remoteNode, ChordNode sourceNode)
        {
            ChordServer.Log(LogLevel.Info, "Local Invoker", "Searching for file with value {0} on node {1}", value, remoteNode);
            return ChordServer.CallGetFile(remoteNode, sourceNode, value);
        }

    }
}