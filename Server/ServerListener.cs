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

            //TODO tutti i InSynch = 0

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
            //SQLiteConnection conn = null;
            //SQLiteTransaction transaction = null;

            //TODO spostare in configurazione
            client.ReceiveTimeout = 1000000;            
            

            try
            {
                bool exitFlag = false;
                string userAuthtoken = Constants.DefaultAuthToken;

                Command k;
                
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
                    Logger.Debug("il comando ricevuto dal server e':\nKmd: " + k.kmd + "\nAuthtoken: " + k.AuthToken + "\nPayloadLength: "
                    + k.PayloadLength + "\nPayload: " + k.Payload);

                    switch (k.kmd)
                    {
                        case CmdType.login:
                            ServerListener.userLogin(client, k, ref userAuthtoken);
                            ServerListener.sendOk(client, userAuthtoken);
                            exitFlag = true;
                            break;


                        case CmdType.registration:
                            ServerListener.userRegistration(client, k, ref userAuthtoken);
                            ServerListener.sendOk(client, userAuthtoken);
                            exitFlag = true;
                            break;



                        //case CmdType.getXmlDigest:
                        //    ///invio l'md5 dell'XML dell'ultima versione della cartella
                        //    ServerListener.sendXmlDigest(client, userID);
                        //    Logger.Info("ho inviato l'md5 dell'Xml richiesto:");
                        //    break;


                        //case CmdType.getXML:
                        //    ///invio l'XML dell'ultima versione della cartella
                        //    ServerListener.sendLastXml(client, userID);
                        //    Logger.Info("ho inviato l'Xml richiesto");
                        //    break;
                            

                        case CmdType.getXmlDigest:
                            ServerListener.clientSynch(client, k.AuthToken);

                            //// Controllo se ho effettivamente iniziato la sincronizzazione
                            //if (conn == null || transaction == null)
                            //{
                            //    ServerListener.sendError(client, ErrorCode.userAlreadyInSynch);
                            //    Logger.Error("L'utente " + userID + " e' gia in synch");

                            //    if (transaction != null)
                            //    {
                            //        transaction.Rollback();
                            //        transaction.Dispose();
                            //        transaction = null;
                            //    }

                            //    if (conn != null)
                            //    {
                            //        conn.Close();
                            //        conn.Dispose();
                            //        conn = null;
                            //    }                              

                            //    exitFlag = true;
                            //}
                            exitFlag = true;
                            break;
                            

                        default:
                            ServerListener.sendError(client, ErrorCode.unexpectedMessageType);
                            throw new Exception("Ricevuto " + Utilis.Cmd2String(k.kmd));

                    }
                }

            }
            catch (Exception e) // Eccezione non gestita, cerco di recuperare il metodo e la linea 
            {
                // Ottengo il metodo che ha generato l'eccezione

                StackTrace st = new StackTrace(e, true);
                StackFrame sf = Utilis.GetFirstValidFrame(st);

                Logger.Error("[" + Path.GetFileName(sf.GetFileName()) + "(" + sf.GetFileLineNumber() + ")]: " + e.Message);

                // eventualmente chiudo la transaction con un rollback se necessario e la connessione
                //if (transaction != null)
                //{
                //    transaction.Rollback();
                //    transaction.Dispose();
                //    transaction = null;
                //}
                //if (conn != null)
                //{
                //    conn.Close();
                //    conn.Dispose();
                //    conn = null;
                //}              

            }
            finally
            {
                //if (transaction != null)
                //{
                //    Logger.Error("AAAAAAAAAAAAAA ERRORE GRAVISSIMO transazione non nulla nel finally");
                //}
                //if (conn != null)
                //{
                //    Logger.Error("AAAAAAAAAAAAAA ERRORE GRAVISSIMO connessione non nulla nel finally");
                //}

                client.Close();
            }

        }

        #region Gestione utente

        /// <summary>Loggo l'utente secondo i parametri passati</summary>
        /// <exception cref="MyException">Sintassi non corretta nel comando</exception>
        /// <param name="client">Connessione TCP al client</param>
        /// <param name="cmd">Comando ricevuto</param>
        /// <param name="usn">Stringa in cui andare a salvare l'username del client</param>
        /// <returns>TRUE se il login ha avuto successo</returns>
        private static bool userLogin(TcpClient client, Command cmd, ref string authToken)
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

                    //using (SQLiteCommand sqlCmd = connection.CreateCommand())
                    using (SQLiteCommand sqlCmd = new SQLiteCommand(connection))
                    {
                        // Cerco l'utente con le credenziali passate
                        string passMd5 = Utilis.Md5String(credentials.Password);
                        sqlCmd.CommandText = @"SELECT UID, AuthToken FROM Utenti WHERE Username=@_username AND Password=@_password";
                        sqlCmd.Parameters.AddWithValue("@_username", credentials.Username);
                        sqlCmd.Parameters.AddWithValue("@_password", passMd5);

                        using(SQLiteDataReader reader = sqlCmd.ExecuteReader())
                        {
                            if (reader.HasRows == false)
                            {
                                ServerListener.sendError(client, ErrorCode.credentialsNotValid);
                                throw new Exception("tentato login con credenziali non valide <" + credentials.Username + "><" + credentials.Password + ">");
                            }

                            // Leggo la prima (e unica) riga
                            reader.Read();

                            // Salvo l'UID e authToken
                            //userID = reader.GetInt32(0);
                            authToken = reader.GetString(1);

                            return true;

                        }                       
                    }

                    //// Cerco l'ID dell'utente
                    ////using (SQLiteCommand sqlCmd = connection.CreateCommand())
                    //using (SQLiteCommand sqlCmd = new SQLiteCommand(connection))
                    //{
                    //    sqlCmd.CommandText = @"SELECT UID FROM Utenti WHERE Username=@_username";
                    //    sqlCmd.Parameters.AddWithValue("@_username", credentials.Username);


                    //    userID = Convert.ToInt32(sqlCmd.ExecuteScalar());
                    //    Logger.Info("Login con successo per <" + credentials.Username + ">");
                    //    return true;

                    //}
                }
                #endregion

            }
            catch (ArgumentException)
            {
                // Notifico l'errore al client
                ServerListener.sendError(client, ErrorCode.syntaxError);
            }

            return true;
        }

        /// <summary>
        /// Registro l'utente secondo i parametri passati</summary>
        /// <exception cref="MyException">Sintassi non corretta nel comando</exception>
        /// <param name="client">Connessione TCP al client</param>
        /// <param name="cmd">Comando ricevuto</param>
        private static void userRegistration(TcpClient client, Command cmd, ref string authToken)
        {
            try
            {
                #region Parsing del comando
                SendCredentials credentials;
                authToken = null;
                try
                {
                    credentials = new SendCredentials(cmd);
                }
                catch (MyException e)
                {
                    // Notifico l'errore al client
                    ServerListener.sendError(client, ErrorCode.syntaxError);

                    throw new Exception(e.Message);
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

                            using (SQLiteDataReader reader = sqlCmd.ExecuteReader())
                            {
                                // Se ho dei risultati vuol dire che l'username esiste gia
                                if (reader.HasRows == true)
                                {
                                    ServerListener.sendError(client, ErrorCode.usernameAlreadyPresent);
                                    throw new Exception("tentata registrazione con username duplicato <" + credentials.Username + ">");
                                }
                            }
                        }

                        using (SQLiteCommand sqlCmd = connection.CreateCommand())
                        {
                            // Genero l'md5 della password
                            string passMd5 = Utilis.Md5String(credentials.Password);

                            // Genero l'authToken
                            string at = Utilis.RandomString(Constants.AuthTokenLength);


                            // Se sono qua posso aggiungere l'utente
                            sqlCmd.CommandText = @"INSERT INTO Utenti(Username, Password, AuthToken, InSynch)
                                                   VALUES (@_username, @_password, @_authToken, @_insynch);";
                            sqlCmd.Parameters.AddWithValue("@_username", credentials.Username);
                            sqlCmd.Parameters.AddWithValue("@_password", passMd5);
                            sqlCmd.Parameters.AddWithValue("@_authToken", at);
                            sqlCmd.Parameters.AddWithValue("@_insynch", false);
                            

                            int nUpdated = sqlCmd.ExecuteNonQuery();
                            if (nUpdated != 1)
                                throw new Exception("impossibile aggiungere l'utente <" + credentials.Username + "> nel DB");

                            authToken = at;
                            tr.Commit();
                        }
                    }

                    // recupero l'uid del client appena registrato
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
                        XmlManager.InitializeXmlFile(xmlPath);
                    }
                }
                #endregion

                
                Logger.Info("Registrato nuovo utente <" + credentials.Username + ">");
            }
            catch (ArgumentException)
            {
                // Notifico l'errore al client
                ServerListener.sendError(client, ErrorCode.syntaxError);
                
            }

        }

        #endregion



        /// <summary>
        /// Controllo se posso iniziare una sessione di sincronizzazione 
        /// </summary>
        /// <param name="client">Il client</param>
        private static void clientSynch(TcpClient client, string authToken)
        {
            #region Controllo il login + MD5
            int UID = ServerListener.checkAuthToken(authToken);
            if (UID == -1)
            {
                ServerListener.sendError(client, ErrorCode.credentialsNotValid);
                throw new Exception("Auth token non valido");
            }

            ServerListener.sendXmlDigest(client, UID);
            Logger.Info("Ho inviato l'md5 dell'Xml richiesto");
            #endregion

            #region Scambio XML
            Command k = Utilis.GetCmdSync(client);
            if (k == null || !(k.kmd == CmdType.logout || k.kmd == CmdType.getXML))
            {
                Logger.Error("Aspettavo un comando, ricevuto nulla o tipo errato");
                ServerListener.sendError(client, ErrorCode.networkError);
                return;
            }
            // Nel caso gli md5 siano uguali il client mi manda un logout
            if (k.kmd == CmdType.logout)
            {
                Logger.Debug("Digest uguali, chiudo");
                return;
            }

            ServerListener.sendLastXml(client, UID);
            Logger.Info("ho inviato l'Xml richiesto");
            #endregion

            #region Controllo inizio sessione
            // Per andare avanti aspetto un OK
            k = Utilis.GetCmdSync(client);
            if (k == null || !(k.kmd == CmdType.logout || k.kmd == CmdType.startSynch))
            {
                Logger.Error("Aspettavo un comando, ricevuto nulla o tipo errato");
                ServerListener.sendError(client, ErrorCode.networkError);
                return;
            }
            // Nel caso gli XML siano uguali il client mi manda un logout
            if (k.kmd == CmdType.logout)
            {
                Logger.Debug("Digest uguali, chiudo");
                return;
            }
            #endregion

            // Se arrivo a sto punto voglio trasmettere dei file
            ServerListener.sendOk(client);

            // Blocco l'utente
            using (SQLiteConnection connessione = new SQLiteConnection(DB.GetConnectionString()))
            {
                connessione.Open();

                using (SQLiteTransaction tr = connessione.BeginTransaction())
                {
                    #region controllo lo stato utente
                    using (SQLiteCommand sqlCmd = connessione.CreateCommand())
                    {
                        sqlCmd.CommandText = @"SELECT InSynch FROM Utenti WHERE UID = @_UID;";
                        sqlCmd.Parameters.AddWithValue("@_UID", UID);

                        using (SQLiteDataReader reader = sqlCmd.ExecuteReader())
                        {
                            // Leggo la prima riga
                            reader.Read();
                            if (reader.GetBoolean(0) == true)
                            {
                                // Il client è gia in synch
                                //return; TODO togliere
                            }
                        }

                    }
                    #endregion

                    #region blocco l'utente
                    // Se arrivo a sto punto vuol dire che non è loggato, allora lo metto in synch
                    using (SQLiteCommand sqlCmdInsert = connessione.CreateCommand())
                    {
                        // Se sono qua posso aggiungere l'utente
                        sqlCmdInsert.CommandText = @"UPDATE Utenti SET InSynch = @_insynch WHERE UID = @_UID";
                        sqlCmdInsert.Parameters.AddWithValue("@_UID", UID);
                        sqlCmdInsert.Parameters.AddWithValue("@_insynch", true);

                        int nUpdated = sqlCmdInsert.ExecuteNonQuery();
                        if (nUpdated != 1)
                            return;

                    }
                    tr.Commit();
                    #endregion

                }
            }            

            using (SQLiteConnection connessione = new SQLiteConnection(DB.GetConnectionString()))
            {
                connessione.Open();

                using (SQLiteTransaction tr = connessione.BeginTransaction())
                {
                    // Ricevo le modifiche                 
                    ServerListener.getUpdates(client, connessione, UID);

                    Logger.Info("File ricevuti");
                    // Ho concluso le modifiche al DB
                    ServerListener.sendOk(client);

                    #region Genero XML
                    // TODO rigenerare l'xml dal DB

                    // Ricevo l'xml dal client
                    XmlCommand lastXmlClient = new XmlCommand(Utilis.GetCmdSync(client));

                    if (lastXmlClient == null)
                        throw new Exception("Aspettavo un comando contenente un Xml, ricevuto nulla");
                    if (lastXmlClient.kmd != CmdType.Xml)
                        throw new Exception("Aspettavo un comando di tipo Xml, ricevuto " + lastXmlClient.kmd);

                    string xmlPath = Constants.PathServerFile + Constants.PathSeparator + UID + ".xml";
                    using (StreamWriter sw = new StreamWriter(xmlPath))
                    {
                        sw.Write(lastXmlClient.Xml);
                    }
                    #endregion

                    // Finalizzo la sincronizzazione 
                    ServerListener.endSynch(client, connessione, UID);
                    Logger.Info("synch terminata");
                    tr.Commit();
                }
            }
        }


        /// <summary>
        /// Riceve i files mancanti dal client passato come parametro
        /// </summary>
        private static void getUpdates(TcpClient client, SQLiteConnection connection, int UID)
        {
            int latestVersionId = 0;
            List<String> listaElementiUltimaVersioneServer = new List<String>();
            

            FileNumCommand numeroFilesCmd = new FileNumCommand(Utilis.GetCmdSync(client));
            int numFiles = numeroFilesCmd.NumFiles;
            Logger.Info("il numero di files che devo ricevere e': " + numFiles);

            #region Recupero il versionID
            // Cerco nel DB l'ultimo numero di versione per l'utente
            using (SQLiteCommand sqlCmd = connection.CreateCommand())
            {
                sqlCmd.CommandText = @"SELECT MAX(VersionID) FROM Versioni WHERE UID = @_UID;";
                sqlCmd.Parameters.AddWithValue("@_UID", UID);
                try
                {
                    ///la conversione fa partire un NULLPOINTEREXCEPTION se l'oggetto sqlCmd è null
                    latestVersionId = Convert.ToInt32(sqlCmd.ExecuteScalar());
                }
                catch (Exception)
                {
                    ///TODO basta gestire l'eccezione in questo modo? non credo
                    //Logger.Error("SERVER->getUpdates La query per trovare l'ID dell'ultima versione non ha dato risultati" + e.ToString());
                    latestVersionId = 0;
                }

            }
            #endregion

            #region Popolo la lista dei file
            // Popolo la lista 
            using (SQLiteCommand sqlCmd = connection.CreateCommand())
            {
                sqlCmd.CommandText = @"SELECT PathClient FROM Versioni WHERE VersionID = @_latestV AND UID = @_UID;";
                sqlCmd.Parameters.AddWithValue("@_latestV", latestVersionId);
                sqlCmd.Parameters.AddWithValue("@_UID", UID);

                using (SQLiteDataReader reader = sqlCmd.ExecuteReader())
                {

                    if (reader.HasRows)
                    {
                        while (reader.Read())
                        {
                            ///popolo la lista dei files dell'ultima versione
                            listaElementiUltimaVersioneServer.Add(reader.GetString(0));
                        }
                    }
                    else
                    {
                        Logger.Info("Il client " + UID + " e' alla prima synch (non ci sono file)");
                    }
                }
            }
            #endregion

            // Passo alla versione n+1
            latestVersionId++;
            
            // Compongo il percorso base come 'cartella del server dove salvo le robe'\'id client'\
            string basePathAbs = Constants.PathServerFile + Constants.PathSeparator + UID + Constants.PathSeparator;

            List<String> savedFile = new List<String>(); // Da usare in caso di eccezione per cancellare i file scaricati fino ad ora
            try
            {
                #region Scarico i nuovi file
                for (int i = 0; i < numFiles; i++)
                {
                    // Ricevo le informazioni sul file in arrivo
                    FileInfoCommand info = new FileInfoCommand(Utilis.GetCmdSync(client));

                    // Genero il nome univoco per il file
                    string destFileName = Utilis.Md5String(Path.GetFileName(info.AbsFilePath)) + "_"
                        + Utilis.RandomString(5) + "." + latestVersionId;

                    // Ricevo e salvo il file
                    string destFilePathAbs = basePathAbs + destFileName;
                    Utilis.GetFile(client, destFilePathAbs, info.FileSize);
                    savedFile.Add(destFilePathAbs);


                    // Modifico i metadati rendendoli uguali a quelli del client
                    File.SetCreationTime(destFilePathAbs, info.LastModTime);
                    File.SetLastWriteTime(destFilePathAbs, info.LastModTime);

                    //rimuovo dalla lista: se l'elemento non è presente nella lista la funzione ritorna false
                    listaElementiUltimaVersioneServer.Remove(info.RelFilePath);

                    // Aggiorno l'entry nel DB
                    using (SQLiteCommand sqlCmd = connection.CreateCommand())
                    {
                        sqlCmd.CommandText = @"INSERT INTO Versioni (UID, VersionID, PathClient, MD5, LastModTime, Size, PathServer, LastVersion, Deleted)
                                                VALUES (@_UID ,@_versionID, @_pathClient, @_md5, @_lastModTime, @_size, @_pathServer, @_lastVersion, @_deleted)";

                        sqlCmd.Parameters.AddWithValue("@_UID", UID);
                        sqlCmd.Parameters.AddWithValue("@_versionID", latestVersionId);
                        sqlCmd.Parameters.AddWithValue("@_pathClient", info.RelFilePath);
                        sqlCmd.Parameters.AddWithValue("@_md5", Utilis.MD5sum(destFilePathAbs));
                        sqlCmd.Parameters.AddWithValue("@_lastModTime", info.LastModTime);
                        sqlCmd.Parameters.AddWithValue("@_size", info.FileSize);
                        sqlCmd.Parameters.AddWithValue("@_pathServer", destFilePathAbs);
                        sqlCmd.Parameters.AddWithValue("@_lastVersion", true);
                        sqlCmd.Parameters.AddWithValue("@_deleted", false);


                        int nUpdated = sqlCmd.ExecuteNonQuery();
                        if (nUpdated != 1)
                            throw new Exception("Impossibile aggiungere il nuovo file " + info + " nel DB");

                    }

                    Logger.Info(i + ") Ho ricevuto il file: " + destFileName);
                }
                #endregion

                #region Aggiorno i DB per i file vecchi
                foreach (String file in listaElementiUltimaVersioneServer)
                {
                    using (SQLiteCommand sqlCmd = connection.CreateCommand())
                    {
                        sqlCmd.CommandText = @"UPDATE Versioni 
                                    SET VersionID = @_versionID, LastVersion = @_lastVersion  
                                    WHERE PathClient = @_pathClient AND UID = @_UID AND VersionID = @_prevVersionID";

                        sqlCmd.Parameters.AddWithValue("@_versionID", latestVersionId);
                        sqlCmd.Parameters.AddWithValue("@_prevVersionID", latestVersionId-1);
                        sqlCmd.Parameters.AddWithValue("@_lastVersion", false);
                        sqlCmd.Parameters.AddWithValue("@_pathClient", file);
                        sqlCmd.Parameters.AddWithValue("@_UID", UID);


                        int nUpdated = sqlCmd.ExecuteNonQuery();
                        if (nUpdated != 1)
                            throw new Exception("Impossibile aggiornare il VersionID del file " + file + " nel DB");

                    }

                }
                #endregion
            }
            catch (Exception e)
            {
                // Cancello i file che sono stati scaricati fino ad ora
                foreach (var fileAbsPath in savedFile)
                {
                    if (File.Exists(fileAbsPath))
                        File.Delete(fileAbsPath);
                }
                throw e;
            }
        }

        /// <summary>
        /// Gestisco la chiusura della sessione
        /// </summary>
        private static void endSynch(TcpClient client, SQLiteConnection connection, int UID)
        {
            // Tolgo l'utente dalla synch
            using (SQLiteCommand sqlCmd = connection.CreateCommand())
            {
                sqlCmd.CommandText = @"UPDATE Utenti SET InSynch = @_insynch WHERE UID = @_UID";
                sqlCmd.Parameters.AddWithValue("@_UID", UID);
                sqlCmd.Parameters.AddWithValue("@_insynch", false);

                int nUpdated = sqlCmd.ExecuteNonQuery();
                if (nUpdated != 1)
                {
                    throw new Exception("Impossibile mettere a false _insynch per UID=" + UID);
                }
                    
            }
        }

        /// <summary>
        /// Controllo se l'auth token è valido oppure no. Ritorno l'UID dell'utente
        /// </summary>
        /// <param name="authToken">Auth token da controllare</param>
        /// <returns>UID dell'utente corrispondente al token oppure -1</returns>
        private static int checkAuthToken(string authToken)
        {
            int ret = -1;

            using (SQLiteConnection connection = new SQLiteConnection(DB.GetConnectionString()))
            {
                connection.Open();
                
                using (SQLiteCommand sqlCmd = new SQLiteCommand(connection))
                {
                    // Cerco l'utente con le credenziali passate
                    sqlCmd.CommandText = @"SELECT UID FROM Utenti WHERE AuthToken=@_authToken";
                    sqlCmd.Parameters.AddWithValue("@_authToken", authToken);


                    using (SQLiteDataReader reader = sqlCmd.ExecuteReader())
                    {
                        if (reader.HasRows == true)
                        {
                            reader.Read();
                            ret = reader.GetInt32(0);
                        }
                    }
                }
            }

            return ret;
        }

        #region Singoli Comandi
        /// <summary>
        /// Mando al client il suo xml digest relativo all'ultima versione
        /// </summary>
        /// <param name="client">Connessione TCP al client</param>
        private static void sendXmlDigest(TcpClient client, int UID)
        {
            string xmlPath = Constants.PathServerFile + Constants.PathSeparator + UID + ".xml";
            if (!File.Exists(xmlPath))
                XmlManager.InitializeXmlFile(xmlPath);

            XmlManager xml = new XmlManager(xmlPath);

            XmlDigestCommand digest = new XmlDigestCommand(xml, Constants.ServerAuthToken);
            Utilis.SendCmdSync(client, digest);
        }

        /// <summary>
        /// il server invia l' XML dell'ultima cartella che conosce
        /// </summary>
        /// <param name="xml"> xml dell'ultima versione della cartella lato server</param>
        public static void sendLastXml(TcpClient cl, int UID)
        {
            string xmlPath = Constants.PathServerFile + Constants.PathSeparator + UID + ".xml";
            if (!File.Exists(xmlPath))
                XmlManager.InitializeXmlFile(xmlPath);

            XmlManager xml = new XmlManager(xmlPath);

            XmlCommand lastXml = new XmlCommand(xml, Constants.ServerAuthToken);
            Utilis.SendCmdSync(cl, lastXml);
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
        /// <param name="payload">Eventuale payload da inserire</param>
        private static void sendOk(TcpClient client, string payload = "")
        {
            Command cmd = new Command(CmdType.ok, payload);
            Utilis.SendCmdSync(client, cmd);
        }

        #endregion
    }
}


