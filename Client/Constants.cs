using System;

namespace Client
{
    class Constants
    {
        /// <summary>
        /// Abilito o meno i LOG di tipo Debug
        /// </summary>
        public const bool DebugEnabled = true;
        
        /// <summary>
        /// Dimensione del campo comando
        /// </summary>
        public const int CommandTypeBytes = sizeof(CmdType);

        /// <summary>
        /// Dimensione del campo payloadLength
        /// </summary>
        public const int CommandLengthBytes = sizeof(Int64);

        /// <summary>
        /// Dimensione totale del header
        /// </summary>
        public const int HeaderSize = CommandLengthBytes + CommandTypeBytes + AuthTokenLength;

        /// <summary>
        /// Percorso di test
        /// </summary>
        public const string PathClient = @"D:\PDSCartellaPDS\ClientSide";

        /// <summary>
        /// Percorso di test
        /// </summary>
        public const string PathServerFile = @"D:\PDSCartellaPDS\ServerBackup";
       
        /// <summary>
        /// Percorso dove salvare il file xml
        /// </summary>
        public const string XmlSavePath = @"D:\PDSCartellaPDS";

        /// <summary>
        /// Percorso dell'icona
        /// </summary>
        public const string IcoPath = @"D:\PDSCartellaPDS\icona.ico";

        /// <summary>
        /// Formato della stringa della data nell'xml
        /// </summary>
        public const string XmlDateFormat = @"yyyy'-'MM'-'dd'T'HH':'mm':'ssK";

        /// <summary>
        /// Tempo di refresh per la tree view in ms
        /// </summary>
        public const int TreeViewRefreshTimerInterval = 1000;

        /// <summary>
        /// Separatore tra elementi di percorso es. C:\Users\Fabio\Desktop\TEST
        /// </summary>
        public const string PathSeparator = @"\";

        /// <summary>
        /// Dimensione del chunk durante l'invio di un file
        /// </summary>
        public const int FileTransferChunkSize = 1024;

        /// <summary>
        /// Lunghezza minima username
        /// </summary>
        public const int MinUsernameLength = 4;

        /// <summary>
        /// Lunghezza massima username
        /// </summary>
        public const int MaxUsernameLength = 16;

        /// <summary>
        /// Lunghezza minima password
        /// </summary>
        public const int MinPasswordLength = 5;

        /// <summary>
        /// Lunghezza massima password
        /// </summary>
        public const int MaxPasswordLength = 32;

        /// <summary>
        /// Dimensione di un HASH MD5
        /// </summary>
        public const int MD5OutputLegth = 32;

        /// <summary>
        /// Lunghezza dell'auth token 
        /// </summary>
        public const int AuthTokenLength = 12;

        /// <summary>
        /// Auth token di default, INVALIDO
        /// </summary>
        public const string DefaultAuthToken = "------------";

        /// <summary>
        /// AuthToken del server, per la mutua autenticazione
        /// </summary>
        public const string ServerAuthToken = "AK3Y8TPZE9XT";
    }

    /// <summary>
    /// Enumerazione dei comandi
    /// </summary>
    public enum CmdType : int
    {
        #region GESTIONE UTENTI
        /// <summary>
        /// Comando per effettuare il login di un utente [lenUsername|username|lenPwd|pwd]
        /// </summary>
        login,
        /// <summary>
        /// Comando per la registrazione di un utente [lenUsername|username|lenPwd|pwd]
        /// </summary>
        registration,
        /// <summary>
        /// Comando che il client manda al server quando vuole chiudere la sessione
        /// </summary>
        logout,
        #endregion

        #region GESTIONE SINCRONIZZAZIONE
        /// <summary>
        /// Comando per ottenere l'ultimo XML disponibile e il suo digest (faccio prima un controllo sul digest) [md5|lenXML|XML]
        /// </summary>
        getXmlDigest,       // senza payload
        xmlDigest,          // XmlDigestCommand
        getXML,             // senza payload
        Xml,                // XmlCommand
        numFile,
        sendFile,
        startSynch,
        endSynch,
        infoNum,        
        #endregion

        #region COMANDI GENERICI
        /// <summary>
        /// Il precedente comando ha avuto successo
        /// </summary>
        ok,
        /// <summary>
        /// Il precedente comando ha generato un errore, fare riferimento al codice dell'errore contenuto nel payload
        /// </summary>
        error,
        bye
        #endregion

    };

    /// <summary>
    /// Codici di errore
    /// </summary>
    public enum ErrorCode : Int32
    {
        syntaxError,
        credentialsNotValid,
        argumentNotValid,
        networkError,
        unexpectedMessageType,
        usernameAlreadyPresent,
        usernameLengthNotValid,
        passwordLengthNotValid

    }
}

