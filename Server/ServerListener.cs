using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Server
{
    public sealed class ServerListener
    {
        // Singleton
        private static ServerListener instance = null;
        private TcpListener tcpListener = null;
        private bool running = false;

        
        public static ServerListener Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = new ServerListener();
                }
                return instance;
            }
        }

        public bool Running
        {
            get
            {
                return running;
            }
            
        }

        private ServerListener()
        {
            running = false;
            Logger.Info("Server creato");
        }

        public void Shutdown()
        {
            if(this.running == true)
            {
                tcpListener.Server.Close();
                this.running = false;
            }
            else
                Logger.Error("Server.shutdown su server spento");
        }

        /// <summary>
        /// Setto la porta alla quale ascoltare
        /// </summary>
        /// <param name="port">Intero che specifica la porta</param>
        public bool setPort(int port)
        {
            if (running == false)
            {
                this.tcpListener = new TcpListener(IPAddress.Any, port);
                return true;
            }
            else
                return false;
        }

        /// <summary>
        /// Setto il file del DB e lo inizializzo ponendo tutti i synch a zero
        /// </summary>
        /// <param name="dbPath">Percorso del db, se non esiste viene creato</param>
        public bool setDB(string dbPath)
        {
            if (running == false)
            {
                DB.SetDbConn(dbPath);
                DB.CheckDB();

                // Tolgo tutti gli utenti dalla synch
                using (SQLiteConnection cnn = new SQLiteConnection(DB.GetConnectionString()))
                {
                    cnn.Open();
                    using (SQLiteCommand mycommand = cnn.CreateCommand())
                    {
                        mycommand.CommandText = @"UPDATE Utenti SET InSynch = @_inSynch";
                        mycommand.Parameters.AddWithValue("@_inSynch", false);

                        mycommand.ExecuteNonQuery();
                    }
                }

                return true;
            }
            else
                return false;
        }

        public async void ServerStart(CancellationToken ct)
        {
            try
            {
                if (this.running == false)
                {
                    running = true;
                    // Start listening for client requests.
                    tcpListener.Start();
                    Logger.Info("Server in ascolto");


                    using (SQLiteConnection cnn = new SQLiteConnection(DB.GetConnectionString()))
                    {
                        cnn.Open();
                        XmlManager testV = new XmlManager(cnn, 1);
                        string xmlPath = Constants.XmlSavePath + Constants.PathSeparator + "version.xml";
                        testV.SaveToFile(xmlPath);
                        
                    }

                    while (!ct.IsCancellationRequested)
                    {
                        TcpClient client = await tcpListener.AcceptTcpClientAsync().ConfigureAwait(false);
                        Logger.Info("Ricevuta connessione da " + client.Client.RemoteEndPoint);

                        // Gestisco il client in un thread separato usando il THREAD POOL
                        // Questo perchè tendenzialmente la maggiorparte delle synch non presenteranno modifiche
                        // Ignoro il warning sulla mancanza di await perchè tanto non ho bisogno del risultato, approccio 'fire & forget'
                        #pragma warning disable 4014
                        Task.Run(() => serveClientSync(client, ct));                        
                        #pragma warning restore 4014

                    }
                }
                
            }
            catch (Exception ex)
            {
                StackTrace st = new StackTrace(ex, true);
                StackFrame sf = Utilis.GetFirstValidFrame(st);

                Logger.Error("[" + Path.GetFileName(sf.GetFileName()) + "(" + sf.GetFileLineNumber() + ")]: " + ex.Message);
            }
        }

        /// <summary>
        /// FUNZIONE PRINCIPALE PER GESTIONE CLIENT
        /// </summary>
        private void serveClientSync(TcpClient client, CancellationToken ct)
        {

            //TODO spostare in configurazione
            client.SendTimeout = Constants.SocketTimeout;
            client.ReceiveTimeout = Constants.SocketTimeout;


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
                                                        

                        case CmdType.getXmlDigest:
                            // Inizio la procedura di synch
                            ServerListener.clientSynch(client, k.AuthToken);
                            exitFlag = true;
                            break;

                        case CmdType.getRestoreXML:
                            // Invio l'XML con tutte le versioni
                            int id = checkAuthToken(k.AuthToken);
                            Logger.Info("RestoreXML da " + k.AuthToken);
                            if(id == -1)
                                ServerListener.sendError(client, ErrorCode.credentialsNotValid);
                            else
                                ServerListener.sendLastXml(client, id);
                            exitFlag = true;
                            break;

                        case CmdType.restoreFile:
                            // Invio il file per il restore
                            id = checkAuthToken(k.AuthToken);
                            if (id == -1)
                                ServerListener.sendError(client, ErrorCode.credentialsNotValid);
                            else
                                ServerListener.restoreFile(client, k);
                            exitFlag = false;
                            break;


                        default:
                            ServerListener.sendError(client, ErrorCode.unexpectedMessageType);
                            throw new Exception("Ricevuto " + Utilis.Cmd2String(k.kmd));

                    }
                }

            }
            catch (Exception e) // Eccezione non gestita, cerco di recuperare il metodo e la linea 
            {
                StackTrace st = new StackTrace(e, true);
                StackFrame sf = Utilis.GetFirstValidFrame(st);

                Logger.Error("[" + Path.GetFileName(sf.GetFileName()) + "(" + sf.GetFileLineNumber() + ")]: " + e.Message);

            }
            finally
            {
                client.Close();
            }

        }

        /// <summary>
        /// Invio all'utente il file voluto
        /// </summary>
        /// <param name="payload"></param>
        private static void restoreFile(TcpClient client, Command cmd)
        {
            #region controllo validità parametri
            // Casto il comando
            RestoreFileCommand restoreCmd = new RestoreFileCommand(cmd);

            // Controllo il token
            int UID = ServerListener.checkAuthToken(restoreCmd.AuthToken);
            if (UID == -1)
            {
                ServerListener.sendError(client, ErrorCode.credentialsNotValid);
                throw new Exception("Auth token non valido");
            }
            #endregion


            string fileAbsPath = "";
            long fileSize = 0;
            using (SQLiteConnection connessione = new SQLiteConnection(DB.GetConnectionString()))
            {
                connessione.Open();

                using (SQLiteTransaction tr = connessione.BeginTransaction())
                {
                    #region Recupero le info dal DB
                    using (SQLiteCommand sqlCmd = connessione.CreateCommand())
                    {
                        sqlCmd.CommandText = @"SELECT PathServer, size FROM Versioni WHERE UID = @_UID AND PathClient = @_clientPath AND VersionID = @_versionID";
                        sqlCmd.Parameters.AddWithValue("@_UID", UID);
                        sqlCmd.Parameters.AddWithValue("@_clientPath", restoreCmd.RelFilePath);
                        sqlCmd.Parameters.AddWithValue("@_versionID", restoreCmd.VersionID);

                        using (SQLiteDataReader reader = sqlCmd.ExecuteReader())
                        {
                            try
                            {
                                reader.Read();
                                fileAbsPath = reader.GetString(0);
                                fileSize = reader.GetInt64(1);

                                Logger.Info("Restore file: " + restoreCmd.RelFilePath + " size: " + fileSize);
                            }
                            catch (Exception)
                            {
                                fileAbsPath = "";
                                fileSize = 0;
                            }
                        }
                    }
                    #endregion

                    #region Invio il file
                    if (fileAbsPath == "")
                    {
                        //File non trovato
                        ServerListener.sendError(client, ErrorCode.fileNotFound);
                        return;
                    }
                    else
                    {
                        ServerListener.sendOk(client);
                        //Invio il file
                        Utilis.SendFile(client, fileAbsPath, fileSize);
                    }
                    #endregion

                    #region Aggiorno il DB
                    // Rimuovo la vecchia LAST VERSION
                    using (SQLiteCommand sqlCmd = connessione.CreateCommand())
                    {
                        sqlCmd.CommandText = @" UPDATE Versioni SET LastVersion = @_lastV
                                            WHERE UID = @_UID AND PathClient = @_clientPath";
                        sqlCmd.Parameters.AddWithValue("@_lastV", false);
                        sqlCmd.Parameters.AddWithValue("@_UID", UID);
                        sqlCmd.Parameters.AddWithValue("@_clientPath", restoreCmd.RelFilePath);

                        sqlCmd.ExecuteNonQuery();

                    }

                    // Aggiungo la nuova LAST VERSION
                    using (SQLiteCommand sqlCmd = connessione.CreateCommand())
                    {
                        sqlCmd.CommandText = @" UPDATE Versioni SET LastVersion = @_lastV, Deleted = @_deleted 
                                            WHERE UID = @_UID AND PathClient = @_clientPath AND VersionID = @_versionID";
                        sqlCmd.Parameters.AddWithValue("@_lastV", true);
                        sqlCmd.Parameters.AddWithValue("@_deleted", false);
                        sqlCmd.Parameters.AddWithValue("@_UID", UID);
                        sqlCmd.Parameters.AddWithValue("@_clientPath", restoreCmd.RelFilePath);
                        sqlCmd.Parameters.AddWithValue("@_versionID", restoreCmd.VersionID);

                        if (sqlCmd.ExecuteNonQuery() != 1)
                            throw new Exception("ClientRestore: Impossibile effettuare l'update B");

                    }
                    #endregion

                    tr.Commit();
                }
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

            #region CONTROLLO INSYNCH
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
            #endregion

            using (SQLiteConnection connessione = new SQLiteConnection(DB.GetConnectionString()))
            {
                connessione.Open();

                using (SQLiteTransaction tr = connessione.BeginTransaction())
                {
                    //Ricevo elenco file cancellati
                    ServerListener.getDeletedFiles(client, connessione, UID);
                    Logger.Info("File cancellati ricevuti");

                    ServerListener.sendOk(client);

                    //Invio file nuovi al client
                    int lastVersionID = 0;
                    ServerListener.sendFilesRequested(client, connessione, UID, ref lastVersionID);
                    Logger.Info("File modificati inviati");

                    ServerListener.sendOk(client);

                    // Ricevo le modifiche dal client      
                    lastVersionID = 0;
                    ServerListener.getUpdates(client, connessione, UID, ref lastVersionID);
                    Logger.Info("File modificati ricevuti");

                    
                    // Ho concluso le modifiche al DB
                    ServerListener.sendOk(client);

                    #region Genero XML da DB
                    string xmlPath = Constants.PathServerFile + Constants.PathSeparator + UID + ".xml";

                    XmlManager aus = new XmlManager(connessione, UID, lastVersionID);
                    aus.SaveToFile(xmlPath);
                    #endregion

                    // Finalizzo la sincronizzazione 
                    ServerListener.endSynch(client, connessione, UID);
                    Logger.Info("synch terminata");
                    tr.Commit();
                }


            }
        }

        private static void getDeletedFiles(TcpClient client, SQLiteConnection connessione, int UID)
        {
            int numFiles = 0;
            FileNumCommand numeroFilesCmd = new FileNumCommand(Utilis.GetCmdSync(client));
            numFiles = numeroFilesCmd.NumFiles;
            Logger.Info("il numero di files che il client ha cancellato e': " + numFiles);

            for (int i = 0; i < numFiles; i++)
            {
                //Ricevo i nomi dei file e segno sul db che sono cancellati
                Command request = Utilis.GetCmdSync(client);
                if (request.kmd != CmdType.deletedFile)
                {
                    Logger.Error("Il comando ricevuto non è corretto, atteso <filename>, ricevuto: <" + request.kmd + ">");
                    return; //TODO notificare l'errore all'utente
                }

                string relFilePath = request.Payload;
                Logger.Debug("Cancellato file " + relFilePath);

                //Prendo l'ultima versione del file
                //int lastVersion = ServerListener.getLastVersionForFile(UID, relFilePath, connessione);

                //segno nel db che e cancellato
                using (SQLiteCommand sqlCmd = connessione.CreateCommand())
                {
                    sqlCmd.CommandText = @"UPDATE Versioni SET Deleted = @_deleted WHERE UID = @_UID AND PathClient = @_pathClient;";
                    sqlCmd.Parameters.AddWithValue("@_deleted", true);
                    sqlCmd.Parameters.AddWithValue("@_UID", UID);
                    //sqlCmd.Parameters.AddWithValue("@_version", lastVersion);
                    sqlCmd.Parameters.AddWithValue("@_pathClient", relFilePath);

                    sqlCmd.ExecuteNonQuery();
                }

            }

        }


        /// <summary>
        /// invio i files al client su richiesta (prima fase di sincronizzazione)
        /// </summary>
        /// <param name="client"></param>
        /// <param name="connection"></param>
        /// <param name="UID"></param>
        /// <param name="lastVersionID"></param>
        private static void sendFilesRequested(TcpClient client, SQLiteConnection connection, int UID, ref int lastVersionID)
        {
            //TODO riguardare che abbia senso
            int numFiles = 0;
            #region Recupero il versionID
            // Cerco nel DB l'ultimo numero di versione per l'utente
            using (SQLiteCommand sqlCmd = connection.CreateCommand())
            {
                sqlCmd.CommandText = @"SELECT MAX(VersionID) FROM Versioni WHERE UID = @_UID;";
                sqlCmd.Parameters.AddWithValue("@_UID", UID);
                try
                {
                    //la conversione fa partire un NULLPOINTEREXCEPTION se l'oggetto sqlCmd è null
                    lastVersionID = Convert.ToInt32(sqlCmd.ExecuteScalar());
                }
                catch (Exception)
                {
                    // Prima synch
                    lastVersionID = 0;
                }

            }
            #endregion

            FileNumCommand numeroFilesCmd = new FileNumCommand(Utilis.GetCmdSync(client));
            numFiles = numeroFilesCmd.NumFiles;
            Logger.Info("il numero di files che devo inviare al client e': " + numFiles);

            #region ricezione richieste e invio files
            for (int i = 0; i < numFiles; i++)
            {
                Command request = Utilis.GetCmdSync(client);
                if (request.kmd != CmdType.fileName) {
                    Logger.Error("Il comando ricevuto non è corretto, atteso <filename>, ricevuto: <" + request.kmd + ">");
                    return; //TODO notificare l'errore all'utente
                }

                string requestedFile = String.Copy(request.Payload);
                using (SQLiteCommand sqlCmd = connection.CreateCommand())
                {
                    sqlCmd.CommandText = @"SELECT PathServer, Size FROM Versioni WHERE UID = @_UID AND VersionID = @_VID AND PathClient = @_pathClient;";
                    sqlCmd.Parameters.AddWithValue("@_UID", UID);
                    sqlCmd.Parameters.AddWithValue("@_VID", lastVersionID);
                    sqlCmd.Parameters.AddWithValue("@_pathClient", requestedFile);

                    using (SQLiteDataReader reader = sqlCmd.ExecuteReader())
                    {
                        if (reader.HasRows == false)
                        {
                            Logger.Error("Il file richiesto non è esistente: " + reader.GetString(0));
                            return; //TODO notificare l'errore all'utente
                        }

                        Utilis.SendFile(client, reader.GetString(0), Convert.ToInt64(reader.GetString(1)));

                    }

                }

            }

            #endregion
        }


        /// <summary>
        /// Riceve i files mancanti dal client passato come parametro
        /// </summary>
        private static void getUpdates(TcpClient client, SQLiteConnection connection, int UID, ref int lastVersion)
        {
            int latestVersionId = 0;
            
            // File non toccati, vanno portati a version+1
            List<String> untouchedElement = new List<String>();

            // File modificati, vanno lasciati alla versione vecchia e lastVersion->false
            List<String> elementUpdated = new List<String>();


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
                    //la conversione fa partire un NULLPOINTEREXCEPTION se l'oggetto sqlCmd è null
                    latestVersionId = Convert.ToInt32(sqlCmd.ExecuteScalar());
                }
                catch (Exception)
                {   
                    // Prima synch
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
                            untouchedElement.Add(reader.GetString(0));
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

            // Controllo se la directory esiste, altrimenti la creo
            if (Directory.Exists(basePathAbs) == false)
                Directory.CreateDirectory(basePathAbs);

            List<String> savedFile = new List<String>(); // Da usare in caso di eccezione per cancellare i file scaricati fino ad ora
            try
            {
                #region Scarico i nuovi file
                for (int i = 0; i < numFiles; i++)
                {
                    // Ricevo le informazioni sul file in arrivo
                    FileInfoCommand info = new FileInfoCommand(Utilis.GetCmdSync(client));

                    // Genero il nome univoco per il file
                    string destFileName = Utilis.Md5String(info.RelFilePath) + "_"
                        + Utilis.RandomString(Constants.RNDNameLength) + "." + latestVersionId;

                    // Ricevo e salvo il file
                    string destFilePathAbs = basePathAbs + destFileName;
                    Utilis.GetFile(client, destFilePathAbs, info.FileSize);
                    savedFile.Add(destFilePathAbs);

                    // Modifico i metadati rendendoli uguali a quelli del client
                    File.SetCreationTime(destFilePathAbs, info.LastModTime);
                    File.SetLastWriteTime(destFilePathAbs, info.LastModTime);

                    //rimuovo dalla lista: se l'elemento non è presente nella lista la funzione ritorna false
                    untouchedElement.Remove(info.RelFilePath);
                    elementUpdated.Add(info.RelFilePath);

                    #region Inserimento nuova versione
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
                    #endregion

                    #region Versione Precedente
                    // Controllo che esista una versione precedente
                    if (ServerListener.getLastVersionForFile(UID, info.RelFilePath, connection) > 0)
                    {
                        using (SQLiteCommand sqlCmd = connection.CreateCommand())
                        {
                            sqlCmd.CommandText = @"UPDATE Versioni 
                                        SET LastVersion = @_lastVersion  
                                        WHERE PathClient = @_pathClient AND UID = @_UID AND VersionID = @_prevVersionID";

                            //sqlCmd.Parameters.AddWithValue("@_versionID", latestVersionId);
                            sqlCmd.Parameters.AddWithValue("@_lastVersion", false);
                            sqlCmd.Parameters.AddWithValue("@_prevVersionID", latestVersionId - 1);                            
                            sqlCmd.Parameters.AddWithValue("@_pathClient", info.RelFilePath);
                            sqlCmd.Parameters.AddWithValue("@_UID", UID);


                            int nUpdated = sqlCmd.ExecuteNonQuery();

                        }
                    }
                    #endregion

                    Logger.Debug(i + ") Ho ricevuto il file: " + destFileName);
                }
                #endregion

                #region Aggiorno i DB per i file vecchi
                foreach (String file in untouchedElement)
                {
                    using (SQLiteCommand sqlCmd = connection.CreateCommand())
                    {
                        sqlCmd.CommandText = @"UPDATE Versioni 
                                    SET VersionID = @_versionID 
                                    WHERE PathClient = @_pathClient AND UID = @_UID AND VersionID = @_prevVersionID";

                        sqlCmd.Parameters.AddWithValue("@_versionID", latestVersionId);
                        sqlCmd.Parameters.AddWithValue("@_prevVersionID", latestVersionId-1);
                        //sqlCmd.Parameters.AddWithValue("@_lastVersion", false);
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

            lastVersion = latestVersionId;
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

        /// <summary>
        /// Cerco l'ultimo id di versione per un file, altrimenti -1
        /// </summary>
        /// <param name="UID">ID dell'utente</param>
        /// <param name="clientRelPath">Percorso del file</param>
        /// <param name="connection">Connessione al db</param>
        private static int getLastVersionForFile(int UID, string clientRelPath, SQLiteConnection connection)
        {
            int ret = -1;
            using (SQLiteCommand sqlCmd = connection.CreateCommand())
            {
                sqlCmd.CommandText = @"SELECT MAX(VersionID) FROM Versioni WHERE UID = @_UID AND PathClient = @_pathClient";
                sqlCmd.Parameters.AddWithValue("@_UID", UID);
                sqlCmd.Parameters.AddWithValue("@_pathClient", clientRelPath);

                ret = Convert.ToInt32(sqlCmd.ExecuteScalar());

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

            using (SQLiteConnection cnn = new SQLiteConnection(DB.GetConnectionString()))
            {
                cnn.Open();
                int lastV;
                using (SQLiteCommand sqlCmd = cnn.CreateCommand())
                {
                    sqlCmd.CommandText = @"SELECT MAX(VersionID) FROM Versioni WHERE UID = @_UID";
                    sqlCmd.Parameters.AddWithValue("@_UID", UID);
                    try
                    {
                        lastV = Convert.ToInt32(sqlCmd.ExecuteScalar());
                    }
                    catch (Exception)
                    {
                        lastV = -1;
                    }

                }

                XmlManager xml = null;
                if (lastV != -1)
                    xml = new XmlManager(cnn, UID, lastV);
                else
                    xml = new XmlManager(cnn, UID);

                XmlDigestCommand digest = new XmlDigestCommand(xml, Constants.ServerAuthToken);
                Utilis.SendCmdSync(client, digest);
            }
        }

        /// <summary>
        /// il server invia l' XML dell'ultima cartella che conosce
        /// </summary>
        /// <param name="xml"> xml dell'ultima versione della cartella lato server</param>
        public static void sendLastXml(TcpClient cl, int UID)
        {

            using (SQLiteConnection cnn = new SQLiteConnection(DB.GetConnectionString()))
            {
                cnn.Open();
                XmlManager xml = new XmlManager(cnn, UID);

                XmlCommand lastXml = new XmlCommand(xml, Constants.ServerAuthToken);
                Utilis.SendCmdSync(cl, lastXml);
            }
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