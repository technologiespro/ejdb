// ============================================================================================
//   .NET API for EJDB database library http://ejdb.org
//   Copyright (C) 2012-2013 Softmotions Ltd <info@softmotions.com>
//
//   This file is part of EJDB.
//   EJDB is free software; you can redistribute it and/or modify it under the terms of
//   the GNU Lesser General Public License as published by the Free Software Foundation; either
//   version 2.1 of the License or any later version.  EJDB is distributed in the hope
//   that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of
//   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU Lesser General Public
//   License for more details.
//   You should have received a copy of the GNU Lesser General Public License along with EJDB;
//   if not, write to the Free Software Foundation, Inc., 59 Temple Place, Suite 330,
//   Boston, MA 02111-1307 USA.
// ============================================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Diagnostics;
using Ejdb.IO;

namespace Ejdb.SON {

	/// <summary>
	/// BSON document deserialized data wrapper.
	/// </summary>
	[Serializable]
	public class BSONDocument : IBSONValue {
		Dictionary<string, BSONValue> _fields;
		readonly List<BSONValue> _fieldslist;

		public virtual BSONType BSONType {
			get {
				return BSONType.OBJECT;
			}
		}

		public BSONDocument() {
			this._fields = null;
			this._fieldslist = new List<BSONValue>();
		}

		public BSONDocument(BSONIterator it) : this() {
			while (it.Next() != BSONType.EOO) {
				Add(it.FetchCurrentValue());
			}
		}

		public BSONDocument(byte[] bsdata) : this() {
			using (BSONIterator it = new BSONIterator(bsdata)) {
				while (it.Next() != BSONType.EOO) {
					Add(it.FetchCurrentValue());
				}
			}
		}

		public BSONDocument(Stream bstream) : this() {
			using (BSONIterator it = new BSONIterator(bstream)) {
				while (it.Next() != BSONType.EOO) {
					Add(it.FetchCurrentValue());
				}
			}
		}

		public BSONDocument Add(BSONValue bv) {
			_fieldslist.Add(bv);
			if (_fields != null) {
				_fields.Add(bv.Key, bv);
			}
			return this;
		}

		public BSONValue this[string key] {
			get { 
				return GetBSONValue(key);
			}
			set {
				SetBSONValue(key, value);
			}
		}

		public BSONValue GetBSONValue(string key) {
			CheckFields();
			return _fields[key];
		}

		public void SetBSONValue(string key, BSONValue val) {
			CheckFields();
			var ov = _fields[key];
			if (ov != null) {
				ov.Key = val.Key;
				ov.BSONType = val.BSONType;
				ov.Value = val.Value;
			} else {
				_fieldslist.Add(val);
				_fields[key] = val;
			}
		}

		public object GetObjectValue(string key) {
			CheckFields();
			var bv = _fields[key];
			return bv != null ? bv.Value : null;
		}

		public BSONValue SetObjectValue(string key, object value) {
			throw new NotImplementedException();
		}

		public BSONValue DropValue(string key) {
			var bv = this[key];
			if (bv == null) {
				return bv;
			}
			_fields.Remove(key);
			_fieldslist.RemoveAll(x => x.Key == key);
			return bv;
		}

		public void Serialize(Stream os) {
			if (os.CanSeek) {
				long start = os.Position;
				os.Position += 4; //skip int32 document size
				using (var bw = new ExtBinaryWriter(os, Encoding.UTF8, true)) {
					foreach (BSONValue bv in _fieldslist) {
						WriteBSONValue(bv, bw);
					}
					bw.Write((byte) 0x00);
					long end = os.Position;
					os.Position = start;
					bw.Write((int) (end - start));
					os.Position = end; //go to the end
				}
			} else {
				byte[] darr;
				var ms = new MemoryStream();
				using (var bw = new ExtBinaryWriter(ms)) {
					foreach (BSONValue bv in _fieldslist) {
						WriteBSONValue(bv, bw);
					}
					darr = ms.ToArray();
				}	
				using (var bw = new ExtBinaryWriter(os, Encoding.UTF8, true)) {
					bw.Write(darr.Length + 4/*doclen*/ + 1/*0x00*/);
					bw.Write(darr);
					bw.Write((byte) 0x00); 
				}
			}
		}
		//.//////////////////////////////////////////////////////////////////
		// 						Private staff										  
		//.//////////////////////////////////////////////////////////////////
		protected void WriteBSONValue(BSONValue bv, ExtBinaryWriter bw) {
			BSONType bt = bv.BSONType;
			switch (bt) {
				case BSONType.EOO:
					break;
				case BSONType.NULL:
				case BSONType.UNDEFINED:	
				case BSONType.MAXKEY:
				case BSONType.MINKEY:
					WriteTypeAndKey(bv, bw);
					break;
				case BSONType.OID:
					{
						WriteTypeAndKey(bv, bw);
						BSONOid oid = (BSONOid) bv.Value;
						Debug.Assert(oid.Bytes.Length == 12);
						bw.Write(oid.Bytes);
						break;
					}
				case BSONType.STRING:
				case BSONType.CODE:
				case BSONType.SYMBOL:										
					WriteTypeAndKey(bv, bw);
					bw.WriteBSONString((string) bv.Value);
					break;
				case BSONType.BOOL:
					WriteTypeAndKey(bv, bw);
					bw.Write((bool) bv.Value);
					break;
				case BSONType.INT:
					WriteTypeAndKey(bv, bw);
					bw.Write((int) bv.Value);
					break;
				case BSONType.LONG:
					WriteTypeAndKey(bv, bw);
					bw.Write((long) bv.Value);
					break;
				case BSONType.ARRAY:
				case BSONType.OBJECT:
					{					
						BSONDocument doc = (BSONDocument) bv.Value;
						WriteTypeAndKey(bv, bw);
						doc.Serialize(bw.BaseStream);
						break;
					}
				case BSONType.DATE:
					{	
						DateTime dt = (DateTime) bv.Value;
						var diff = dt.ToUniversalTime() - BSONConstants.Epoch;
						long time = (long) Math.Floor(diff.TotalMilliseconds);
						WriteTypeAndKey(bv, bw);
						bw.Write(time);
						break;
					}
				case BSONType.DOUBLE:
					WriteTypeAndKey(bv, bw);
					bw.Write((double) bv.Value);
					break;	
				case BSONType.REGEX:
					{
						BSONRegexp rv = (BSONRegexp) bv.Value;		
						WriteTypeAndKey(bv, bw);
						bw.WriteCString(rv.Re ?? "");
						bw.WriteCString(rv.Opts ?? "");						
						break;
					}				
				case BSONType.BINDATA:
					{						
						BSONBinData bdata = (BSONBinData) bv.Value;
						WriteTypeAndKey(bv, bw);
						bw.Write(bdata.Data.Length);
						bw.Write(bdata.Subtype);
						bw.Write(bdata.Data);						
						break;		
					}
				case BSONType.DBREF:
					//Unsupported DBREF!
					break;
				case BSONType.TIMESTAMP:
					{
						BSONTimestamp ts = (BSONTimestamp) bv.Value;
						WriteTypeAndKey(bv, bw);
						bw.Write(ts.Inc);
						bw.Write(ts.Ts);
						break;										
					}		
				case BSONType.CODEWSCOPE:
					{
						BSONCodeWScope cw = (BSONCodeWScope) bv.Value;						
						WriteTypeAndKey(bv, bw);						
						using (var cwwr = new ExtBinaryWriter(new MemoryStream())) {							
							cwwr.WriteBSONString(cw.Code);
							cw.Scope.Serialize(cwwr.BaseStream);					
							byte[] cwdata = ((MemoryStream) cwwr.BaseStream).ToArray();
							bw.Write(cwdata.Length);
							bw.Write(cwdata);
						}
						break;
					}
				default:
					throw new InvalidBSONDataException("Unknown entry type: " + bt);											
			}		
		}

		protected void WriteTypeAndKey(BSONValue bv, ExtBinaryWriter bw) {
			bw.Write((byte) bv.BSONType);
			bw.WriteBSONString(bv.Key);
		}

		protected void CheckFields() {
			if (_fields != null) {
				return;
			}
			_fields = new Dictionary<string, BSONValue>(Math.Max(_fieldslist.Count + 1, 32));
			foreach (var bv in _fieldslist) {
				_fields.Add(bv.Key, bv);
			}
		}
	}
}
