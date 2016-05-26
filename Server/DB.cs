using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.IO;


namespace Server
{
    public static class DB
    {
        static string dbConnectionString;
        static string dbPath;

        /// <summary>
        /// Setto i parametri di connessione
        /// </summary>
        /// <param name="dbPath">Path del db</param>
        public static void SetDbConn(string dbPath)
        {
            DB.dbPath = dbPath;
            
            dbConnectionString = string.Format(@"Data Source={0}; Pooling=false; FailIfMissing=false; Version=3;", dbPath);
            //dbConnectionString = string.Format(@"Data Source={0}; Version=3;", dbPath);
        }

        /// <summary>
        /// Ritorno la stringa per aprire una nuova connessione
        /// </summary>
        /// <returns></returns>
        public static string GetConnectionString()
        {
            return DB.dbConnectionString;
        }

        /// <summary>
        /// Controllo la presenza del database, se non esiste lo creo
        /// </summary>
        public static void CheckDB()
        {
            // Controllo se il DB esiste
            if (!File.Exists(DB.dbPath))
            {
                // Se il file non esiste devo creare il DB da zero
                SQLiteConnection.CreateFile(DB.dbPath);
                DB.createDB();
            }
            else
            {
                // Se esiste invece, controllo la presenza di tutte le tabelle
                // TODO completare
                if (!(DB.TableExists("Utenti")) && !(DB.TableExists("Versioni")))
                {
                    DB.ClearDB();
                    DB.createDB();
                }
            }
            
        }

        /// <summary>
        /// Creo le varie tabelle nel db
        /// </summary>
        private static void createDB()
        {
            string sql = @" --
                            -- File generated with SQLiteStudio v3.0.7 on mer apr 27 10:49:52 2016
                            --
                            -- Text encoding used: windows-1252
                            --
                            PRAGMA foreign_keys = off;
                            BEGIN TRANSACTION;

                            -- Table: Utenti
                            DROP TABLE IF EXISTS Utenti;

                            CREATE TABLE Utenti (
                                UID      INTEGER NOT NULL
                                                    PRIMARY KEY AUTOINCREMENT,
                                Username TEXT    NOT NULL
                                                    UNIQUE,
                                Password TEXT    NOT NULL,
                                AuthToken STRING  NOT NULL,
                                InSynch  BOOLEAN DEFAULT FALSE
                                                    NOT NULL
                            );

                            -- Table: Versioni
                            DROP TABLE IF EXISTS Versioni;

                            CREATE TABLE Versioni (
                                UID         INTEGER NOT NULL,
                                VersionID   INTEGER NOT NULL,
                                PathClient  TEXT    NOT NULL,
                                MD5         TEXT    NOT NULL,
                                LastModTime TEXT    NOT NULL,
                                Size        INTEGER NOT NULL,
                                PathServer  TEXT    NOT NULL,
                                LastVersion BOOLEAN NOT NULL,
                                Deleted     BOOLEAN NOT NULL
                                                    DEFAULT FALSE,
                                PRIMARY KEY (
                                    UID,
                                    VersionID,
                                    PathClient
                                ),
                                FOREIGN KEY (
                                    UID
                                )
                                REFERENCES Utenti
                            );



                            COMMIT TRANSACTION;
                            PRAGMA foreign_keys = on;

                            ";
            DB.ExecuteNonQuery(sql);
        }
        

        /// <summary>  
        /// Allows the programmer to run a query against the Database.  
        /// </summary>  
        /// <param name="sql">The SQL to run</param>  
        /// <returns>A DataTable containing the result set.</returns>  
        public static DataTable GetDataTable(string sql)
        {
            DataTable dt = new DataTable();
            try
            {
                using (SQLiteConnection cnn = new SQLiteConnection(DB.GetConnectionString()))
                {
                    cnn.Open();
                    using (SQLiteCommand mycommand = cnn.CreateCommand())
                    {
                        mycommand.CommandText = sql;

                        using (SQLiteDataReader rdr = mycommand.ExecuteReader())
                        {
                            dt.Load(rdr);
                        }
                    }
                }
            }
            catch (Exception)
            {
                throw;
            }
            return dt;
        }

        /// <summary>  
        /// Allows the programmer to interact with the database for purposes other than a query.  
        /// </summary>  
        /// <param name="sql">The SQL to be run.</param>  
        /// <returns>An Integer containing the number of rows updated.</returns>  
        public static int ExecuteNonQuery(string sql)
        {
            int ret = 0;
            using (SQLiteConnection cnn = new SQLiteConnection(DB.GetConnectionString()))
            {
                cnn.Open();
                using (SQLiteCommand mycommand = cnn.CreateCommand())
                {
                    mycommand.CommandText = sql;
                    ret = mycommand.ExecuteNonQuery();

                }
            }
            return ret;
        }

        /// <summary>  
        /// Allows the programmer to retrieve single items from the DB.  
        /// </summary>  
        /// <param name="sql">The query to run.</param>  
        /// <returns>A string.</returns>  
        public static string ExecuteScalar(string sql)
        {
            string ret = "";
            using (SQLiteConnection cnn = new SQLiteConnection(DB.GetConnectionString()))
            {
                cnn.Open();
                using (SQLiteCommand mycommand = cnn.CreateCommand())
                {
                    mycommand.CommandText = sql;
                    object value = mycommand.ExecuteScalar();
                    if (value != null)
                    {
                        return value.ToString();
                    }
                }
            }
            return ret;
        }

        #region UPDATE DELETE INSERT

        /// <summary>  
        ///     Allows the programmer to easily update rows in the DB.  
        /// </summary>  
        /// <param name="tableName">The table to update.</param>  
        /// <param name="data">A dictionary containing Column names and their new values.</param>  
        /// <param name="where">The where clause for the update statement.</param>  
        /// <returns>A boolean true or false to signify success or failure.</returns>  
        public static bool Update(String tableName, Dictionary<String, String> data, String where)
        {
            String vals = "";
            Boolean returnCode = true;
            if (data.Count >= 1)
            {
                foreach (KeyValuePair<String, String> val in data)
                {
                    vals += String.Format(" {0} = '{1}',", val.Key.ToString(), val.Value.ToString());
                }
                vals = vals.Substring(0, vals.Length - 1);
            }
            try
            {
                DB.ExecuteNonQuery(String.Format("update {0} set {1} where {2};", tableName,
                                       vals, where));
            }
            catch (Exception)
            {
                returnCode = false;
                //ServiceLogWriter.LogError(ex);  
            }
            return returnCode;
        }

        /// <summary>  
        ///     Allows the programmer to easily delete rows from the DB.  
        /// </summary>  
        /// <param name="tableName">The table from which to delete.</param>  
        /// <param name="where">The where clause for the delete.</param>  
        /// <returns>A boolean true or false to signify success or failure.</returns>  
        public static bool Delete(String tableName, String where)
        {
            Boolean returnCode = true;
            try
            {
                DB.ExecuteNonQuery(String.Format("delete from {0} where {1};", tableName, where));
            }
            catch (Exception)
            {
                returnCode = false;
            }
            return returnCode;
        }

        /// <summary>  
        ///     Allows the programmer to easily insert into the DB  
        /// </summary>  
        /// <param name="tableName">The table into which we insert the data.</param>  
        /// <param name="data">A dictionary containing the column names and data for the insert.</param>  
        /// <returns>A boolean true or false to signify success or failure.</returns>  
        public static bool Insert(String tableName, Dictionary<String, String> data)
        {
            String columns = "";
            String values = "";
            Boolean returnCode = true;
            foreach (KeyValuePair<String, String> val in data)
            {
                columns += String.Format(" {0},", val.Key.ToString());
                values += String.Format(" '{0}',", val.Value);
            }
            columns = columns.Substring(0, columns.Length - 1);
            values = values.Substring(0, values.Length - 1);
            try
            {
                DB.ExecuteNonQuery(String.Format("insert into {0}({1}) values({2});", tableName, columns, values));
            }
            catch (Exception)
            {
                returnCode = false;
            }
            return returnCode;
        }

        #endregion

        /// <summary>  
        /// Allows the programmer to easily delete all data from the DB.  
        /// </summary>  
        /// <returns>A boolean true or false to signify success or failure.</returns>  
        public static bool ClearDB()
        {
            DataTable tables;
            try
            {
                tables = DB.GetDataTable("select NAME from SQLITE_MASTER where type= 'table' order by NAME;");
                foreach (DataRow table in tables.Rows)
                {
                    DB.ClearTable(table["NAME"].ToString());
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        #region ClearTable TableExists

        /// <summary>  
        ///     Allows the user to easily clear all data from a specific table.  
        /// </summary>  
        /// <param name="table">The name of the table to clear.</param>  
        /// <returns>A boolean true or false to signify success or failure.</returns>  
        public static bool ClearTable(String table)
        {
            try
            {
                DB.ExecuteNonQuery(String.Format("delete from {0};", table));
                return true;
            }
            catch
            {
                return false;
            }
        }
               

        /// <summary>  
        /// Allows the programmer to easily test if table exists in the DB.  
        /// </summary>  
        /// <returns>A boolean true or false to signify success or failure.</returns>  
        public static bool TableExists(String tableName)
        {
            string count = "0";
            if (dbConnectionString == default(string))
                return false;
            using (SQLiteConnection cnn = new SQLiteConnection(dbConnectionString))
            {
                try
                {
                    cnn.Open();
                    if (tableName == null || cnn.State != ConnectionState.Open)
                    {
                        return false;
                    }
                    String sql = string.Format("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name ='{0}'", tableName);
                    count = ExecuteScalar(sql);
                }
                finally
                {
                    // Close the database connection  
                    if ((cnn != null) && (cnn.State != ConnectionState.Open))
                        cnn.Close();
                }
            }
            return Convert.ToInt32(count) > 0;
        }

        #endregion
        
        /// <summary>  
        /// Allows the programmer to easily test connect to the DB.  
        /// </summary>  
        /// <returns>A boolean true or false to signify success or failure.</returns>  
        public static bool TestConnection()
        {
            using (SQLiteConnection cnn = new SQLiteConnection(dbConnectionString))
            {
                try
                {
                    cnn.Open();
                    return true;
                }
                catch
                {
                    return false;
                }
                finally
                {
                    // Close the database connection  
                    if ((cnn != null) && (cnn.State != ConnectionState.Open))
                        cnn.Close();
                }
            }
        }
    }
}
