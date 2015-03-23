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
        private SortedList<ulong, string> dataStore = new SortedList<ulong, string>();

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
            if (this.dataStore.ContainsKey(key))
            {
                ChordServer.Log(LogLevel.Info, "Local invoker", "Found key {0} on node {1}", key, ChordServer.LocalNode);
                return dataStore[key];
            }

            // If the local datastore doesn't contain the specified 
            // key, search for the key on a remote datastore
            return FindKeyRemote(key, ChordServer.GetSuccessor(ChordServer.LocalNode), ChordServer.LocalNode);
        }

        /// <summary>
        /// Retrieve a value based on the key with the source node specified. 
        /// This method should not be invoked directly; use instead FindKey(key)
        /// </summary>
        /// <param name="key">The key for the value we want to fetch.</param>
        /// <param name="sourceNode">The node which initiated the request.</param>
        /// <returns>The value if it exists in the ring; otherwise an empty string.</returns>
        public string FindKey(ulong key, ChordNode sourceNode)
        {
            // First check if the local datastore contains the key
            if (this.dataStore.ContainsKey(key))
            {
                ChordServer.Log(LogLevel.Info, "Local invoker", "Found key {0} on node {1}", key, ChordServer.LocalNode);
                return dataStore[key];
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

        private string FindKeyRemote(ulong key, ChordNode remoteNode, ChordNode sourceNode)
        {
            ChordServer.Log(LogLevel.Info, "Local Invoker", "Searching for key {0} on node {1}", key, remoteNode);
            return ChordServer.CallFindKey(remoteNode, sourceNode, key);
        }

        public void DeleteKey(string value)
        {
            ulong key = ChordServer.GetHash(value);

            ChordServer.Log(LogLevel.Info, "Local invoker", "Deleting value {0} from local datastore", value);
            this.dataStore.Remove(key);

            DeleteKeyRemote(value, ChordServer.GetSuccessor(ChordServer.LocalNode), ChordServer.LocalNode);
        }

        public void DeleteKey(string value, ChordNode sourceNode)
        {
            ulong key = ChordServer.GetHash(value);

            ChordServer.Log(LogLevel.Info, "Local invoker", "Deleting value {0} from local datastore", value);
            this.dataStore.Remove(key);

            if (sourceNode.ID != ChordServer.LocalNode.ID)
            {
                DeleteKeyRemote(value, ChordServer.GetSuccessor(ChordServer.LocalNode), sourceNode);
            }
        }

        private void DeleteKeyRemote(string value, ChordNode remoteNode, ChordNode sourceNode)
        {
            ChordServer.Log(LogLevel.Info, "Local Invoker", "Deleting value {0} on node {1}", value, remoteNode);
            ChordServer.CallDeleteKey(value, remoteNode, sourceNode);
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
            if (!this.dataStore.ContainsKey(key))
            {
                ChordServer.Log(LogLevel.Info, "Local invoker", "Replicating value {0} to local datastore", value);
                this.dataStore.Add(key, value);
            }
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

        /// <summary>
        /// Retrieves a file from the network if present on any node.
        /// </summary>
        /// <param name="value">The filename to fetch.</param>
        /// <returns>A byte array consisting of the file.</returns>
        public byte[] FindFile(string value)
        {
            ulong key = ChordServer.GetHash(value);

            byte[] fileContent = null;
            string[] files = Directory.GetFiles("files");

            // Search our files directory for the specified file
            foreach (string file in files)
            {
                string filename = Path.GetFileName(file);
                if (key == ChordServer.GetHash(filename))
                {
                    // If the hash of both the stored file and the specified file
                    // matches eachother, convert the file to a byte array
                    fileContent = File.ReadAllBytes(file);
                }
            }

            // If the file couldn't be found, ask our successor for the file
            if (fileContent == null)
            {
                byte[] remoteContent = FindFileRemote(value, ChordServer.GetSuccessor(ChordServer.LocalNode), ChordServer.LocalNode);
                if (remoteContent != null)
                {
                    // If the file was found on our successor, replicate the
                    // file to our local directory
                    ReplicateFile(value, remoteContent);
                }
                return remoteContent;
            }
            ChordServer.Log(LogLevel.Info, "Local Invoker", "Found file with value {0} on node {1}", key, ChordServer.LocalNode);
            return fileContent;
        }

        /// <summary>
        /// Retrieves a file from the network if present on any node with the source node
        /// specified. This method should not be invoked directly; use instead GetFile(value)
        /// </summary>
        /// <param name="value">The filename to fetch.</param>
        /// <returns>A byte array consisting of the file.</returns>
        public byte[] FindFile(string value, ChordNode sourceNode)
        {
            // If the current node is the source node, abort the request
            if (sourceNode.ID == ChordServer.LocalNode.ID)
            {
                ChordServer.Log(LogLevel.Info, "Local Invoker", "Couldn't find file with value {0} on any node", value);
                return null;
            }

            ulong key = ChordServer.GetHash(value);

            byte[] fileContent = null;
            string[] files = Directory.GetFiles("files");

            // Search our files directory for the specified file
            foreach (string file in files)
            {
                string filename = Path.GetFileName(file);
                if (key == ChordServer.GetHash(filename))
                {
                    // If the hash of both the stored file and the specified file
                    // matches eachother, convert the file to a byte array
                    fileContent = File.ReadAllBytes(file);
                }
            }

            // If the file couldn't be found, ask our successor for the file
            if (fileContent == null)
            {
                byte[] remoteContent = FindFileRemote(value, ChordServer.GetSuccessor(ChordServer.LocalNode), sourceNode);
                if (remoteContent != null)
                {
                    // If the file was found on our successor, replicate the
                    // file to our local directory
                    ReplicateFile(value, remoteContent);
                }
                return remoteContent;
            }
            ChordServer.Log(LogLevel.Info, "Local Invoker", "Found file with key {0} on node {1}", key, ChordServer.LocalNode);
            return fileContent;
        }

        private byte[] FindFileRemote(string value, ChordNode remoteNode, ChordNode sourceNode)
        {
            ChordServer.Log(LogLevel.Info, "Local Invoker", "Searching for file with value {0} on node {1}", value, remoteNode);
            return ChordServer.CallGetFile(remoteNode, sourceNode, value);
        }

        /// <summary>
        /// Replicate the specified byte array to the specified filename.
        /// </summary>
        /// <param name="name">The filename of the file to replicate.</param>
        /// <param name="fileContent">The content of the file to replicate.</param>
        public void ReplicateFile(string name, byte[] fileContent)
        {
            ChordServer.Log(LogLevel.Info, "Local Invoker", "Replicating file with name {0} on local node", name);
            string path = Environment.CurrentDirectory + "/files/" + name;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, fileContent);
        }

        public void DeleteFile(string name)
        {
            ChordServer.Log(LogLevel.Info, "Local Invoker", "Deleting file with name {0} on local node", name);
            File.Delete(Environment.CurrentDirectory + "/files/" + name);

            DeleteFileRemote(name, ChordServer.GetSuccessor(ChordServer.LocalNode), ChordServer.LocalNode);
        }

        public void DeleteFile(string name, ChordNode sourceNode)
        {
            ChordServer.Log(LogLevel.Info, "Local Invoker", "Deleting file with name {0} on local node", name);
            File.Delete(Environment.CurrentDirectory + "/files/" + name);

            if (sourceNode.ID != ChordServer.LocalNode.ID)
            {
                DeleteFileRemote(name, ChordServer.GetSuccessor(ChordServer.LocalNode), sourceNode);
            }
        }

        private void DeleteFileRemote(string name, ChordNode remoteNode, ChordNode sourceNode)
        {
            ChordServer.Log(LogLevel.Info, "Local Invoker", "Deleting file with name {0} on node {1}", name, remoteNode);
            ChordServer.CallDeleteFile(remoteNode, sourceNode, name);
        }

    }
}