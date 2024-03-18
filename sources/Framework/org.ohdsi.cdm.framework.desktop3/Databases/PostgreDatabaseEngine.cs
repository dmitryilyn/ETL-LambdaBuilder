﻿using org.ohdsi.cdm.framework.common.Definitions;
using org.ohdsi.cdm.framework.desktop.Enums;
using org.ohdsi.cdm.framework.desktop.Savers;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Odbc;

namespace org.ohdsi.cdm.framework.desktop.Databases
{
    public class PostgreDatabaseEngine : DatabaseEngine
    {
        public PostgreDatabaseEngine()
        {
            Database = Database.Postgre;
        }

        public override IEnumerable<string> GetAllTables()
        {
            throw new NotImplementedException("PostgreDatabaseEngine.GetAllTables()");
        }

        public override IEnumerable<string> GetAllColumns(string tableName)
        {
            throw new NotImplementedException("PostgreDatabaseEngine.GetAllColumns()");
        }

        public override ISaver GetSaver()
        {
            return new PostgreSaver();
        }

        public override IDbCommand GetCommand(string cmdText, IDbConnection connection)
        {
            var c = new OdbcCommand(cmdText, (OdbcConnection)connection);

            return c;
        }

        public override IDataReader ReadChunkData(IDbConnection conn, IDbCommand cmd, QueryDefinition qd, int chunkId,
            string prefix)
        {
            return cmd.ExecuteReader();
        }
    }
}