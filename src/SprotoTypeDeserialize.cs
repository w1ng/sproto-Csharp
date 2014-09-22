﻿using System;
using System.IO;
using System.Collections.Generic;

namespace Sproto
{
	public class SprotoTypeDeserialize {

		private SprotoTypeReader reader;
		private long begin_data_pos;
		private long cur_field_pos;

		private int fn;
		private int tag = -1;
		private int value;

		public SprotoTypeDeserialize (byte[] data) {
			this.reader = new SprotoTypeReader (data, 0, data.LongLength);
			this.init ();
		}

		public SprotoTypeDeserialize(SprotoTypeReader reader) {
			this.reader = reader;
			this.init ();
		}


		private void init() {
			this.fn = this.read_word ();

			long header_length = SprotoTypeSize.sizeof_header + this.fn * SprotoTypeSize.sizeof_field;
			this.begin_data_pos = header_length;
			this.cur_field_pos = this.reader.Position;

			if (this.reader.Length < header_length) {
				SprotoTypeSize.error ("invalid decode header.");
			}

			this.reader.Seek (this.begin_data_pos);
		}

		private UInt64 expand64(UInt32 v) {
			UInt64 value = (UInt64)v;
			if ( (value & 0x80000000) != 0) {
				value |= (UInt64)(0xffffffff00000000);
			}
			return value;
		}

		private int read_word() {
			return (int)this.reader.ReadByte () |
				((int)this.reader.ReadByte ()) << 8;
		}

		private UInt32 read_dword() {
			return 	(UInt32)this.reader.ReadByte ()    |
				((UInt32)this.reader.ReadByte ()) << 8 |
				((UInt32)this.reader.ReadByte ()) << 16|
				((UInt32)this.reader.ReadByte ()) << 24;
		}

		private UInt32 read_array_size() {
			if (this.value >= 0)
				SprotoTypeSize.error ("invalid array value.");

			UInt32 sz = this.read_dword ();
			if (sz < 1)
				SprotoTypeSize.error ("error array size("+sz+")");

			return sz;
		}


		public int read_tag() {
			long pos = this.reader.Position;
			this.reader.Seek (this.cur_field_pos);

			while(this.reader.Position < this.begin_data_pos){
				this.tag++;
				int value = this.read_word ();

				if( (value & 1) == 0) {
					this.cur_field_pos = this.reader.Position;
					this.reader.Seek(pos);
					this.value = value/2 - 1;
					return this.tag;
				}

				this.tag += value/2;
			}


			this.reader.Seek(pos);
			return -1;
		}


		public Int64 read_integer() {
			if (this.value >= 0) {
				return (Int64)(this.value);
			} else {
				UInt32 sz = this.read_dword ();
				if (sz == sizeof(UInt32)) {
					UInt64 v = this.expand64 (this.read_dword ());
					return (Int64)v;
				} else if (sz == sizeof(UInt64)) {
					UInt32 low = this.read_dword ();
					UInt32 hi  = this.read_dword (); 
					UInt64 v = (UInt64)low | (UInt64)hi << 32;
					return (Int64)v;
				} else {
					SprotoTypeSize.error ("read invalid integer size (" + sz + ")");
				}
			}

			return 0;
		}

		public List<Int64> read_integer_list() {
			List<Int64> integer_list = null;

			UInt32 sz = this.read_array_size ();
			int len = this.reader.ReadByte ();
			sz--;

			if (len == sizeof(UInt32)) {
				if (sz % sizeof(UInt32) != 0) {
					SprotoTypeSize.error ("error array size("+sz+")@sizeof(Uint32)");
				}

				integer_list = new List<Int64> ();
				for (int i = 0; i < sz / sizeof(UInt32); i++) {
					UInt64 v = this.expand64 (this.read_dword ());
					integer_list.Add ((Int64)v);
				}

			} else if (len == sizeof(UInt64)) {
				if (sz % sizeof(UInt64) != 0) {
					SprotoTypeSize.error ("error array size("+sz+")@sizeof(Uint64)");
				}

				integer_list = new List<Int64> ();
				for (int i = 0; i < sz / sizeof(UInt64); i++) {
					UInt32 low = this.read_dword ();
					UInt32 hi  = this.read_dword (); 
					UInt64 v = (UInt64)low | (UInt64)hi << 32;
					integer_list.Add ((Int64)v);
				}
			
			} else {
				SprotoTypeSize.error ("error intlen("+len+")");
			}

			return integer_list;
		}


		public bool read_boolean() {
			if (this.value < 0) {
				SprotoTypeSize.error ("read invalid boolean.");
				return false;
			} else {
				return (this.value ==0)?(false):(true);
			}
		}

		public List<bool> read_boolean_list() {
			UInt32 sz = this.read_array_size ();

			List<bool> boolean_list = new List<bool> ();
			for (int i = 0; i < sz; i++) {
				bool v = (this.reader.ReadByte() == (byte)0)?(false):(true);
				boolean_list.Add (v);
			}

			return boolean_list;
		}


		public string read_string() {
			UInt32 sz = this.read_dword ();
			byte[] buffer = new byte[sz];
			this.reader.Read (buffer, 0, buffer.Length);
			return System.Text.Encoding.UTF8.GetString (buffer);
		}

		public List<string> read_string_list() {
			UInt32 sz = this.read_array_size ();

			List<string> string_list = new List<string> ();
			for (UInt32 i = 0; sz > 0; i++) {
				if (sz < SprotoTypeSize.sizeof_length) {
					SprotoTypeSize.error ("error array size.");
				}

				UInt32 hsz = this.read_dword ();
				sz -= (UInt32)SprotoTypeSize.sizeof_length;

				if (hsz > sz) {
					SprotoTypeSize.error ("error array object.");
				}

				byte[] buffer = new byte[hsz];
				this.reader.Read (buffer, 0, buffer.Length);
				string v = System.Text.Encoding.UTF8.GetString (buffer);

				string_list.Add (v);
				sz -= hsz;
			}

			return string_list;
		}


		public T read_obj<T>() where T : SprotoTypeBase, new() {
			UInt32 sz = this.read_dword ();

			SprotoTypeReader reader = new SprotoTypeReader (this.reader.Buffer, this.reader.Offset, sz);
			this.reader.Seek (this.reader.Position + sz);

			T obj = new T ();
			obj.init (reader);
			return obj;
		}

		public List<T> read_obj_list<T>() where T : SprotoTypeBase, new() {
			UInt32 sz = this.read_array_size ();

			List<T> obj_list = new List<T> ();
			for (UInt32 i = 0; sz > 0; i++) {
				if (sz < SprotoTypeSize.sizeof_length) {
					SprotoTypeSize.error ("error array size.");
				}

				UInt32 hsz = this.read_dword ();
				sz -= (UInt32)SprotoTypeSize.sizeof_length;

				if (hsz > sz) {
					SprotoTypeSize.error ("error array object.");
				}

				SprotoTypeReader reader = new SprotoTypeReader (this.reader.Buffer, this.reader.Offset, hsz);
				this.reader.Seek (this.reader.Position + hsz);

				T obj = new T();
				obj.init (reader);
				obj_list.Add (obj);

				sz -= hsz;
			}

			return obj_list;
		}



		public void read_unknow_data() {
			if (this.value < 0) {
				UInt32 sz = this.read_dword ();
				this.reader.Seek (sz + this.reader.Position);
			}
		}


		public void clear() {
			this.fn = 0;
			this.tag = -1;
			this.value = 0;
			this.reader.Seek (0);
		}
	}
}

