using System;
using System.Net.Sockets;
using System.Threading;
using System.Xml.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;

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
                //conn.SendTimeout

                //TODO Prendere i dati dalla configurazione
                conn.Connect("127.0.0.1", 10000);

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
                System.Windows.MessageBox.Show(ex.Message, "Errore Registrazione", MessageBoxButton.OK, MessageBoxImage.Error);

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
        public void ClientSync(XmlManager xmlClient, string authToken)
        {
            XElement xmlRootServer;
            XElement xmlRootClient;
            List<String> refList = new List<String>();
            
            string r = "";
            string path = "";

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
                    Logger.Info("Le due cartelle sono perfettamente uguali, non e necessario nessun aggiornamento");
                    this.sendLogout();
                }
                else
                {
                    //le due cartelle sono diverse
                    xmlRootClient = xmlClient.GetRoot();
                    xmlRootServer = XmlManager.GetRoot(getLastXml(authToken));

                    //guardo le differenze tra i due XDocuments e popolo r delle stringhe di richiesta da inviare al server
                    int elementsNumber = XmlManager.checkDiff(xmlRootClient, xmlRootServer, refList, ref r, path);
                    Logger.Info("l'elenco comandi inviati al server e': \n" + r);
                    Logger.Info("il numero di elementi da inviare al server e': " + elementsNumber);

                    if(elementsNumber == 0)
                    {
                        Logger.Debug("Chiudo perche' non ho file da salvare");
                        this.sendLogout();
                    }
                    else
                        updateDirectory(xmlClient, elementsNumber, refList, authToken);

                }
            }
            catch (Exception e)
            {
                throw e;
            }
            finally
            {
                this.disconnect();
            }

        }


        /// <summary>
        /// invia al server i files creati/modificati
        /// </summary>
        /// <param name="elementi">numero di elementsNumber da inviare al server</param>
        private void updateDirectory(XmlManager xmlClient, int elementi, List<String> refString, string authToken)
        {
            #region Preparazione synch
            // Devo inviare degli aggiornamenti, apro la synch
            Command cmd = new Command(CmdType.startSynch);
            Utilis.SendCmdSync(conn, cmd);

            // Ricevo la risposta del server
            Command resp = Utilis.GetCmdSync(conn);
            if (resp == null || resp.kmd != CmdType.ok)
            {
                Logger.Error("errore inizio synch");
                return;
            }

            //invio il numero di files che il server deve aspettarsi
            FileNumCommand numFiles = new FileNumCommand(elementi, authToken);
            Utilis.SendCmdSync(conn, numFiles);
            #endregion

            Logger.Info("Avvio la procedura di invio file al server. " + elementi + " file");
            
            #region Invio file
            //testare che la dimensione inviata sia corretta
            foreach (string filePath in refString)
            {
                // Invio le informazioni sul file
                FileInfoCommand f = new FileInfoCommand(filePath, authToken);
                Utilis.SendCmdSync(conn, f);

                Utilis.SendFile(conn, f.AbsFilePath, f.FileSize);
                Logger.Info("Ho inviato il file " + f.AbsFilePath);
            }
            #endregion

            Logger.Info("File inviati al server");

            #region Chiusura synch
            // Chiudo la sessione di sincronizzazione
            resp = Utilis.GetCmdSync(conn);
            if (resp == null || resp.kmd != CmdType.ok)
            {
                Logger.Error("errore fine synch");
                return;
            }

            // Mando l'xml
            //XmlCommand lastXml = new XmlCommand(xmlClient, authToken);
            //Utilis.SendCmdSync(conn, lastXml);

            Command end = new Command(CmdType.endSynch);
            Utilis.SendCmdSync(conn, end);
            Logger.Info("Synch terminata");
            #endregion
            
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
        public XDocument getLastXml(string authToken)
        {
            Command richiestaXml = new Command(CmdType.getXML);
            richiestaXml.AuthToken = authToken;

            Utilis.SendCmdSync(this.conn, richiestaXml);

            //il client riceve l'xml completo
            XmlCommand lastXmlServer = new XmlCommand(Utilis.GetCmdSync(this.conn));
            
            if (lastXmlServer == null || lastXmlServer.kmd != CmdType.Xml)
            {
                this.sendLogout();
                throw new Exception("Aspettavo un comando contenente un Xml, ricevuto nulla o errato");
            }

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
    }
}
