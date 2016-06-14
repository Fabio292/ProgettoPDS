using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Windows;
using System.Xml.Linq;

namespace Client
{
    class ClientConnection
    {

        private TcpClient conn = null;
        private bool isConnected = false;

        public bool IsConnected
        {
            get
            {
                return isConnected;
            }
        }

        public ClientConnection()
        {
        }

        /// <summary>
        /// Disconnetto il client (se era precedentemente connesso)
        /// </summary>
        private void disconnect()
        {
            if (this.isConnected == true)
            {
                this.isConnected = false;
                conn.Close();
            }
        }

        /// <summary>
        /// Connetto il client al server</summary>
        /// <exception cref="Exception">Eccezioni generiche</exception>
        private void connect()
        {
            // Controllo di non essere gia connesso
            if (this.isConnected == false)
            {
                conn = new TcpClient();
                this.isConnected = false;

                //TODO setto i parametri della connessione
                conn.SendTimeout = Constants.SocketTimeout;
                conn.ReceiveTimeout = Constants.SocketTimeout;


                conn.Connect(Settings.ServerIP, Settings.ServerPort);

                this.isConnected = true;
                Logger.log("Aperta connessione verso il server");
            }
        }

        /// <summary>
        /// Ritorna l'oggetto TcpClient per questa istanza
        /// </summary>
        public TcpClient GetTcpClient()
        {
            return this.conn;
        }

        /// <summary>
        /// Effettuo la registrazione ritornando, se con successo, l'authToken da utilizzare
        /// </summary>
        /// <param name="username">L'username</param>
        /// <param name="password">La password</param>
        /// <param name="authToken">ref: La stringa di autenticazione</param>
        public bool ClientRegistration(string username, string password, ref string authToken)
        {
            bool ret = false;

            try
            {
                this.connect();

                #region Invio richiesta registrazione
                SendCredentials regCmd = new SendCredentials(username, password, CmdType.registration);

                Utilis.SendCmdSync(this.conn, regCmd);
                Logger.Debug("il comando inviato e':\nKmd: " + regCmd.kmd + "\nAuthtoken: " + regCmd.AuthToken + "\nPayloadLength: "
                    + regCmd.PayloadLength + "\nPayload: " + regCmd.Payload);
                #endregion

                #region Ricevo risposta
                Command answer = Utilis.GetCmdSync(this.conn);

                if (answer == null)
                {
                    Logger.Error("Aspettavo un comando di risposta dal server a seguito della registrazione, ricevuto nulla");
                    throw new Exception("Errore comunicazione con il server");
                }


                switch (answer.kmd)
                {
                    case CmdType.error:
                        ErrorCommand errCmd = new ErrorCommand(answer);
                        string errMsg = "";

                        switch (errCmd.Code)
                        {
                            case ErrorCode.usernameAlreadyPresent:
                                errMsg = "Username gia utilizzato, scegline un altro";
                                break;

                            default:
                                errMsg = "Errore durante la registrazione (" + Utilis.Err2String(errCmd.Code) + ")";
                                break;
                        }
                        throw new Exception(errMsg);

                    case CmdType.ok:
                        Logger.Info("registrazione effettuato correttamente, authToken: " + answer.Payload);
                        authToken = answer.Payload;
                        ret = true;
                        break;

                    default:
                        Logger.Error("Mi aspettavo (error|ok) Ricevuto " + Utilis.Cmd2String(answer.kmd));
                        throw new Exception("Errore comunicazione con il server");
                }
                #endregion

            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Errore Registrazione", MessageBoxButton.OK, MessageBoxImage.Error);

                StackTrace st = new StackTrace(ex, true);
                StackFrame sf = Utilis.GetFirstValidFrame(st);

                Logger.Error("[" + Path.GetFileName(sf.GetFileName()) + "(" + sf.GetFileLineNumber() + ")]: " + ex.Message);
            }
            finally
            {
                this.disconnect();
            }

            return ret;
        }

        /// <summary>
        /// Effettuo il login ritornando, se con successo, l'authToken da utilizzare
        /// </summary>
        /// <param name="username">L'username</param>
        /// <param name="password">La password</param>
        /// <param name="authToken">ref: La stringa di autenticazione</param>
        public bool ClientLogin(string username, string password, ref string authToken)
        {
            bool ret = false;

            try
            {
                this.connect();

                #region Invio richiesta login
                SendCredentials loginCmd = new SendCredentials(username, password, CmdType.login);

                Utilis.SendCmdSync(this.conn, loginCmd);
                Logger.Debug("il comando inviato e':\nKmd: " + loginCmd.kmd + "\nAuthtoken: " + loginCmd.AuthToken + "\nPayloadLength: "
                    + loginCmd.PayloadLength + "\nPayload: " + loginCmd.Payload);
                #endregion

                #region Ricevo risposta
                Command answer = Utilis.GetCmdSync(this.conn);

                if (answer == null)
                {
                    Logger.Error("Aspettavo un comando di risposta dal server a seguito del login, ricevuto nulla");
                    throw new Exception("Errore comunicazione con il server");
                }
                    

                switch (answer.kmd)
                {
                    case CmdType.error:
                        Logger.Error("Credenziali errate");
                        throw new Exception("Credenziali errate");

                    case CmdType.ok:
                        Logger.Info("login effettuato correttamente, authToken: " + answer.Payload);
                        authToken = answer.Payload;
                        ret = true;
                        break;

                    default:
                        Logger.Error("Mi aspettavo (error|ok) Ricevuto " + Utilis.Cmd2String(answer.kmd));
                        throw new Exception("Errore comunicazione con il server");
                }
                #endregion

            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(ex.Message, "Errore Login", MessageBoxButton.OK, MessageBoxImage.Error);

                StackTrace st = new StackTrace(ex, true);
                StackFrame sf = Utilis.GetFirstValidFrame(st);

                Logger.Error("[" + Path.GetFileName(sf.GetFileName()) + "(" + sf.GetFileLineNumber() + ")]: " + ex.Message);
            }
            finally
            {
                this.disconnect();
            }

            return ret;
        }

        /// <summary>
        /// analizza le differenze presenti tra la cartella lato client e quella lato server
        /// invia i files nuovi/modificati
        /// </summary>
        public void ClientSync(XmlManager xmlClient, string authToken, List<string> deletedFileList)
        {
            XElement xmlRootServer;
            XElement xmlRootClient;            

            try
            {
                // Mi connetto al server
                this.connect();

                // Calcolo l'md5 del mio ultimo xml
                string md5XmlCLient = xmlClient.XMLDigest();
                string md5XmlServer = getXmlDigest(authToken); // Scarico il digest dal server

                Logger.Debug("CLIENT: " + md5XmlCLient + "\nSERVER: " + md5XmlServer);

                if (String.Compare(md5XmlCLient, md5XmlServer) == 0)
                {
                    //TODO resetto il timer
                    Logger.Info("Le due cartelle sono perfettamente uguali, non e' necessario nessun aggiornamento");
                    this.sendLogout();
                }
                else
                {
                    //le due cartelle sono diverse apro la synch
                    xmlRootClient = xmlClient.GetRoot();
                    xmlRootServer = XmlManager.GetRoot(getLastXml(authToken));

                    #region Apro la synch
                    Command cmd = new Command(CmdType.startSynch);
                    Utilis.SendCmdSync(conn, cmd);

                    // Ricevo la risposta del server
                    Command resp = Utilis.GetCmdSync(conn);
                    if (resp == null || resp.kmd != CmdType.ok)
                    {
                        Logger.Error("errore inizio synch");
                        return;
                    }
                    #endregion

                    #region DELETED FILES
                    //Mando al server la lista dei file che ho cancellato
                    Logger.Info("il numero di elementi cancellati e': " + deletedFileList.Count);
                    if(deletedFileList.Count != 0)
                    {
                        sendDeletedFiles(deletedFileList, authToken);
                    }
                    else
                    {
                        // Non ho file da cancellare
                        FileNumCommand numFiles = new FileNumCommand(0, authToken);
                        Utilis.SendCmdSync(conn, numFiles);
                    }
                    #endregion

                    // Mi sincronizzo col server
                    Command k = Utilis.GetCmdSync(this.conn); 
                    if (k == null || !(k.kmd == CmdType.ok))
                        throw new Exception("Aspettavo un comando OK, ricevuto nulla o tipo errato");

                    #region SYNC SERVER -> CLIENT
                    //fase A: il client guarda se il server abbia dei files aggiornati o nuovi
                    //li memorizza in una lista e li richiede al server
                    List<VersionInfo> fileToGetList = new List<VersionInfo>();
                    XmlManager.checkDiffClientServerTOClient(xmlRootClient, xmlRootServer, fileToGetList, deletedFileList);

                    Logger.Info("il numero di elementi da chiedere al server e': " + fileToGetList.Count);
                    if (fileToGetList.Count != 0)
                        getFilesModifiedFromServer(fileToGetList, authToken);
                    else
                    {
                        // Non ho file da Ricevere
                        FileNumCommand numFiles = new FileNumCommand(0, authToken);
                        Utilis.SendCmdSync(conn, numFiles);
                    }
                    #endregion

                    k = Utilis.GetCmdSync(this.conn);
                    if (k == null || !(k.kmd == CmdType.ok))
                        throw new Exception("Aspettavo un comando OK, ricevuto nulla o tipo errato");

                    #region SYNC CLIENT -> SERVER
                    //fase B: il client seleziona i files non memorizzati dal server (nuovi o modificati)
                    //li memorizza in una lista e li invia al server
                    //guardo le differenze tra i due XDocuments e popolo r delle stringhe di richiesta da inviare al server
                    List<VersionInfo> fileToSend = new List<VersionInfo>();
                    XmlManager.checkDiffClientClientTOServer(xmlRootClient, xmlRootServer, fileToSend);

                    Logger.Info("il numero di elementi da inviare al server e': " + fileToSend.Count);
                    if(fileToSend.Count != 0)
                        sendFilesModifiedToServer(fileToSend, authToken);
                    else
                    {
                        // Non ho file da inviare
                        FileNumCommand numFiles = new FileNumCommand(0, authToken);
                        Utilis.SendCmdSync(conn, numFiles);
                    }
                    #endregion

                    #region Chiusura synch
                    // Chiudo la sessione di sincronizzazione
                    resp = Utilis.GetCmdSync(conn);
                    if (resp == null || resp.kmd != CmdType.ok)
                        throw new Exception("errore fine synch");

                    // Mando l'xml
                    //XmlCommand lastXml = new XmlCommand(xmlClient, authToken);
                    //Utilis.SendCmdSync(conn, lastXml);

                    Command end = new Command(CmdType.endSynch);
                    Utilis.SendCmdSync(conn, end);
                    Logger.Info("Synch terminata");
                    #endregion

                    deletedFileList.Clear();

                }
            }
            catch (Exception e)
            {
                StackTrace st = new StackTrace(e, true);
                StackFrame sf = Utilis.GetFirstValidFrame(st);

                Logger.Error("[" + Path.GetFileName(sf.GetFileName()) + "(" + sf.GetFileLineNumber() + ")]: " + e.Message);

                throw e;
            }
            finally
            {
                this.disconnect();
            }

        }

        /// <summary>
        /// Analizzo la 
        /// </summary>
        /// <param name="xmlClient"></param>
        /// <param name="authToken"></param>
        public XElement ClientBeginRestore(XmlManager xmlClient, string authToken)
        {
            XElement xmlRootServer;
            XElement xmlRootClient;


            try
            {
                // Mi connetto al server
                this.connect();
                
                //Richiedo l'xml con le versioni al server
                xmlRootClient = xmlClient.GetRoot();
                xmlRootServer = XmlManager.GetRoot(getLastXml(authToken, CmdType.getRestoreXML));

                return xmlRootServer;                
            }
            catch (Exception e)
            {
                StackTrace st = new StackTrace(e, true);
                StackFrame sf = Utilis.GetFirstValidFrame(st);

                Logger.Error("[" + Path.GetFileName(sf.GetFileName()) + "(" + sf.GetFileLineNumber() + ")]: " + e.Message);

                throw e;
            }
            finally
            {
                this.disconnect();
            }
        }

        /// <summary>
        /// Mando al server i file cancellati
        /// </summary>
        /// <param name="deletedFileList">Lista dei file cancellati</param>
        private void sendDeletedFiles(List<string> deletedFileList, string authToken)
        {
            int elementi = deletedFileList.Count;
            FileNumCommand numFiles = new FileNumCommand(deletedFileList.Count, authToken);
            Utilis.SendCmdSync(conn, numFiles);

            Logger.Info("Avvio la procedura di invio file cancellati al server. " + elementi + " file");

            foreach (string filePath in deletedFileList)
            {
                // Invio le informazioni sul file
                Command deletedFile = new Command(CmdType.deletedFile, filePath);
                Utilis.SendCmdSync(conn, deletedFile);

                Logger.Debug("Ho inviato cancellazione file " + filePath);
            }

            Logger.Info("File cancellati inviati al server");

        }
        
        /// <summary>
        /// invia al server i files creati/modificati
        /// </summary>
        private void sendFilesModifiedToServer(List<VersionInfo> fileToSendList, string authToken)
        {
            #region Preparazione synch
            //invio il numero di files che il server deve aspettarsi
            FileNumCommand numFiles = new FileNumCommand(fileToSendList.Count, authToken);
            Utilis.SendCmdSync(conn, numFiles);
            #endregion

            #region Invio file al server
            foreach (VersionInfo fileToSendInfo in fileToSendList)
            {
                // Invio le informazioni sul file
                FileInfoCommand f = new FileInfoCommand(fileToSendInfo.relPath, authToken);
                Utilis.SendCmdSync(conn, f);

                Utilis.SendFile(conn, f.AbsFilePath, f.FileSize);
                Logger.Debug("Ho inviato il file " + f.AbsFilePath);
            }
            #endregion

            Logger.Info("File inviati al server");
            
        }
        
        /// <summary>
        /// scarica dal server i files modificati
        /// </summary>
        private void getFilesModifiedFromServer(List<VersionInfo> fileToGetList, string authToken)
        {            
            #region Preparazione synch
            //invio il numero di files che il client richiede al server
            FileNumCommand numFiles = new FileNumCommand(fileToGetList.Count, authToken);
            Utilis.SendCmdSync(conn, numFiles);
            #endregion

            #region Richiedo i file al server
            foreach (VersionInfo fileToGetInfo in fileToGetList)
            {
                // Invio le informazioni sul file
                Command requestedFile = new Command(CmdType.fileName, fileToGetInfo.relPath);
                Utilis.SendCmdSync(conn, requestedFile);
                Logger.Debug("Ho inviato la richiesta del file " + fileToGetInfo.relPath);
                
                string destPath = Utilis.RelativeToAbsPath(fileToGetInfo.relPath, Settings.SynchPath);

                // Se il file esiste gia lo cancello
                if (File.Exists(destPath) == true)
                    File.Delete(destPath);

                // In ogni caso il percorso deve esistere
                if (Directory.Exists(Path.GetDirectoryName(destPath)) == false)
                    Directory.CreateDirectory(Path.GetDirectoryName(destPath));

                Utilis.GetFile(conn, destPath, fileToGetInfo.FileSize);
                //TODO modificare la data di ultima modifica del file ?

                Logger.Debug("Ho ricevuto il file: " + fileToGetInfo.relPath);
            }
            #endregion
            Logger.Info("Ho finito di scaricare dal server i files mancanti");
        }

        
        
        /// <summary>
        /// Ritorna l'md5 dell'XML dell'ultima versione posseduta dal server
        /// </summary>
        private string getXmlDigest(string authToken)
        {
            //invio un messaggio di richiesta al server
            Command xmlDigestRequest = new Command(CmdType.getXmlDigest);
            xmlDigestRequest.AuthToken = authToken;

            Utilis.SendCmdSync(this.conn, xmlDigestRequest);

            //attendo la risposta da parte del server (mi invia l'md5)
            XmlDigestCommand md5LastXmlServer = new XmlDigestCommand(Utilis.GetCmdSync(this.conn));

            if (md5LastXmlServer == null || md5LastXmlServer.kmd != CmdType.xmlDigest)
            {
                this.sendLogout();
                throw new Exception("Aspettavo un comando contenente un md5, ricevuto nulla o errato");
            }

            return md5LastXmlServer.Digest;
        }

        /// <summary>
        /// Ritorna l'XML dell'ultima versione posseduta dal server
        /// </summary>
        public XDocument getLastXml(string authToken, CmdType cmdType = CmdType.getXML)
        {
            Command richiestaXml = new Command(cmdType);
            richiestaXml.AuthToken = authToken;

            Utilis.SendCmdSync(this.conn, richiestaXml);

            //il client riceve l'xml completo
            XmlCommand lastXmlServer = new XmlCommand(Utilis.GetCmdSync(this.conn));
            
            if (lastXmlServer == null || lastXmlServer.kmd != CmdType.Xml)
                throw new Exception("Aspettavo un comando contenente un Xml, ricevuto nulla o errato");


            return XDocument.Parse(lastXmlServer.Payload);
        }
       
        /// <summary>
        /// Invio al server un logout
        /// </summary>
        private void sendLogout()
        {
            Command cmd = new Command(CmdType.logout);
            Utilis.SendCmdSync(this.conn, cmd);
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


        /// <summary>
        /// invia la richiesta di restore del file selezionato al server
        /// </summary>
        /// <param name="relPath"></param>
        /// <param name="versionId"></param>
        public void requestRestore(String relPath, int versionId, int fileSize, string authToken) {
            RestoreFileCommand restoreFile = new RestoreFileCommand(relPath, versionId, authToken);
            Utilis.SendCmdSync(conn, restoreFile);


            //TODO testare la risposta del server -> attualmente risponde con un comando di errore o direttamente con il file
            //non è meglio se il server risponde sempre con un comando e nel caso di file trovato segua poi l'invio del file?

            if (File.Exists(Settings.SynchPath + relPath))
            {
                File.Delete(Settings.SynchPath + relPath);
            }

            Utilis.GetFile(conn, relPath, fileSize);
        }
    }
}
