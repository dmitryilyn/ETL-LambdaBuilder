﻿using org.ohdsi.cdm.framework.common.Builder;
using org.ohdsi.cdm.framework.common.Omop;
using System;
using System.Collections.Generic;
using System.Data;

namespace org.ohdsi.cdm.framework.common.DataReaders.v5
{
    public class VisitOccurrenceDataReader(List<VisitOccurrence> batch, KeyMasterOffsetManager o) : IDataReader
    {
        private readonly IEnumerator<VisitOccurrence> _visitEnumerator = batch?.GetEnumerator();

        private readonly KeyMasterOffsetManager _offset = o;

        public bool Read()
        {
            return _visitEnumerator.MoveNext();
        }

        public int FieldCount
        {
            get { return 12; }
        }

        // is this called only because the datatype specific methods are not implemented?  
        // probably performance to be gained by not passing object back?
        public object GetValue(int i)
        {
            switch (i)
            {
                case 0:
                    {
                        return _offset.GetKeyOffset(_visitEnumerator.Current.PersonId)
                            .VisitOccurrenceIdChanged
                            ? _offset.GetId(_visitEnumerator.Current.PersonId, _visitEnumerator.Current.Id)
                            : _visitEnumerator.Current.Id;
                    }
                case 1:
                    return _visitEnumerator.Current.PersonId;
                case 2:
                    return _visitEnumerator.Current.ConceptId;
                case 3:
                    return _visitEnumerator.Current.StartDate;
                case 4:
                    return _visitEnumerator.Current.StartDate.TimeOfDay;
                case 5:
                    return _visitEnumerator.Current.EndDate;
                case 6:
                    return _visitEnumerator.Current.EndDate?.TimeOfDay;
                case 7:
                    return _visitEnumerator.Current.TypeConceptId;
                case 8:
                    return _visitEnumerator.Current.ProviderId == 0 ? null : _visitEnumerator.Current.ProviderId;
                case 9:
                    return _visitEnumerator.Current.CareSiteId == 0 ? null : _visitEnumerator.Current.CareSiteId;
                case 10:
                    return _visitEnumerator.Current.SourceValue;
                case 11:
                    return _visitEnumerator.Current.SourceConceptId;

                default:
                    throw new NotImplementedException();
            }
        }

        public string GetName(int i)
        {
            return i switch
            {
                0 => "Id",
                1 => "PersonId",
                2 => "ConceptId",
                3 => "StartDate",
                4 => "StartTime",
                5 => "EndDate",
                6 => "EndTime",
                7 => "TypeConceptId",
                8 => "ProviderId",
                9 => "CareSiteId",
                10 => "SourceValue",
                11 => "SourceConceptId",
                _ => throw new NotImplementedException(),
            };
        }

        #region implementationn not required for SqlBulkCopy

        public bool NextResult()
        {
            throw new NotImplementedException();
        }

        public void Close()
        {
            throw new NotImplementedException();
        }

        public bool IsClosed
        {
            get { throw new NotImplementedException(); }
        }

        public int Depth
        {
            get { throw new NotImplementedException(); }
        }

        public DataTable GetSchemaTable()
        {
            throw new NotImplementedException();
        }

        public int RecordsAffected
        {
            get { throw new NotImplementedException(); }
        }

        public void Dispose()
        {
            throw new NotImplementedException();
        }

        public bool GetBoolean(int i)
        {
            return (bool)GetValue(i);
        }

        public byte GetByte(int i)
        {
            return (byte)GetValue(i);
        }

        public long GetBytes(int i, long fieldOffset, byte[] buffer, int bufferoffset, int length)
        {
            throw new NotImplementedException();
        }

        public char GetChar(int i)
        {
            return (char)GetValue(i);
        }

        public long GetChars(int i, long fieldoffset, char[] buffer, int bufferoffset, int length)
        {
            throw new NotImplementedException();
        }

        public IDataReader GetData(int i)
        {
            throw new NotImplementedException();
        }

        public string GetDataTypeName(int i)
        {
            throw new NotImplementedException();
        }

        public DateTime GetDateTime(int i)
        {
            return (DateTime)GetValue(i);
        }

        public decimal GetDecimal(int i)
        {
            return (decimal)GetValue(i);
        }

        public double GetDouble(int i)
        {
            return Convert.ToDouble(GetValue(i));
        }

        public Type GetFieldType(int i)
        {
            return i switch
            {
                0 => typeof(long),
                1 => typeof(long),
                2 => typeof(long),
                3 => typeof(DateTime),
                4 => typeof(string),
                5 => typeof(DateTime),
                6 => typeof(string),
                7 => typeof(long?),
                8 => typeof(long?),
                9 => typeof(int?),
                10 => typeof(string),
                11 => typeof(long?),
                _ => throw new NotImplementedException(),
            };
        }

        public float GetFloat(int i)
        {
            return (float)GetValue(i);
        }

        public Guid GetGuid(int i)
        {
            return (Guid)GetValue(i);
        }

        public short GetInt16(int i)
        {
            return (short)GetValue(i);
        }

        public int GetInt32(int i)
        {
            return (int)GetValue(i);
        }

        public long GetInt64(int i)
        {
            return Convert.ToInt64(GetValue(i));
        }

        public int GetOrdinal(string name)
        {
            throw new NotImplementedException();
        }

        public string GetString(int i)
        {
            return (string)GetValue(i);
        }

        public int GetValues(object[] values)
        {
            var cnt = 0;
            for (var i = 0; i < FieldCount; i++)
            {
                values[i] = GetValue(i);
                cnt++;
            }

            return cnt;
        }

        public bool IsDBNull(int i)
        {
            return GetValue(i) == null;
        }

        public object this[string name]
        {
            get { throw new NotImplementedException(); }
        }

        public object this[int i]
        {
            get { throw new NotImplementedException(); }
        }

        #endregion
    }
}
