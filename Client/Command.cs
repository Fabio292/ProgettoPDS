using System;
using System.IO;
using System.Text;

namespace Client
{
    /// <summary>
    /// Il comando è composto da [tipo|auth_token|lunghezza_payload|payload]
    /// </summary>
    class Command
    {
        private CmdType _kmd;
        private Int64 _payloadLength;
        private string _authToken;
        private string _payload;


        public Int64 PayloadLength
        {
            get { return _payloadLength; }
        }

        public string Payload
        {
            get { return _payload; }
            set
            {
                _payload = value;
                this._payloadLength = _payload.Length;
            }
        }

        public CmdType kmd
        {
            get { return _kmd; }
            set { _kmd = value; }
        }

        public string AuthToken
        {
            get
            {
                return _authToken;
            }

            set
            {
                _authToken = value;
            }
        }


        /// <summary>
        /// Costruisco un generico comando
        /// </summary>
        /// <param name="_kmd">Il tipo di comando</param>
        /// <param name="_payload">Payload dentro il comando</param>
        public Command(CmdType _kmd, string _payload)
        {
            this.Payload = _payload;
            this._kmd = _kmd;
            this._authToken = Constants.DefaultAuthToken;
        }

        public Command(CmdType _kmd)
        {
            this._authToken = Constants.DefaultAuthToken;
            this.Payload = "";
            this._kmd = _kmd;
        }

        /// <summary>
        /// Costruisco un comando vuoto (tipo default: OK)
        /// </summary>
        public Command()
        {
            this._authToken = Constants.DefaultAuthToken;
            this.Payload = "";
            this._kmd = CmdType.ok;

        }

        public override string ToString()
        {
            return _kmd + _payloadLength.ToString() + _authToken + _payload;
        }

        /// <summary>
        /// Converto in un vettore di byte il comando attuale
        /// </summary>
        public byte[] ToBytes()
        {
            byte[] ret = new byte[this.GetTotalLength()];
            int offset = 0;

            //Copio nel buffer il codice del comando
            Buffer.BlockCopy(BitConverter.GetBytes((int)this.kmd), 0, ret, offset, Constants.CommandTypeBytes);
            offset += Constants.CommandTypeBytes;

            //Copio nel buffer la dimensione del Payload
            Buffer.BlockCopy(BitConverter.GetBytes(this.PayloadLength), 0, ret, offset, Constants.CommandLengthBytes);
            offset += Constants.CommandLengthBytes;

            //Copio nel buffer l'auth token
            Buffer.BlockCopy(Encoding.UTF8.GetBytes(this.AuthToken), 0, ret, offset, Constants.AuthTokenLength);
            offset += Constants.AuthTokenLength;

            //Copio nel buffer il Payload
            Buffer.BlockCopy(Encoding.UTF8.GetBytes(this.Payload), 0, ret, offset, Convert.ToInt32(this.PayloadLength));

            return ret;
        }

        /// <summary>
        /// Ottengo la lunghezza totale del comando forzata a Int32
        /// </summary>
        public Int32 GetTotalLength()
        {
            return Constants.CommandTypeBytes + Constants.CommandLengthBytes + Constants.AuthTokenLength +  Convert.ToInt32(_payloadLength);
        }
    }

    /// <summary>
    /// Comando di login. Sintassi [lenUsername|username|lenPwd|pwd]
    /// </summary>
    class SendCredentials : Command
    {
        #region Dimensioni Header
        private static readonly Int32 UsernameLengthSize = sizeof(Int32);
        private static readonly Int32 PasswordLengthSize = sizeof(Int32);
        #endregion

        private string _username = "";
        private string _password = "";

        public string Username
        {
            get { return _username; }
            set
            {
                _username = value;
                // Ad ogni modifica rigenero il Payload
                this.Payload = this.generatePayload();
            }
        }

        public string Password
        {
            get { return _password; }
            set
            {
                _password = value;
                // Ad ogni modifica rigenero il Payload
                this.Payload = this.generatePayload();
            }
        }

        /// <summary>
        /// Creo un oggetto SendCredentials con i parametri passati
        /// </summary>
        /// <param name="username">L'username scelto</param>
        /// <param name="password">La password scelta</param>
        /// <exception cref="ArgumentException">Eccezione lanciata quando il comando passato non è valido</exception>
        public SendCredentials(string username, string password, CmdType type) : base()
        {
            // Imposto il tipo di comando
            this.kmd = type;

            // Controllo la validità dei parametri in input
            if ((username == null) || (username.Length < 1))
                throw new ArgumentException("Username troppo corto");
            if ((password == null) || (password.Length < 1))
                throw new ArgumentException("Password troppo corta");

            this.Password = password;
            this.Username = username;
        }

        /// <summary>
        /// Estraggo da un comando generico i dati per costruire un SendCredentials, Lancio un'eccezione se non è possibile
        /// </summary>
        /// <param name="cmd">Il comando generico ricevuto via socket</param>
        /// <exception cref="ArgumentException">Eccezione lanciata quando il comando passato non è valido</exception>
        public SendCredentials(Command cmd) : base()
        {
            if (cmd == null)
                throw new ArgumentException("Il comando passato è vuoto");

            switch (cmd.kmd)
            {
                case CmdType.login:
                    this.kmd = CmdType.login;
                    break;

                case CmdType.registration:
                    this.kmd = CmdType.registration;
                    break;

                default:
                    throw new ArgumentException("Il comando non e' di tipo corretto, validi (login|registrazione), ricevuto " + Utilis.Cmd2String(cmd.kmd));
            }

            // Il Payload deve contenere almeno la lunghezza di password e username
            if (cmd.PayloadLength < (SendCredentials.PasswordLengthSize + SendCredentials.UsernameLengthSize))
                throw new ArgumentException("Il comando passato non è formattato correttamente");

            this.extractField(cmd.Payload);

            // Copio la stringa del Payload
            this.Payload = cmd.Payload;
        }

        /// <summary>
        /// Estraggo la dimensione dei campi dal Payload
        /// </summary>
        private void extractFieldSize(string payloadP, out int lenUsername, out int lenPwd)
        {
            try
            {
                // Estraggo le dimensioni
                lenUsername = BitConverter.ToInt32(Encoding.UTF8.GetBytes(payloadP.Substring(0, SendCredentials.UsernameLengthSize)), 0);
                lenPwd = BitConverter.ToInt32(Encoding.UTF8.GetBytes(payloadP.Substring(SendCredentials.UsernameLengthSize + lenUsername, SendCredentials.PasswordLengthSize)), 0);

                // Controllo se le dimensioni sono valide
                if (lenPwd < 0 || lenUsername < 0)
                    throw new Exception("Lunghezza campi negativa");

                //Controllo se le dimensioni sono coerenti
                Int32 expectedPayloadSize = SendCredentials.PasswordLengthSize + SendCredentials.UsernameLengthSize + lenPwd + lenUsername;
                if (payloadP.Length != expectedPayloadSize)
                    throw new Exception("La dimensione del payload non è corretta");
            }
            catch (Exception e)
            {
                throw new ArgumentException("Il comando passato non è formattato correttamente (" + e.Message + ")");
            }
        }

        /// <summary>
        /// Estraggo il valore dei campi e lo vado a salvare direttamente nelle proprietà
        /// </summary>
        /// <param name="payloadP"></param>
        private void extractField(string payloadP)
        {
            // Inizio il parsing dei campi
            Int32 lenUsername = 0;
            Int32 lenPwd = 0;

            this.extractFieldSize(payloadP, out lenUsername, out lenPwd);

            // Se arrivo a questo punto la lunghezza è valida e devo estrarre i campi
            this._username = payloadP.Substring(SendCredentials.UsernameLengthSize, lenUsername);
            this._password = payloadP.Substring(SendCredentials.UsernameLengthSize + lenUsername + SendCredentials.PasswordLengthSize, lenPwd);
        }

        /// <summary>
        /// Genero la stringa da mettere nel Payload formattata secondo specifiche
        /// </summary>
        private string generatePayload()
        {
            string ret = "";

            Int32 lenUsername = this._username.Length;
            Int32 lenPwd = this._password.Length;

            ret += Encoding.UTF8.GetString(BitConverter.GetBytes(lenUsername));
            ret += this._username.ToString();
            ret += Encoding.UTF8.GetString(BitConverter.GetBytes(lenPwd));
            ret += this._password.ToString();

            return ret;
        }
    }

    /// <summary>
    /// Comando di errore. il codice è contenuto nel payload come numero convertito a stringa
    /// </summary>
    class ErrorCommand : Command
    {
        private ErrorCode _errCode;

        public ErrorCode Code
        {
            get { return _errCode; }
            set
            {
                _errCode = value;
                this.Payload = generatePayload(_errCode);
            }
        }

        /// <summary>
        /// Creo un oggetto ErrorCommand passando il codice dell'errore
        /// </summary>
        /// <param name="errC">Il codice dell'errore da segnalare</param>
        public ErrorCommand(ErrorCode errC) : base()
        {
            this.kmd = CmdType.error;
            this.Code = errC;
        }


        public ErrorCommand(Command cmd) : base()
        {
            if ((cmd == null) || (cmd.kmd != CmdType.error))
                throw new ArgumentException("Il comando passato è vuoto oppure non di tipo error");

            this.kmd = CmdType.error;
            try
            {
                this._errCode = extractField(cmd.Payload);
                this.Payload = cmd.Payload;
            }
            catch (Exception)
            {
                throw new ArgumentException("Il comando passato non è formattato correttamente");
            }

        }


        public ErrorCode extractField(string payloadP)
        {
            int enumVal = Convert.ToInt32(payloadP);

            if (Enum.IsDefined(typeof(ErrorCode), enumVal) == false)
                throw new Exception();

            // Se arrivo a questo punto il valore nel payload corrisponde ad una enum valida
            return (ErrorCode)enumVal;
        }

        private string generatePayload(ErrorCode errC)
        {
            string ret = "";

            ret = Convert.ToString((Int32)errC);

            return ret;
        }
    }

    /// <summary>
    /// Comando per iniziare l'invio di un file
    /// Vengono spediti i metadati nel payload [path|size|lastModTime] (size ha dimensione fissa di 8 byte)
    /// </summary>
    class FileInfoCommand : Command
    {

        private string _relFilePath;
        private Int64 _fileSize;
        private DateTime _lastModTime;
        private static char cmdSeparator = '|';

        /// <summary>
        /// Il percorso del file relativo alla cartella di backup (senza '\' iniziale)
        /// </summary>
        public string RelFilePath
        {
            get
            {
                return _relFilePath;
            }

            private set { }
        }

        /// <summary>
        /// Il percorso assoluto del file
        /// </summary>
        public string AbsFilePath
        {
            get
            {
                return Utilis.RelativeToAbsPath(_relFilePath, Settings.SynchPath);
            }

            private set { }
        }

        /// <summary>
        /// Dimensione in byte del file che sto per ricevere
        /// </summary>
        public long FileSize
        {
            get
            {
                return _fileSize;
            }

            private set { }
        }

        /// <summary>
        /// Data di ultima modifica del file che sto per ricevere
        /// </summary>
        public DateTime LastModTime
        {
            get
            {
                return _lastModTime;
            }

            private set { }
        }

        /// <summary>
        /// Crea l'oggetto FileInfoCommand caricando i parametri del file passato
        /// </summary>
        /// <param name="relPath">Percorso relativo (senza il '\' iniziale) del file</param>
        /// <exception cref="ArgumentException">Eccezione lanciata quando il percorso del file non è valido</exception>
        public FileInfoCommand(string relPath, string authToken) : base()
        {
            string absPath = Utilis.RelativeToAbsPath(relPath, Settings.SynchPath);

            // Controllo se il file esiste e se è veramente un file
            if (!File.Exists(absPath))
                throw new ArgumentException("Il percorso passato come parametro non esiste");
            if (Utilis.IsDirectory(absPath) == true)
                throw new ArgumentException("Il percorso passato come parametro è relativo ad una cartella");
            if(authToken.Length != Constants.AuthTokenLength)
                throw new ArgumentException("Auth token non valido");

            FileInfo fInf = new FileInfo(absPath);

            // Imposto il tipo di comando
            this.kmd = CmdType.sendFile;

            // Recupero le informazioni sul file
            this._relFilePath = relPath;
            this._fileSize = fInf.Length;
            this._lastModTime = Utilis.NormalizeDateTime(fInf.LastWriteTime);
            this.AuthToken = authToken;

            // Salvo il payload
            this.Payload = this.generatePayload();
        }

        /// <summary>
        /// Estraggo da un comando generico i dati per costruire un FileInfoCommand, Lancio un'eccezione se non è possibile
        /// </summary>
        /// <param name="cmd">Il comando generico ricevuto via socket</param>
        /// <exception cref="ArgumentException">Eccezione lanciata quando il comando passato non è valido</exception>
        public FileInfoCommand(Command cmd) : base()
        {
            if ((cmd == null) || (cmd.kmd != CmdType.sendFile))
                throw new ArgumentException("Il comando passato è vuoto oppure non di tipo sendFile");

            // Salvo l'auth token
            this.AuthToken = cmd.AuthToken;

            // Imposto il tipo di comando
            this.kmd = CmdType.sendFile;

            // Estraggo i campi
            this.extractField(cmd.Payload);

            // Se arrivo a questo punto ho finito di leggere il comando ricevuto
        }


        /// <summary>
        /// Estraggo il valore dei campi e lo vado a salvare direttamente nelle proprietà
        /// </summary>
        private void extractField(string payloadP)
        {
            char[] splitChar = new char[] { FileInfoCommand.cmdSeparator };
            string[] token = payloadP.Split(splitChar, StringSplitOptions.RemoveEmptyEntries);

            // Devo avere SOLO 3 token: [path|size|lastModTime]
            if (token.Length != 3)
                throw new ArgumentException("Il comando passato non è formattato correttamente");

            // Estraggo i campi
            this._relFilePath = token[0];

            try
            {
                this._fileSize = Int64.Parse(token[1]);
                this._lastModTime = DateTime.Parse(token[2]);
                this.Payload = this.generatePayload();
            }
            catch (Exception e)
            {
                throw new ArgumentException("Il comando passato non è formattato correttamente (" + e.Message + ")");
            }
        }

        /// <summary>
        /// Genero la stringa da mettere nel Payload formattata secondo specifiche
        /// </summary>
        private string generatePayload()
        {
            //[path|size|lastModTime]
            string ret = "";

            ret += this._relFilePath;
            ret += FileInfoCommand.cmdSeparator;
            ret += this._fileSize.ToString();
            ret += FileInfoCommand.cmdSeparator;
            ret += this._lastModTime.ToString();


            return ret;
        }

    }

    /// <summary>
    /// Comando per inviare il digest dell'xml del server. Il client deve poi confrontarlo con la sua versione
    /// </summary>
    class XmlDigestCommand : Command
    {

        public String Digest 
        {
            get { return Payload; }
            private set
            {
                this.Payload = value;
            }
        }

        /// <summary>
        /// Creo un oggetto XmlDigestCommand passando l'xml relativo
        /// </summary>
        /// <param name="errC">Il codice dell'errore da segnalare</param>
        public XmlDigestCommand(XmlManager xml, string authToken) : base()
        {
            if (authToken.Length != Constants.AuthTokenLength)
                throw new ArgumentException("Auth token non valido");

            this.AuthToken = authToken;
            this.kmd = CmdType.xmlDigest;

            string md5Xml = xml.XMLDigest();
            this.Digest = md5Xml;
        }


        public XmlDigestCommand(Command cmd) : base()
        {
            if ((cmd == null) || (cmd.kmd != CmdType.xmlDigest))
                throw new ArgumentException("Il comando passato è vuoto oppure non di tipo xmlDigest");

            this.kmd = CmdType.xmlDigest;
            try
            {
                this.Digest = extractField(cmd.Payload);
                this.AuthToken = cmd.AuthToken;
            }
            catch (Exception)
            {
                throw new ArgumentException("Il comando passato non è formattato correttamente");
            }

        }

        /// <summary>
        /// Estraggo il valore del digest, in questo caso tutto il payload e controllo la sua validità
        /// </summary>
        private String extractField(string payloadP)
        {
            if (payloadP.Length != Constants.MD5OutputLegth)
                throw new Exception();

            return payloadP;
        }

    }

    /// <summary>
    /// Comando per inviare l'xml del server.
    /// </summary>
    class XmlCommand : Command
    {

        public string Xml
        {
            get { return Payload; }

            set
            {
                Payload = value;
            }
        }

        /// <summary>
        /// Creo un oggetto XmlDigestCommand passando l'xml relativo
        /// </summary>
        public XmlCommand(XmlManager xml, string authToken) : base()
        {
            if (authToken.Length != Constants.AuthTokenLength)
                throw new ArgumentException("Auth token non valido");

            this.AuthToken = authToken;
            this.kmd = CmdType.Xml;

            // Metto l'XML nel payload del comando
            this.Xml = xml.ToString();
            
        }


        public XmlCommand(Command cmd) : base()
        {
            if ((cmd == null) || (cmd.kmd != CmdType.Xml))
                throw new ArgumentException("Il comando passato è vuoto oppure non di tipo xmlDigest");

            this.kmd = CmdType.Xml;
            try
            {
                this.Xml = extractField(cmd.Payload);
                this.AuthToken = cmd.AuthToken;
            }
            catch (Exception)
            {
                throw new ArgumentException("Il comando passato non è formattato correttamente");
            }

        }

        /// <summary>
        /// Estraggo il valore del digest, in questo caso tutto il payload e controllo la sua validità
        /// </summary>
        private String extractField(string payloadP)
        {
            return payloadP;
        }

    }

    /// <summary>
    /// Comando per inviare il numero di file da mandare al server
    /// </summary>
    class FileNumCommand : Command
    {

        public int NumFiles
        {
            get { return Int32.Parse(Payload); }

            set
            {
                Payload = value.ToString();
            }
        }

        /// <summary>
        /// Creo un oggetto FileNumCommand passando il numero di file
        /// </summary>
        public FileNumCommand(int num, string authToken) : base()
        {
            if (authToken.Length != Constants.AuthTokenLength)
                throw new ArgumentException("Auth token non valido");

            this.AuthToken = authToken;

            this.kmd = CmdType.numFile;

            this.NumFiles = num;
            
        }


        public FileNumCommand(Command cmd) : base()
        {
            if ((cmd == null) || (cmd.kmd != CmdType.numFile))
                throw new ArgumentException("Il comando passato è vuoto oppure non di tipo numFile");

            this.kmd = CmdType.numFile;
            try
            {
                this.NumFiles = extractField(cmd.Payload);
                this.AuthToken = cmd.AuthToken;
            }
            catch (Exception)
            {
                throw new ArgumentException("Il comando passato non è formattato correttamente");
            }

        }

        /// <summary>
        /// Estraggo il valore del digest, in questo caso tutto il payload e controllo la sua validità
        /// </summary>
        private Int32 extractField(string payloadP)
        {
            int ret = 0;
            
            ret = Int32.Parse(payloadP);           

            return ret;
        }

    }

    /// <summary>
    /// Comando per iniziare l'invio di un file
    /// Vengono spediti i metadati nel payload [path|size|lastModTime] (size ha dimensione fissa di 8 byte)
    /// </summary>
    class RestoreFileCommand : Command
    {

        private string _relFilePath;
        private int _versionId;
        private static char cmdSeparator = '|';

        /// <summary>
        /// Il percorso del file relativo alla cartella di backup (senza '\' iniziale)
        /// </summary>
        public string RelFilePath
        {
            get { return _relFilePath; }
            private set { }
        }

        public int VersionID
        {
            get { return _versionId; }
            private set { }
        }

        /// <summary>
        /// Crea l'oggetto RestoreFileCommand caricando i parametri del file passato
        /// </summary>
        /// <param name="relPath">Percorso relativo (senza il '\' iniziale) del file</param>
        /// <exception cref="ArgumentException">Eccezione lanciata quando il percorso del file non è valido</exception>
        public RestoreFileCommand(string relPath, int version, string authToken) : base()
        {

            if (version < 0)
                throw new ArgumentException("version non valido");
            if (authToken.Length != Constants.AuthTokenLength)
                throw new ArgumentException("Auth token non valido");


            // Imposto il tipo di comando
            this.kmd = CmdType.restoreFile;

            // Recupero le informazioni sul file
            this._relFilePath = relPath;
            this._versionId = version;
            this.AuthToken = authToken;

            // Salvo il payload
            this.Payload = this.generatePayload();
        }

        /// <summary>
        /// Estraggo da un comando generico i dati per costruire un RestoreFileCommand, Lancio un'eccezione se non è possibile
        /// </summary>
        /// <param name="cmd">Il comando generico ricevuto via socket</param>
        /// <exception cref="ArgumentException">Eccezione lanciata quando il comando passato non è valido</exception>
        public RestoreFileCommand(Command cmd) : base()
        {
            if ((cmd == null) || (cmd.kmd != CmdType.restoreFile))
                throw new ArgumentException("Il comando passato è vuoto oppure non di tipo restoreFile");

            // Salvo l'auth token
            this.AuthToken = cmd.AuthToken;

            // Imposto il tipo di comando
            this.kmd = CmdType.restoreFile;

            // Estraggo i campi
            this.extractField(cmd.Payload);
        }


        /// <summary>
        /// Estraggo il valore dei campi e lo vado a salvare direttamente nelle proprietà
        /// </summary>
        private void extractField(string payloadP)
        {
            char[] splitChar = new char[] { RestoreFileCommand.cmdSeparator };
            string[] token = payloadP.Split(splitChar, StringSplitOptions.RemoveEmptyEntries);

            // Devo avere SOLO 2 token: [relPath|vesionID]
            if (token.Length != 2)
                throw new ArgumentException("Il comando passato non è formattato correttamente");

            // Estraggo i campi
            this._relFilePath = token[0];

            try
            {
                this._versionId = Int32.Parse(token[1]);
                this.Payload = this.generatePayload();
            }
            catch (Exception e)
            {
                throw new ArgumentException("Il comando passato non è formattato correttamente (" + e.Message + ")");
            }
        }

        /// <summary>
        /// Genero la stringa da mettere nel Payload formattata secondo specifiche
        /// </summary>
        private string generatePayload()
        {
            //[path|size|lastModTime]
            string ret = "";

            ret += this._relFilePath;
            ret += RestoreFileCommand.cmdSeparator;
            ret += this._versionId.ToString();


            return ret;
        }

    }

}
