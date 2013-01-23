﻿using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Torrent.Client
{
    /// <summary>
    /// Provides a container class for the BitfieldMessage data for peer communication.
    /// </summary>
    class BitfieldMessage:PeerMessage
    {
        /// <summary>
        /// The ID of the message
        /// </summary>
        public static readonly int Id = 5;
        /// <summary>
        /// A bitfield representing the pieces that have been successfully downloaded.
        /// <para>A cleared bit indicated a missing piece, and set bits indicate a valid and available piece.</para>
        /// </summary>
        public byte[] Bitfield { get; private set; }
        
        /// <summary>
        /// Initializes a new empty instance of the Torrent.Client.BitfieldMessage class.
        /// </summary>
        public BitfieldMessage()
        {
            Bitfield = null;
        }

        /// <summary>
        /// Initializes a new instance of the Torrent.Client.BitfieldMessage class.
        /// </summary>
        /// <param name="bitfield">A bitfield representing the pieces that have been successfully downloaded.</param>
        public BitfieldMessage(byte[] bitfield)
        {
            Contract.Requires(bitfield != null);

            this.Bitfield = bitfield;
        }

        /// <summary>
        /// The lenght of the BitfieldMessage.
        /// </summary>
        public override int MessageLength
        {
            get { return 4+1+Bitfield.Length; }
        }

        /// <summary>
        /// Sets the BitfieldMessage properties via a byte array.
        /// </summary>
        /// <param name="buffer">The byte array containing the message data.</param>
        /// <param name="offset">The position in the array at which the message begins.</param>
        /// <param name="count">The length to be read in bytes.</param>
        public override void FromBytes(byte[] buffer, int offset, int count)
        {
            if (count != MessageLength)
                throw new ArgumentException("Invalid message length.");
            this.Bitfield = ReadBytes(buffer, ref offset, count);
        }

        /// <summary>
        /// Writes the BitfieldMessage data to a byte array.
        /// </summary>
        /// <param name="buffer">The byte array that the message data will be written to.</param>
        /// <param name="offset">The position in the array at which the message begins.</param>
        /// <returns>An integer representing the the amount of bytes written in the array.</returns>
        public override int ToBytes(byte[] buffer, int offset)
        {
            int start = offset;
            offset += Write(buffer, offset, (int)5);
            offset += Write(buffer, offset, (byte)5);
            offset += Write(buffer, offset, Bitfield);
            return offset - start;
        }

        /// <summary>
        /// Returns a string that represents the content of the BitfieldMessage object.
        /// </summary>
        /// <returns>The string containing the BitfieldMessage data representation.</returns>
        public override string ToString()
        {
            return string.Format("Bitfield message: {Bitfield.Length: {0}}", Bitfield.Length);
        }

        /// <summary>
        /// Determines wheteher this BitfieldMessage instance and a specified object, which also must be a BitfieldMessage object, have the same data values.
        /// </summary>
        /// <param name="obj">The BitfieldMessage to compare to this instance.</param>
        /// <returns></returns>
        public override bool Equals(object obj)
        {
            BitfieldMessage msg = obj as BitfieldMessage;
            
            if (msg == null)
                return false;
            return CompareByteArray(this.Bitfield, msg.Bitfield);
        }

        /// <summary>
        /// Returns the hash code for this BitfieldMessage instance.
        /// </summary>
        /// <returns>An integer representing the hash code of this instace of the BitfieldMessage class.</returns>
        public override int GetHashCode()
        {
            return MessageLength.GetHashCode() ^ Id.GetHashCode() ^ Bitfield.GetHashCode();
        }
    }
}
