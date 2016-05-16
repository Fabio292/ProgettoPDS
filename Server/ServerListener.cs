using System;
using System.Data.SQLite;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.IO;
using System.Diagnostics;

namespace Server
{
    class ServerListener
    {
        private TcpListener serverListener = null;
        XmlManager XmlManager;

        // TODO fare un singleton
        public ServerListener()
        {
            // TODO spostare nella configurazione
            Int32 port = 10000;
            IPAddress localAddr = IPAddress.Parse("127.0.0.1");

            // TcpListener server = new TcpListener(port);
            this.serverListener = new TcpListener(IPAddress.Any, port);

            // TODO gestisco il DB
            DB.SetDbConn(@"D:\PDSCartellaPDS\ServerSide\db.sqlite");

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
            DirectoryInfo dirClient = new DirectoryInfo(Constants.TestPathServer);
            XmlManager = new XmlManager(dirClient);
            string clientUsername = "";

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
                        ServerListener.userLogin(client, k, ref clientUsername);
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
                        throw  new Exception("Mi aspettavo (Login|registration) Ricevuto " + Utilis.Cmd2String(k.kmd));
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
                            break;

                        case CmdType.getXML:
                            ///invio l'XML dell'ultima versione della cartella
                            ServerListener.sendLastXml(client, XmlManager);
                            Logger.Info("ho inviato l'Xml richiesto");
                            ServerListener.getUpdates(client);

                            // DEBUG
                            XmlManager newXml = new XmlManager(dirClient);
                            newXml.SaveToFile(Constants.XmlSavePath + @"\server.xml");
                            break;

                        case CmdType.sendFile:
                            break;

                        case CmdType.getXmlDigest:
                            ///invio l'md5 dell'XML dell'ultima versione della cartella
                            ServerListener.sendXmlDigest(client, XmlManager);
                            Logger.Info("ho inviato l'md5 dell'Xml richiesto:" + XmlManager.XMLDigest());
                            break;

                        case CmdType.endSynch:
                            break;

                        default:
                            ServerListener.sendError(client, ErrorCode.unexpectedMessageType);
                            throw new Exception("Mi aspettavo (Logout|getXML|TODO) Ricevuto " + Utilis.Cmd2String(k.kmd));

                    }
                }

            }
            catch(MyException e)
            {
                // Eccezione che ho lanciato io, devo solo loggare
                Logger.Error("[" + e.FileName+ "(" + e.LineNumber + ")]: " + e.Message);
            }
            catch (Exception e) // Eccezione non gestita, cerco di recuperare il metodo e la linea 
            {
                // Ottengo il metodo che ha generato l'eccezione

                StackTrace st = new StackTrace(e, true);
                StackFrame sf = Utilis.GetFirstValidFrame(st);

                Logger.Error("Errore gestione client (" + client.Client.RemoteEndPoint + ")[" + Path.GetFileName(sf.GetFileName()) + "(" + sf.GetFileLineNumber() + ")]: " + e.Message);


            }
            finally
            {
                // TODO chiudo la sync? altre cose? boh!
                client.Close();
            }
            
        }


        /// <summary>Loggo l'utente secondo i parametri passati</summary>
        /// <exception cref="MyException">Sintassi non corretta nel comando</exception>
        /// <param name="client">Connessione TCP al client</param>
        /// <param name="cmd">Comando ricevuto</param>
        /// <param name="usn">Stringa in cui andare a salvare l'username del client</param>
        /// <returns>TRUE se il login ha avuto successo</returns>
        private static bool userLogin(TcpClient client, Command cmd, ref string usn)
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
                }

                usn = credentials.Username;
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
        private static void sendXmlDigest(TcpClient client, XmlManager xml)
        {
            XmlDigestCommand digest = new XmlDigestCommand(xml);
            Utilis.SendCmdSync(client, digest);
        }

        /// <summary>
        /// il server invia l' XML dell'ultima cartella che conosce
        /// </summary>
        /// <param name="xml"> xml dell'ultima versione della cartella lato server</param>
        public static void sendLastXml(TcpClient cl, XmlManager xml)
        {
            XmlCommand lastXml = new XmlCommand(xml);
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
        private static void sendOk(TcpClient client)
        {
            Command cmd = new Command(CmdType.ok, "");
            Utilis.SendCmdSync(client, cmd);
        }

        /// <summary>
        /// riceve i files mancanti dal client passato come parametro
        /// </summary>
        private static void getUpdates(TcpClient client)
        {
            FileNumCommand numeroFiles = new FileNumCommand(Utilis.GetCmdSync(client));
            Logger.Info("il numero di files che devo ricevere e': " + numeroFiles.NumFiles);


            for (int i = 0; i < numeroFiles.NumFiles; i++)
            {
                FileInfoCommand info = new FileInfoCommand(Utilis.GetCmdSync(client));
                // TODO i file andranno poi messi con un nome generato a caso (per le versioni)
                // e divisi per utente
                string destPath = Utilis.RelativeToAbsPath(info.RelFilePath, Constants.TestPathServer); 
                

                // Creo l'albero di cartelle se necessario
                Directory.CreateDirectory(Path.GetDirectoryName(destPath));
                
                // Ricevo e salvo il file
                Utilis.GetFile(client, destPath, info.FileSize);

                // Modifico i metadati rendendoli uguali a quelli del client
                File.SetCreationTime(destPath, info.LastModTime);
                File.SetLastWriteTime(destPath, info.LastModTime);
                

                //Utilis.GetFile(client, Constants.TestPathServer + parti[0], Int32.Parse(parti[1]));

                Logger.Info(i + ") Ho ricevuto il file: " + destPath);
            }

        }



    }
}




/*

using (SQLiteConnection connection = new SQLiteConnection(DB.GetConnectionString()))
{
    connection.Open();

    using (SQLiteTransaction tr = connection.BeginTransaction())
    { 
        using (SQLiteCommand sqlCmd = connection.CreateCommand())
        {
            // Se sono qua posso aggiungere l'utente
            sqlCmd.CommandText = @"INSERT INTO Utenti(Username, Password, InSynch)
                                    VALUES (@_username, @_password, @_insynch)";
            sqlCmd.Parameters.AddWithValue("@_username", credentials.Username);
            sqlCmd.Parameters.AddWithValue("@_insynch", false);

            int nUpdated = sqlCmd.ExecuteNonQuery();
            if (nUpdated != 1)
                throw new Exception("impossibile aggiungere l'utente nel DB");

            tr.Commit();
        }

    }

}

*/
