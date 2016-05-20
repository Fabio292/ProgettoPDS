using System;
using System.Data.SQLite;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;

namespace Server
{
    class ServerListener
    {
        private TcpListener serverListener = null;

        // TODO fare un singleton
        public ServerListener()
        {
            // TODO spostare nella configurazione
            Int32 port = 10000;
            IPAddress localAddr = IPAddress.Parse("127.0.0.1");

            // TcpListener server = new TcpListener(port);
            this.serverListener = new TcpListener(IPAddress.Any, port);

            // TODO gestisco il DB
            DB.SetDbConn(Constants.ServerDBPath);

            //TODO gestire correttamente PROVA --> creo il DB
            DB.CheckDB();

            // Start listening for client requests.
            serverListener.Start();
            Logger.Info("Server creato");

        }

        public void Shutdown()
        {
            serverListener.Server.Close();
        }

        public async void ServerStart(CancellationToken ct)
        {
            try
            {
                Logger.Info("Server in ascolto");
                while (!ct.IsCancellationRequested)
                {
                    TcpClient client = await serverListener.AcceptTcpClientAsync().ConfigureAwait(false);
                    Logger.Info("Ricevuta connessione da " + client.Client.RemoteEndPoint);

                    /* await Task.Factory.StartNew(() =>
                    {
                        this.test();
                        //this.serve_client(client, ct);
                    });*/

                    // Gestisco il client in un thread separato
                    Thread thread = new Thread(() => serveClientSync(client, ct));
                    thread.Start();

                }
            }
            catch (Exception e)
            {
                Logger.Error("Errore in ServerStart:" + e.ToString());
            }
        }

        /// <summary>
        /// FUNZIONE PRINCIPALE PER GESTIONE CLIENT
        /// </summary>
        private void serveClientSync(TcpClient client, CancellationToken ct)
        {
            int userID = -1;

            SQLiteConnection conn = null;
            SQLiteTransaction transaction = null;

            //TODO spostare in configurazione
            client.ReceiveTimeout = 1000000;
            bool exitFlag = false;

            // Ricevo il primo comando
            Command k = Utilis.GetCmdSync(client);
            Logger.Debug("il comando ricevuto dal server e':\nCOMANDO: " + k.kmd + "\nPAYLOAD: " + k.Payload + "\nPAYLOADLENGTH: " + k.PayloadLength);

            if (k == null)
            {
                Logger.Error("Aspettavo un comando, ricevuto nulla");
                // Notifico l'errore al client
                ServerListener.sendError(client, ErrorCode.networkError);
                return;
            }

            try
            {
                // PRIMO COMANDO
                switch (k.kmd)
                {
                    case CmdType.login:
                        ServerListener.userLogin(client, k, ref userID);
                        ServerListener.sendOk(client);
                        break;

                    case CmdType.registration:
                        ServerListener.userRegistration(client, k);
                        // La registrazione è andata a buon fine, chiudo perchè tanto il client deve impostare la configurazione
                        ServerListener.sendOk(client);
                        exitFlag = true;
                        break;

                    default:
                        ServerListener.sendError(client, ErrorCode.unexpectedMessageType);
                        throw new Exception("Mi aspettavo (Login|registration) Ricevuto " + Utilis.Cmd2String(k.kmd));
                }

                // Se arrivo a questo punto ho effettuato correttamente il login               

                // LOOP DI COMANDI
                while ((exitFlag == false) || (ct.IsCancellationRequested == true))
                {
                    k = Utilis.GetCmdSync(client);
                    if (k == null)
                    {
                        Logger.Error("Aspettavo un comando, ricevuto nulla");
                        ServerListener.sendError(client, ErrorCode.networkError);
                        return;
                    }

                    switch (k.kmd)
                    {
                        case CmdType.logout:
                            exitFlag = true;
                            //TODO cancellare conn e transaction
                            // commit
                            break;

                        case CmdType.getXmlDigest:
                            ///invio l'md5 dell'XML dell'ultima versione della cartella
                            ServerListener.sendXmlDigest(client, userID);
                            Logger.Info("ho inviato l'md5 dell'Xml richiesto:");
                            break;

                        case CmdType.getXML:
                            ///invio l'XML dell'ultima versione della cartella
                            ServerListener.sendLastXml(client, userID);
                            Logger.Info("ho inviato l'Xml richiesto");

                            break;

                        case CmdType.sendFile:
                            break;


                        case CmdType.startSynch:
                            ServerListener.startSynch(client, userID, ref conn, ref transaction);

                            // Controllo se ho effettivamente iniziato la sincronizzazione
                            if (conn == null || transaction == null)
                            {
                                ServerListener.sendError(client, ErrorCode.userAlreadyInSynch);
                                Logger.Error("L'utente " + userID + " e' gia in synch");

                                if (transaction != null)
                                {
                                    transaction.Rollback();
                                    transaction.Dispose();
                                    transaction = null;
                                }

                                if (conn != null)
                                {
                                    conn.Close();
                                    conn.Dispose();
                                    conn = null;
                                }                              

                                exitFlag = true;
                            }

                            ServerListener.sendOk(client);
                            ServerListener.getUpdates(client, conn, transaction, userID);

                            break;

                        case CmdType.endSynch:

                            break;

                        default:
                            ServerListener.sendError(client, ErrorCode.unexpectedMessageType);
                            throw new Exception("Mi aspettavo (Logout|getXML|TODO) Ricevuto " + Utilis.Cmd2String(k.kmd));

                    }
                }

            }
            catch (Exception e) // Eccezione non gestita, cerco di recuperare il metodo e la linea 
            {
                // Ottengo il metodo che ha generato l'eccezione

                StackTrace st = new StackTrace(e, true);
                StackFrame sf = Utilis.GetFirstValidFrame(st);

                Logger.Error("Errore gestione client (" + client.Client.RemoteEndPoint + ")[" + Path.GetFileName(sf.GetFileName()) + "(" + sf.GetFileLineNumber() + ")]: " + e.Message);

                // eventualmente chiudo la transaction con un rollback se necessario e la connessione
                if (transaction != null)
                {
                    transaction.Rollback();
                    transaction.Dispose();
                    transaction = null;
                }

                if (conn != null)
                {
                    conn.Close();
                    conn.Dispose();
                    conn = null;
                }              

            }
            finally
            {
                if (transaction != null)
                {
                    Logger.Error("AAAAAAAAAAAAAA ERRORE GRAVISSIMO transazione non nulla nel finally");
                }

                if (conn != null)
                {
                    Logger.Error("AAAAAAAAAAAAAA ERRORE GRAVISSIMO connessione non nulla nel finally");
                }

                client.Close();
            }

        }


        /// <summary>Loggo l'utente secondo i parametri passati</summary>
        /// <exception cref="MyException">Sintassi non corretta nel comando</exception>
        /// <param name="client">Connessione TCP al client</param>
        /// <param name="cmd">Comando ricevuto</param>
        /// <param name="usn">Stringa in cui andare a salvare l'username del client</param>
        /// <returns>TRUE se il login ha avuto successo</returns>
        private static bool userLogin(TcpClient client, Command cmd, ref int userID)
        {
            try
            {
                #region Parsing del comando
                SendCredentials credentials;
                try
                {
                    credentials = new SendCredentials(cmd);
                }
                catch (Exception e)
                {
                    // Notifico l'errore al client
                    ServerListener.sendError(client, ErrorCode.syntaxError);

                    StackTrace st = new StackTrace(e, true);
                    StackFrame sf = Utilis.GetFirstValidFrame(st);

                    throw new MyException(e.Message, Path.GetFileName(sf.GetFileName()), sf.GetFileLineNumber());
                }
                #endregion

                #region Validazione Input
                // Controllo di validità su username e pwd
                int usnLen = credentials.Username.Length;
                if (usnLen < Constants.MinUsernameLength || usnLen > Constants.MaxUsernameLength)
                {
                    ServerListener.sendError(client, ErrorCode.usernameLengthNotValid);
                    throw new Exception("lunghezza username non valida <" + usnLen + ">");

                }

                int pwdLen = credentials.Password.Length;
                if (pwdLen < Constants.MinPasswordLength || pwdLen > Constants.MaxPasswordLength)
                {
                    ServerListener.sendError(client, ErrorCode.passwordLengthNotValid);
                    throw new Exception("lunghezza password non valida <" + pwdLen + ">");

                }
                #endregion

                #region controllo su DB
                using (SQLiteConnection connection = new SQLiteConnection(DB.GetConnectionString()))
                {
                    connection.Open();
                    using (SQLiteTransaction tr = connection.BeginTransaction())
                    {


                        using (SQLiteCommand sqlCmd = connection.CreateCommand())
                        {
                            // Cerco l'utente con le credenziali passate
                            string passMd5 = Utilis.Md5String(credentials.Password);
                            sqlCmd.CommandText = @"SELECT * FROM Utenti WHERE Username=@_username AND Password=@_password";
                            sqlCmd.Parameters.AddWithValue("@_username", credentials.Username);
                            sqlCmd.Parameters.AddWithValue("@_password", passMd5);

                            SQLiteDataReader reader = sqlCmd.ExecuteReader();
                            if (reader.HasRows == false)
                            {
                                ServerListener.sendError(client, ErrorCode.credentialsNotValid);
                                throw new Exception("tentato login con credenziali non valide <" + credentials.Username + "><" + credentials.Password + ">");
                            }
                        }

                        // Cerco l'ID dell'utente
                        using (SQLiteCommand sqlCmd = connection.CreateCommand())
                        {
                            sqlCmd.CommandText = @"SELECT UID FROM Utenti WHERE Username=@_username";
                            sqlCmd.Parameters.AddWithValue("@_username", credentials.Username);

                            //SQLiteDataReader reader = sqlCmd.ExecuteReader();
                            //if (reader.HasRows == false)
                            //{
                            //    ServerListener.sendError(client, ErrorCode.credentialsNotValid);
                            //    throw new Exception("tentato login con credenziali non valide <" + credentials.Username + "><" + credentials.Password + ">");
                            //}

                            userID = Convert.ToInt32(sqlCmd.ExecuteScalar());
                        }
                        tr.Commit();
                    }
                }

                
                #endregion


                Logger.Info("Login con successo per <" + credentials.Username + ">");
            }
            catch (ArgumentException e)
            {
                // Notifico l'errore al client
                ServerListener.sendError(client, ErrorCode.syntaxError);

                StackTrace st = new StackTrace(e, true);
                StackFrame sf = Utilis.GetFirstValidFrame(st);
            }

            return true;
        }

        /// <summary>
        /// Registro l'utente secondo i parametri passati</summary>
        /// <exception cref="MyException">Sintassi non corretta nel comando</exception>
        /// <param name="client">Connessione TCP al client</param>
        /// <param name="cmd">Comando ricevuto</param>
        private static void userRegistration(TcpClient client, Command cmd)
        {
            try
            {
                #region Parsing del comando
                SendCredentials credentials;
                try
                {
                    credentials = new SendCredentials(cmd);
                }
                catch (MyException e)
                {
                    // Notifico l'errore al client
                    ServerListener.sendError(client, ErrorCode.syntaxError);

                    StackTrace st = new StackTrace(e, true);
                    StackFrame sf = Utilis.GetFirstValidFrame(st);

                    throw new MyException(e.Message, Path.GetFileName(sf.GetFileName()), sf.GetFileLineNumber());
                }
                #endregion

                #region Validazione Input
                // Controllo di validità su username e pwd
                int usnLen = credentials.Username.Length;
                if (usnLen < Constants.MinUsernameLength || usnLen > Constants.MaxUsernameLength)
                {
                    ServerListener.sendError(client, ErrorCode.usernameLengthNotValid);
                    throw new Exception("lunghezza username non valida <" + usnLen + ">");

                }

                int pwdLen = credentials.Password.Length;
                if (pwdLen < Constants.MinPasswordLength || pwdLen > Constants.MaxPasswordLength)
                {
                    ServerListener.sendError(client, ErrorCode.passwordLengthNotValid);
                    throw new Exception("lunghezza password non valida <" + pwdLen + ">");
                }
                #endregion

                #region Controllo duplicato + inserimento nel DB
                // Il comando è valido, controllo se è gia presente altrimenti inserisco
                using (SQLiteConnection connection = new SQLiteConnection(DB.GetConnectionString()))
                {
                    connection.Open();

                    // Se viene lanciata un'eccezione automaticamente viene fatto il rollback uscendo dal blocco using
                    using (SQLiteTransaction tr = connection.BeginTransaction())
                    {
                        using (SQLiteCommand sqlCmd = connection.CreateCommand())
                        {
                            sqlCmd.CommandText = @"SELECT * FROM Utenti WHERE Username = @_username;";
                            sqlCmd.Parameters.AddWithValue("@_username", credentials.Username);

                            SQLiteDataReader reader = sqlCmd.ExecuteReader();
                            // Se ho dei risultati
                            //TODO testare l'hasrows
                            if (reader.HasRows == true)
                            {
                                ServerListener.sendError(client, ErrorCode.usernameAlreadyPresent);
                                throw new Exception("tentata registrazione con username duplicato <" + credentials.Username + ">");
                            }
                        }

                        using (SQLiteCommand sqlCmd = connection.CreateCommand())
                        {
                            // Genero l'md5 della password
                            string passMd5 = Utilis.Md5String(credentials.Password);

                            // Se sono qua posso aggiungere l'utente
                            sqlCmd.CommandText = @"INSERT INTO Utenti(Username, Password, InSynch)
                                                   VALUES (@_username, @_password, @_insynch);";
                            sqlCmd.Parameters.AddWithValue("@_username", credentials.Username);
                            sqlCmd.Parameters.AddWithValue("@_password", passMd5);
                            sqlCmd.Parameters.AddWithValue("@_insynch", false);

                            int nUpdated = sqlCmd.ExecuteNonQuery();
                            if (nUpdated != 1)
                                throw new Exception("impossibile aggiungere l'utente <" + credentials.Username + "> nel DB");

                            tr.Commit();
                        }
                    }

                    // recuper l'uid del client appena registrato
                    using (SQLiteCommand sqlCmd = connection.CreateCommand())
                    {
                        sqlCmd.CommandText = @"SELECT UID FROM Utenti WHERE Username=@_username";
                        sqlCmd.Parameters.AddWithValue("@_username", credentials.Username);


                        int UID = Convert.ToInt32(sqlCmd.ExecuteScalar());
                        
                        // Creo la cartella per l'utente
                        string basePath = Constants.PathServerFile + Path.DirectorySeparatorChar + UID;
                        Directory.CreateDirectory(basePath);

                        // Creo l'xml dell'utente
                        string xmlPath = basePath + ".xml";
                        using(StreamWriter sw = new StreamWriter(xmlPath))
                        {
                            sw.WriteLine(@"<dir name = ""ClientSide"">");
                            sw.WriteLine(@"</dir>");
                        }
                    }
                }
                #endregion

                

                Logger.Info("Registrato nuovo utente <" + credentials.Username + ">");
            }
            catch (Exception e)
            {
                // Notifico l'errore al client
                ServerListener.sendError(client, ErrorCode.syntaxError);

                StackTrace st = new StackTrace(e, true);
                StackFrame sf = Utilis.GetFirstValidFrame(st);
            }

        }


        /// <summary>
        /// Mando al client il suo xml digest relativo all'ultima versione
        /// </summary>
        /// <param name="client">Connessione TCP al client</param>
        private static void sendXmlDigest(TcpClient client, int UID)
        {
            string xmlPath = Constants.PathServerFile + Constants.PathSeparator + UID + ".xml";
            XmlManager xml = new XmlManager(xmlPath);

            XmlDigestCommand digest = new XmlDigestCommand(xml);
            Utilis.SendCmdSync(client, digest);
        }

        /// <summary>
        /// il server invia l' XML dell'ultima cartella che conosce
        /// </summary>
        /// <param name="xml"> xml dell'ultima versione della cartella lato server</param>
        public static void sendLastXml(TcpClient cl, int UID)
        {
            string xmlPath = Constants.PathServerFile + Constants.PathSeparator + UID + ".xml";
            XmlManager xml = new XmlManager(xmlPath);

            XmlCommand lastXml = new XmlCommand(xml);
            Utilis.SendCmdSync(cl, lastXml);
        }
        

        /// <summary>
        /// Controllo se posso iniziare una sessione di sincronizzazione 
        /// </summary>
        /// <param name="client">Il client</param>
        private static void startSynch(TcpClient client, int UID, ref SQLiteConnection connGlobalp, ref SQLiteTransaction tGlobal)
        {
            tGlobal = null;
            connGlobalp = null;
            //connGlobalp = new SQLiteConnection(DB.GetConnectionString());
            //connGlobalp.Open();
            

            using (SQLiteConnection connGlobal = new SQLiteConnection(DB.GetConnectionString()))
            {
                connGlobal.Open();
                // Se viene lanciata un'eccezione automaticamente viene fatto il rollback uscendo dal blocco using
                using (SQLiteTransaction tr = connGlobal.BeginTransaction())
                {

                    using (SQLiteCommand sqlCmd = connGlobal.CreateCommand())
                    {
                        sqlCmd.CommandText = @"SELECT InSynch FROM Utenti WHERE UID = @_UID;";
                        sqlCmd.Parameters.AddWithValue("@_UID", UID);

                        SQLiteDataReader reader = sqlCmd.ExecuteReader();
                        // Leggo la prima riga
                        reader.Read();
                        if (reader.GetBoolean(0) == true)
                        {
                            // Il client è gia in synch
                            return;
                        }
                    }

                    //// Se arrivo a sto punto vuol dire che non è loggato, allora lo metto in synch
                    //using (SQLiteCommand sqlCmdInsert = connGlobal.CreateCommand())
                    //{
                    //    // Se sono qua posso aggiungere l'utente
                    //    sqlCmdInsert.CommandText = @"UPDATE Utenti SET InSynch = @_insynch WHERE UID = @_UID";
                    //    sqlCmdInsert.Parameters.AddWithValue("@_UID", UID);
                    //    sqlCmdInsert.Parameters.AddWithValue("@_insynch", true);

                    //    int nUpdated = sqlCmdInsert.ExecuteNonQuery();
                    //    if (nUpdated != 1)
                    //        return;

                    //}

                    tr.Commit();
                }
            }

            //tGlobal = connGlobal.BeginTransaction();
        }

        /// <summary>
        /// riceve i files mancanti dal client passato come parametro
        /// </summary>
        private static void getUpdates(TcpClient client, SQLiteConnection connGlobal, SQLiteTransaction transGlobals, int UID)
        {
            FileNumCommand numeroFiles = new FileNumCommand(Utilis.GetCmdSync(client));
            Logger.Info("il numero di files che devo ricevere e': " + numeroFiles.NumFiles);

            int latestVersionId = 0;
            List<String> listaElementiUltimaVersioneServer = new List<String>();

            #region Recupero le informazioni dal DB
            // Cerco nel DB l'ultimo numero di versione per l'utente
            using (SQLiteCommand sqlCmd = connGlobal.CreateCommand())
            {
                sqlCmd.CommandText = @"SELECT MAX(VersionID) FROM Versioni WHERE UID = @_UID;";
                sqlCmd.Parameters.AddWithValue("@_UID", UID);
                try
                {
                    ///la conversione fa partire un NULLPOINTEREXCEPTION se l'oggetto sqlCmd è null
                    latestVersionId = Convert.ToInt32(sqlCmd.ExecuteScalar());
                }
                catch (Exception e)
                {
                    ///TODO basta gestire l'eccezione in questo modo? non credo
                    Logger.Error("SERVER->getUpdates La query per trovare l'ID dell'ultima versione non ha dato risultati" + e.ToString());
                    latestVersionId = 0;
                }

            }

            // Popolo la lista 
            using (SQLiteCommand sqlCmd = connGlobal.CreateCommand())
            {
                sqlCmd.CommandText = @"SELECT PathClient FROM Versioni WHERE VersionID = @_latestV AND UID = @_UID;";
                sqlCmd.Parameters.AddWithValue("@_latestV", latestVersionId);
                sqlCmd.Parameters.AddWithValue("@_UID", UID);

                SQLiteDataReader reader = sqlCmd.ExecuteReader();

                if (reader.HasRows)
                {
                    while (reader.Read())
                    {
                        ///popolo la lista dei files dell'ultima versione
                        listaElementiUltimaVersioneServer.Add(reader.GetString(0));
                        Logger.log("Aggiunto elemento " + reader.GetString(0) + "alla lista listaElementiUltimaVersioneServer");
                    }
                }
                else
                {
                    Logger.Info("Il client " + UID + " e' alla prima synch (non ci sono file)");
                }
                reader.Close();
            }
            #endregion

            // Passo alla versione n+1
            latestVersionId++;
            
            // Compongo il percorso base come 'cartella del server dove salvo le robe'\'id client'\
            string basePathAbs = Constants.PathServerFile + Constants.PathSeparator + UID + Constants.PathSeparator;


            for (int i = 0; i < numeroFiles.NumFiles; i++)
            {
                // Ricevo le informazioni sul file in arrivo
                FileInfoCommand info = new FileInfoCommand(Utilis.GetCmdSync(client));

                // Genero il nome univoco per il file
                //string destFileName = Utilis.Md5String(Path.GetFileName(info.AbsFilePath) + "." + latestVersionId);
                string destFileName = Path.GetFileName(info.AbsFilePath) + "." + latestVersionId;

                // Creo l'albero di cartelle se necessario
                // Directory.CreateDirectory(Path.GetDirectoryName(destPath));

                // Ricevo e salvo il file
                string destFilePathAbs = basePathAbs + destFileName;
                Utilis.GetFile(client, destFilePathAbs, info.FileSize);


                // Modifico i metadati rendendoli uguali a quelli del client
                File.SetCreationTime(destFilePathAbs, info.LastModTime);
                File.SetLastWriteTime(destFilePathAbs, info.LastModTime);

                //rimuovo dalla lista: se l'elemento non è presente nella lista la funzione ritorna false
                listaElementiUltimaVersioneServer.Remove(info.RelFilePath);

                    //file correttamente rimosso, significa che il file è stato aggiornato -> aggiorno l'entry nel DB
                    using (SQLiteCommand sqlCmd = connGlobal.CreateCommand())
                    {
                        sqlCmd.CommandText = @"INSERT Versioni (UID, VersionID, PathClient, MD5, LastModTime, Size, PathServer, LastVersion, Deleted)
                                                VALUES (@_UID ,@_latestV, @_pathClient, ,@_lastModTime, @_size, @_pathServer, true, false)";

                        sqlCmd.Parameters.AddWithValue("@_UID", UID);
                        sqlCmd.Parameters.AddWithValue("@_latestV", latestVersionId);
                        sqlCmd.Parameters.AddWithValue("@_pathClient", info.RelFilePath);
                        sqlCmd.Parameters.AddWithValue("@_md5", Utilis.MD5sum(destFilePathAbs));
                        sqlCmd.Parameters.AddWithValue("@_lastModTime", info.LastModTime);
                        sqlCmd.Parameters.AddWithValue("@_size", info.FileSize);
                        sqlCmd.Parameters.AddWithValue("@_pathServer", destFilePathAbs);


                        int nUpdated = sqlCmd.ExecuteNonQuery();
                        if (nUpdated != 1)
                            throw new Exception("Impossibile aggiungere il nuovo file " + info + " nel DB");

                    }

                Logger.Info(i + ") Ho ricevuto il file: " + destFileName);
            }

            ///aggiorno il valore del VersionID per tutti i files che non sono stati modificati
            foreach (String file in listaElementiUltimaVersioneServer)
            {
                    using (SQLiteCommand sqlCmd = connGlobal.CreateCommand())
                    {
                        sqlCmd.CommandText = @"UPDATE Versioni 
                                        SET VersionID = @_latestV, LastVersion = false  
                                        WHERE PathClient = @_pathClient AND UID = @_UID";
                               
                        sqlCmd.Parameters.AddWithValue("@_latestV", latestVersionId);
                        sqlCmd.Parameters.AddWithValue("@_pathClient", file);
                        sqlCmd.Parameters.AddWithValue("@_UID", UID);

                        int nUpdated = sqlCmd.ExecuteNonQuery();
                        if (nUpdated != 1)
                            throw new Exception("Impossibile aggiornare il VersionID del file " + file + " nel DB");

                    }
                         
            }

            // Ho concluso le modifiche al DB
            ServerListener.sendOk(client);

            // TODO rigenerare l'xml dal DB

            // Ricevo l'xml dal client
            XmlCommand lastXmlClient = new XmlCommand(Utilis.GetCmdSync(client));

            if (lastXmlClient == null)
                throw new Exception("Aspettavo un comando contenente un Xml, ricevuto nulla");
            if (lastXmlClient.kmd != CmdType.Xml)
                throw new Exception("Aspettavo un comando di tipo Xml, ricevuto " + lastXmlClient.kmd);

            string xmlPath = Constants.PathServerFile + Constants.PathSeparator + UID + ".xml";
            using(StreamWriter sw = new StreamWriter(xmlPath))
            {
                sw.Write(lastXmlClient.Xml);
            }


        }

        /// <summary>
        /// Gestisco la chiusura della sessione
        /// </summary>
        private static void endSynch(TcpClient client, SQLiteConnection connGlobal, SQLiteTransaction transGlobals, int UID)
        {

            // Tolgo l'utente dalla synch
            using (SQLiteCommand sqlCmd = connGlobal.CreateCommand())
            {
                // Se sono qua posso aggiungere l'utente
                sqlCmd.CommandText = @"UPDATE Utenti SET InSynch = @_insynch WHERE UID = @_UID";
                sqlCmd.Parameters.AddWithValue("@_UID", UID);
                sqlCmd.Parameters.AddWithValue("@_insynch", false);

                int nUpdated = sqlCmd.ExecuteNonQuery();
                if (nUpdated != 1)
                {
                    throw new Exception("Impossibile mettere a false _insynch per UID=" + UID);
                }
                    
            }

            
            
            // Chiudo tutto
            transGlobals.Commit();

            transGlobals.Rollback();
            transGlobals.Dispose();

            connGlobal.Close();
            connGlobal.Dispose();

            transGlobals = null; //TODO controllare che l'istanza globale venga messa a null!
            connGlobal = null;
        }


        /// <summary>
        /// Invio comando di errore al client</summary>
        /// <param name="client">Connessione TCP al client</param>
        /// <param name="code"Codice di errore></param>
        private static void sendError(TcpClient client, ErrorCode code)
        {
            ErrorCommand er = new ErrorCommand(code);
            Utilis.SendCmdSync(client, er);
        }

        /// <summary>
        /// Invio OK</summary>
        /// <param name="client">Connessione TCP al client</param>
        private static void sendOk(TcpClient client)
        {
            Command cmd = new Command(CmdType.ok, "");
            Utilis.SendCmdSync(client, cmd);
        }
    }
}


