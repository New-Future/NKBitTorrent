﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Torrent.Client
{
    
    /// <summary>
    /// Represents a BitTorrent data transfer.
    /// </summary>
    public class TorrentTransfer
    {
        private const int PSTR_LENGTH = 19;
        private volatile bool stop = false;
        private TrackerClient tracker;
        private List<IPEndPoint> Endpoints;
        private NetworkCallback PeerConnectedCallback;
        private Socket listenSocket;
        private HandshakeMessage localHandshake;
        /// <summary>
        /// The metadata decribing the torrent.
        /// </summary>
        public TorrentData Data { get; private set; }

        public ConcurrentDictionary<string, PeerState> Peers { get; private set; }

        public bool Running { get; private set; }

        /// <summary>
        /// Initialize a torrent transfer with metadata from a file on the filesystem.
        /// </summary>
        /// <param name="torrentPath">Path to the torrent file.</param>
        public TorrentTransfer(string torrentPath):this(File.OpenRead(torrentPath))
        {
            Contract.Requires(torrentPath != null);
        }
        
        /// <summary>
        /// Initialize a torrent transfer with metadata read from the specified stream.
        /// </summary>
        /// <param name="torrentStream">The stream to read the torrent metadata from.</param>
        public TorrentTransfer(Stream torrentStream)
        {
            Contract.Requires(torrentStream != null);

            Endpoints = new List<IPEndPoint>();

            using (torrentStream)
            using (var reader = new BinaryReader(torrentStream))
            {
                var bytes = reader.ReadBytes((int)reader.BaseStream.Length);
                this.Data = new TorrentData(bytes);
            }

            tracker = new TrackerClient(this.Data.Announces);
            this.Peers = new ConcurrentDictionary<string, PeerState>();
            this.PeerConnectedCallback = PeerConnected;
            listenSocket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            localHandshake = new HandshakeMessage(LocalInfo.Instance.PeerId, new byte[8], Data.InfoHash, "BitTorrent protocol");
        }

        /// <summary>
        /// Starts the torrent transfer on a new thread.
        /// </summary>
        public void Start()
        {
            if (Running) throw new TorrentException("Already started.");

            var torrentThread = new Thread(StartThread);
            torrentThread.IsBackground = true;
            torrentThread.Start();
        }
        /// <summary>
        /// Stops all torrent activity and shuts down the thread.
        /// </summary>
        public void Stop()
        {
            stop = true;
        }

        private void StartThread()
        {
            StartActions();
            try
            {
                Listen();
                HandshakeTracker();
                ConnectToPeers();
                WaitForStop();
            }
            catch (Exception e)
            {
                OnRaisedException(e);
            }
            StopActions();
        }

        private void ConnectToPeers()
        {
            foreach (var peerEndpoint in Endpoints)
            {
                var peer = new PeerState(new Socket(SocketType.Stream, ProtocolType.Tcp), peerEndpoint);
                NetworkIO.Connect(peer.Socket, peer.EndPoint, peer, PeerConnected);
            }
        }

        private void PeerConnected(bool success, int transmitted, object state)
        {
            var peer = (PeerState)state;
            if (success)
            {
                Debug.WriteLine("Connected to: " + peer);
                MessageIO.SendMessage(peer.Socket, localHandshake, peer, HandshakeSent);
            }
            else
                Debug.WriteLine("Couldn't connect to: " + peer);
        }

        private void StartActions()
        {
            stop = false;
            Running = true;
        }

        private void StopActions()
        {
            OnStopping();
            Running = false;
        }

        private void WaitForStop()
        {
            while (true)
            {
                Thread.Sleep(100);
                if (stop) break;
            }
        }

        private void Listen()
        {
            listenSocket.Bind(new IPEndPoint(IPAddress.Any, LocalInfo.Instance.ListeningPort));
            listenSocket.Listen(10);
            BeginListen();
        }

        private void BeginListen()
        {
            if (stop) return;
            listenSocket.BeginAccept(EndAccept, listenSocket);
        }

        private void EndAccept(IAsyncResult ar)
        {
            var socket = (Socket)ar.AsyncState;
            var newsocket = socket.EndAccept(ar);
            var peer = new PeerState(newsocket, (IPEndPoint)newsocket.RemoteEndPoint);
            MessageIO.ReceiveHandshake(newsocket, peer, HandshakeReceived);
            Debug.WriteLine("Hello new peer");
            BeginListen();
        }

        private void HandshakeReceived(bool success, PeerMessage message, object state)
        {
            var peer = (PeerState)state;
            var handshake = (HandshakeMessage)message;
            if(success)
            {
                peer.ReceivedHandshake = true;
                peer.ID = handshake.PeerID;
                Peers.AddOrUpdate(peer.ID, peer, (id, s) => s);
                OnGotPeers();
                if(!peer.SentHandshake && peer.ID != LocalInfo.Instance.PeerId)
                    MessageIO.SendMessage(peer.Socket, localHandshake, peer, HandshakeSent);
            }
        }

        private void HandshakeSent(bool success, int sent, object state)
        {
            var peer = (PeerState)state;
            if (success)
            {
                peer.SentHandshake = true;
                OnGotPeers();
                if (!peer.ReceivedHandshake)
                {
                    MessageIO.ReceiveHandshake(peer.Socket, peer, HandshakeReceived);
                }
            }
        }

        private void MessageSent(bool success, int sent, object state)
        {
            Debug.WriteLine("I sent some message, success: " + success);
        }     

        private void HandshakeTracker()
        {
            var info = tracker.AnnounceStart(Data.InfoHash, LocalInfo.Instance.PeerId, LocalInfo.Instance.ListeningPort,
                0, 0, (long)this.Data.Files.Sum(f => f.Length));
            Endpoints = info.Endpoints;
        }
        #region Events
        private void OnGotPeers()
        {
            if (GotPeers != null)
            {
                GotPeers(this, EventArgs.Empty);
            }
        }

        private void OnRaisedException(Exception e)
        {
            if (RaisedException != null)
            {
                RaisedException(this, e);
            }
        }

        private void OnGotTcpMessage(string msg)
        {
            if (GotTcpMessage != null)
            {
                GotTcpMessage(this, msg);
            }
        }

        private void OnSentHandshake(EndPoint peer)
        {
            if (SentHandshake != null)
            {
                SentHandshake(this, peer);
            }
        }

        private void OnReceivedHandshake(EndPoint peer)
        {
            if (ReceivedHandshake != null)
            {
                ReceivedHandshake(this, peer);
            }
        }

        private void OnStopping()
        {
            if (Stopping != null)
            {
                Stopping(this, EventArgs.Empty);
            }
        }
        /// <summary>
        /// Fires when the torrent receives peers from the tracker.
        /// </summary>
        public event EventHandler GotPeers;
        /// <summary>
        /// Fires when an exception occurs in the transfer thread.
        /// </summary>
        public event EventHandler<Exception> RaisedException;
        /// <summary>
        /// Fires just prior to the transfer's complete stop.
        /// </summary>
        public event EventHandler Stopping;

        public event EventHandler<string> GotTcpMessage;

        public event EventHandler<EndPoint> SentHandshake;

        public event EventHandler<EndPoint> ReceivedHandshake;
        #endregion
    }
}
