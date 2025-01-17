//
// Version 1.1  03 Jan 2025
//


using System;
using System.Diagnostics;

namespace GoProMP4MetadataExtraction
{
    /// <summary>
    /// An item read from GPMF data.
    /// </summary>
    public class GpmfItem
	{
		// instance variables
		public string FourCC;
		public GpmfTypeSize TypeSize;
		public object? Payload;

		/// <summary>
		/// Initializes the fields from values.
		/// </summary>
		/// <param name="fourcc">FourCC code.</param>
		/// <param name="typeSize">Size of the item's data.</param>
		public GpmfItem(string fourcc, GpmfTypeSize typeSize)
		{
			FourCC = fourcc;
			TypeSize = typeSize;
			Payload = null;

        }

		/// <summary>
		/// Initializes the fields from a pointer to encoded data.
		/// </summary>
		/// <param name="ptr">Encoded data.</param>
		public GpmfItem(ref IntPtr ptr)
		{
			FourCC = GpmfParser.ReadString(ref ptr, 4);
			TypeSize = new GpmfTypeSize(ref ptr);
			Payload = GpmfParser.ParsePayload(ref ptr, this);
		}

		#region String

		/// <summary>
		/// Gets the payload as a string.
		/// </summary>
		/// <returns>The payload as a string.</returns>
		public string GetString()
		{
			string s = string.Empty;
			try
			{
				if (Payload is not null)
					s = (string)Payload;
			}
			catch
			{
				s = string.Empty;
			}
			return s;
		}

		/// <summary>
		/// Gets the payload as an array of strings.
		/// </summary>
		/// <returns>The payload as an array of strings.</returns>
		public string[] GetStringArray()
		{
			string[] sa;
			try
			{
				sa = (string[])Payload!;
			}
			catch
			{
				sa = [];
			}
			return sa;
		}

		#endregion
		#region Int

		/// <summary>
		/// Gets the payload as an int.
		/// </summary>
		/// <returns>The payload as an int.</returns>
		public int GetInt()
		{
			int i = 0;
			if (Payload is not null)
			{
				try
				{
					i = (int)Payload;
				}
				catch
				{
					i = 0;
				}
			}
			return i;
		}

		/// <summary>
		/// Gets the payload as an array of ints.
		/// </summary>
		/// <returns>The payload as an array of ints.</returns>
		public int[] GetIntArray()
		{
			int[] ia = new int[0];
			if (Payload is not null)
			{
				try
				{
					ia = (int[])Payload;
				}
				catch
				{
					ia = new int[0];
				}
			}
			return ia;
		}

		#endregion
		#region UInt

		/// <summary>
		/// Gets the payload as an uint.
		/// </summary>
		/// <returns>The payload as an uint.</returns>
		public uint GetUInt()
		{
			uint u = 0;
			if (Payload is not null)
			{

				try
				{
					u = (uint)Payload;
				}
				catch
				{
					u = 0;
				}
			}
			return u;
		}

		/// <summary>
		/// Gets the payload as an array of uints.
		/// </summary>
		/// <returns>The payload as an array of uints.</returns>
		public uint[] GetUIntArray()
		{
			uint[] ua = new uint[0];
			if (Payload is not null)
			{
				try
				{
					ua = (uint[])Payload;
				}
				catch
				{
					ua = new uint[0];
				}
			}
			return ua;
		}

		#endregion
		#region Short

		/// <summary>
		/// Gets the payload as a short.
		/// </summary>
		/// <returns>The payload as a short.</returns>
		public short GetShort()
		{
			short s = 0;
			if (Payload is not null)
			{
				try
				{
					s = (short)Payload;
				}
				catch
				{
					s = 0;
				}
			}
			return s;
		}

		/// <summary>
		/// Gets the payload as an array of shorts.
		/// </summary>
		/// <returns>The payload as an array of shorts.</returns>
		public short[] GetShortArray()
		{
			short[] sa = new short[0];
			if (Payload is not null)
			{
				try
				{
					sa = (short[])Payload;
				}
				catch
				{
					sa = new short[0];
				}
			}
			return sa;
		}

		#endregion
		#region UShort

		/// <summary>
		/// Gets the payload as a ushort.
		/// </summary>
		/// <returns>The payload as a ushort.</returns>
		public ushort GetUShort()
		{
			ushort u = 0;
			if (Payload is not null)
			{
				try
				{
					u = (ushort)Payload;
				}
				catch
				{
					u = 0;
				}
			}
			return u;
		}

		/// <summary>
		/// Gets the payload as an array of ushorts.
		/// </summary>
		/// <returns>The payload as an array of ushorts.</returns>
		public ushort[] GetUShortArray()
		{
			ushort[] us = Array.Empty<ushort>();
			if (Payload is not null)
			{
				try
				{
					us = (ushort[])Payload;
				}
				catch
				{
					us = new ushort[0];
				}
			}
			return us;
		}

		#endregion
		#region Long

		/// <summary>
		/// Gets the payload as a long.
		/// </summary>
		/// <returns>The payload as a long.</returns>
		public long GetLong()
		{
			long s = 0;
			if (Payload is not null)
			{
				try
				{
					s = (long)Payload;
				}
				catch
				{
					s = 0;
				}
			}
			return s;
		}

		/// <summary>
		/// Gets the payload as an array of longs.
		/// </summary>
		/// <returns>The payload as an array of longs.</returns>
		public long[] GetLongArray()
		{
			long[] sa = new long[0];
			if (Payload is not null)
			{
				try
				{
					sa = (long[])Payload;
				}
				catch
				{
					sa = new long[0];
				}
			}
			return sa;
		}

		#endregion
		#region ULong

		/// <summary>
		/// Gets the payload as a ulong.
		/// </summary>
		/// <returns>The payload as a ulong.</returns>
		public ulong GetULong()
		{
			ulong s = 0;
			if (Payload is not null)
			{
				try
				{
					s = (ulong)Payload;
				}
				catch
				{
					s = 0;
				}
			}
			return s;
		}

		/// <summary>
		/// Gets the payload as an array of ulongs.
		/// </summary>
		/// <returns>The payload as an array of ulongs.</returns>
		public ulong[] GetULongArray()
		{
			ulong[] sa = new ulong[0];
			if (Payload is not null)
			{
				try
				{
					sa = (ulong[])Payload;
				}
				catch
				{
					sa = new ulong[0];
				}
			}
			return sa;
		}

		#endregion
		#region DateTime

		public DateTime GetDateTime()
		{
			DateTime dt = DateTime.MinValue;
			if (Payload is not null)
			{
				try
				{
					dt = (DateTime)Payload;
				}
				catch
				{
					dt = DateTime.MinValue;
				}
			}
			return dt;
		}

		#endregion
	}
}
