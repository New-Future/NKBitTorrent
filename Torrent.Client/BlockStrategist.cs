﻿using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Torrent.Client.Events;
using Torrent.Client.Extensions;

namespace Torrent.Client
{
    public class BlockStrategist
    {
        private readonly int blockSize;
        private readonly int pieceSize;
        private readonly long totalSize;
        private readonly int blockCount;
        private readonly BlockAddressCollection<int> unavailable;
        private readonly int[] pieces;

        public BitArray Bitfield { get; private set; }
        public int Available { get; private set; }
        public bool Complete
        {
            get
            {
                lock (unavailable)
                    return !unavailable.Any();
            }
        }

        public BlockStrategist(TorrentData data)
        {
            this.blockSize = Global.Instance.BlockSize;
            pieceSize = data.PieceLength;
            totalSize = data.Files.Sum(f => f.Length);
            blockCount = (int)Math.Ceiling((float)totalSize/blockSize);
            pieces = new int[data.PieceCount];
            Bitfield = new BitArray(data.PieceCount);
            unavailable = new BlockAddressCollection<int>();
            for (int i = 0; i < blockCount; i++)
                unavailable.Add(i);

            for(int i = 0; i < data.PieceCount-1; i++)
            {
                pieces[i]= data.PieceLength;
            }
            int lastLength = (int)(data.TotalLength - (data.PieceLength*(data.PieceCount - 1)));
            pieces[pieces.Length - 1] = lastLength;
        }

        public BlockInfo Next(BitArray bitfield)
        {
            if (Available == blockCount)
                return BlockInfo.Empty;
            BlockInfo block;
            int counter = 0;
            do
            {
                counter++;
                int index;
                lock(unavailable)
                {
                    if (unavailable.Any())
                        index = unavailable.Random();
                    else return BlockInfo.Empty;
                }
                
                block = Block.FromAbsoluteAddress((long)index*blockSize, pieceSize, blockSize,
                                                 totalSize);
                if (counter > 10)
                    return block;
            } while (!bitfield[block.Index]);

            Debug.WriteLine("Strategist requested block " + block.Index);
            return block;
        }

        public bool Received(BlockInfo block)
        {   //изчисляване на адреса на блока
            int address = (int)(Block.GetAbsoluteAddress(block.Index, block.Offset, pieceSize)/blockSize);
            lock (unavailable)
            {
                if(unavailable.Contains(address) && block.Length > 0)
                {
                    Debug.WriteLine("Needed block incoming:" + address);
                    unavailable.Remove(address);
                    Available++;
                    pieces[block.Index] -= block.Length;
                    if(pieces[block.Index]<=0)
                    {
                        SetDownloaded(block.Index);
                    }
                    return true;
                }
                Debug.WriteLine("Unneeded block incoming:" + address);
                return false;
            }
        }

        private void SetDownloaded(int piece)
        {
            Bitfield.Set(piece, true);
            OnHavePiece(piece);
        }

        public event EventHandler<EventArgs<int>> HavePiece;

        private void OnHavePiece(int e)
        {
            EventHandler<EventArgs<int>> handler = HavePiece;
            if(handler != null) handler(this, new EventArgs<int>(e));
        }
    }

    public class BlockAddressCollection<T>:KeyedCollection<int,int>
    {
        protected override int GetKeyForItem(int item)
        {
            return item;
        }
    }
}