using System;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Diagnostics;
using System.Threading;

namespace Client
{
    class Utilis
    {
        #region MD5
        /// <summary>
        /// Calcola l'hash md5 del contenuto del file </summary>
        /// <param name="f">Percorso del file</param>
        /// <returns>Una stringa lunga 32 caratteri contenente l'md5 del file</returns>
        public static string MD5sum(FileInfo f)
        {
            return MD5sum(f.FullName);
        }

        /// <summary>
        /// Calcola l'hash md5 del contenuto del file </summary>
        /// <param name="file">Percorso del file</param>
        /// <returns>Una stringa lunga 32 caratteri contenente l'md5 del file</returns>
        public static string MD5sum(string file)
        {
            Exception last = null;
            
            for (int i = 0; i < 5; i++) // HACK mostruoso
            {
                try
                {
                    using (var stream = new BufferedStream(File.OpenRead(file), 512 * 1024))
                    {
                        using (var md5 = MD5.Create())
                        {
                            byte[] checksum = md5.ComputeHash(stream);
                            return BitConverter.ToString(checksum).Replace("-", string.Empty).ToLower();
                            
                        }
                    }
                }
                catch (Exception e)
                {
                    Thread.Sleep(50);
                    last = e;                   
                }
            }
            // Se arrivo qua c'è stato un problema
            throw last;            
        }

        /// <summary>
        /// Calcolo l'md5 di una stringa passata come parametro
        /// </summary>
        /// <param name="p">Stringa da hashare</param>
        /// <returns>L'hash md5 della stringa</returns>
        public static string Md5String(string p)
        {
            // step 1, calculate MD5 hash from input
            MD5 md5 = System.Security.Cryptography.MD5.Create();

            byte[] inputBytes = System.Text.Encoding.ASCII.GetBytes(p);
            byte[] hash = md5.ComputeHash(inputBytes);

            // step 2, convert byte array to hex string
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < hash.Length; i++)
            {
                sb.Append(hash[i].ToString("X2"));
            }
            return sb.ToString();
        }

        #endregion


        /// <summary>
        /// Normalizza la dimensione di un file per ottenere una stringa con il suffisso MB KB GB
        /// </summary>
        /// <param name="bytes">Dimensione in byte del file</param>
        /// <returns></returns> 
        public static string NormalizeSize(long bytes)
        {
            string ret = "";

            if (bytes > (1024 * 1024 * 1024))
                ret = (bytes / 1024 / 1024 / 1024).ToString() + " GB";
            else if (bytes > (1024 * 1024))
                ret = (bytes / 1024 / 1024).ToString() + " MB";
            else if (bytes > (1024))
                ret = (bytes / 1024).ToString() + " KB";
            else
                ret = bytes.ToString() + " B";

            return ret;
        }

        /// <summary>
        /// Normalizzo una data impostando a 0 i millisecondi
        /// </summary>
        /// <param name="dt">Oggetto Data da normalizzare</param>
        /// <returns>Una data normalizzata senza millisecondi</returns>
        public static DateTime NormalizeDateTime(DateTime dt)
        {
            return new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, dt.Second, 0, dt.Kind);
        }

        #region File Path
        /// <summary>
        /// Controllo se il perscorso passato come parametro e' una cartella o un file, altrimenti lancio un eccezione </summary>
        /// <exception cref="FileNotFoundException">Eccezione lanciata quando il percorso dell'oggetto passato come parametro non esiste</exception>
        /// <param name="path">Il percorso dell'oggetto da controllare</param>
        /// <returns>TRUE se directory, altrimenti FALSE</returns>
        public static bool IsDirectory(string path)
        {
            // Leggo le informazioni sul file
            FileAttributes attr = File.GetAttributes(path);

            // Controllo se è una dir o un file
            if ((attr & FileAttributes.Directory) == FileAttributes.Directory)
                return true;
            else
                return false;
        }

        /// <summary>
        /// Estraggo il percorso relativo da un percorso assoluto</summary>
        /// <param name="abs">Il percorso assoluto (es: C:\blabla\a.txt)</param>
        /// <returns>Percorso relativo, senza '\' iniziale</returns>
        public static string AbsToRelativePath(string abs, string root = Constants.PathClient)
        {
            return abs.Replace(root, "").Substring(1);
        }

        /// <summary>
        /// Costruisco un percorso assoluto partendo da uno relativo </summary>
        /// <param name="relative">Percorso relativo, (es dati\report.txt)'</param>
        /// <returns>Il percorso assoluto (es: C:\blabla\a.txt)</returns>
        public static string RelativeToAbsPath(string relative, string root = Constants.PathClient)
        {

            if (relative.StartsWith(Path.DirectorySeparatorChar.ToString()) == true)
                return root + relative;
            else
                return root + Path.DirectorySeparatorChar + relative;
        }

        #endregion

        /// <summary>
        /// Converne l'enum CmdType in una stringa, per debug
        /// </summary>
        public static string Cmd2String(CmdType type)
        {
            switch (type)
            {
                case CmdType.login:
                    return "Login";
                case CmdType.registration:
                    return "Registration";
                case CmdType.logout:
                    return "Logout";
                case CmdType.sendFile:
                    return "SendFile";
                case CmdType.startSynch:
                    return "";
                case CmdType.endSynch:
                    return "";
                case CmdType.ok:
                    return "";
                case CmdType.error:
                    return "";
                case CmdType.bye:
                    return "";
                default:
                    return "???";
            }
        }

        /// <summary>
        /// Converte l'enum ErrorCode in una stringa, per debug
        /// </summary>
        public static string Err2String(ErrorCode err)
        {
            switch (err)
            {
                case ErrorCode.syntaxError:
                    return "SyntaxError";
                case ErrorCode.credentialsNotValid:
                    return "CredentialsNotValid";
                case ErrorCode.argumentNotValid:
                    return "ArgumentNotValid";
                case ErrorCode.networkError:
                    return "NetworkError";
                case ErrorCode.unexpectedMessageType:
                    return "UnexpectedMessageType";
                case ErrorCode.usernameAlreadyPresent:
                    return "UsernameAlreadyPresent";
                case ErrorCode.usernameLengthNotValid:
                    return "UsernameLengthNotValid";
                case ErrorCode.passwordLengthNotValid:
                    return "PasswordLengthNotValid";
                default:
                    return "???";
            }
        }

        /// <summary>
        /// Ritorno il numero di riga che ha scatenato l'eccezione
        /// </summary>
        /// <param name="stackTrace">StackTrace dell'eccezione</param>
        public static int GetExceptionLine(string stackTrace)
        {
            int ret = 0;
            string firstLine = "";

            int endFirstLine = stackTrace.IndexOf(Environment.NewLine);

            if (endFirstLine == -1)
                firstLine = stackTrace;
            else
                firstLine = stackTrace.Substring(0, endFirstLine);

            string[] token = firstLine.Split(' ');
            string ln = token[token.Length - 1];

            if (int.TryParse(ln, out ret) == false)
            {
                ret = 0;
            }

            return ret;
        }

        /// <summary>
        /// Ritorno il primo stackframe utile (escludendo quelli di libreria)
        /// </summary>
        /// <param name="st">La Stack Trace dell'eccezione</param>
        public static StackFrame GetFirstValidFrame(StackTrace st)
        {
            StackFrame ret = null;

            for (int i = 0; i < st.FrameCount; i++)
            {
                ret = st.GetFrame(i);

                // Controllo che sia valido
                if ((ret.GetFileName() != null) && (ret.GetFileLineNumber() > 0))
                    return ret;
            }

            return ret;
        }

        /// <summary>
        /// Mando un file su un socket
        /// </summary>
        /// <param name="cl">Connessione TCP su cui trasferire il file</param>
        /// <param name="absFname">Nome del file (assoluto)</param>
        /// <param name="fileSize">Dimensione del file in byte</param>
        public static void SendFile(TcpClient cl, string absFname, Int64 fileSize)
        {
            int counter = 0, letti = 0;
            FileStream fs = new FileStream(absFname, FileMode.Open, FileAccess.ReadWrite);
            byte[] chunk = new byte[Constants.FileTransferChunkSize];
            Int32 chunkNumber = (Int32)Math.Floor((double)fileSize / 1024);
            Int32 lastChunkSize = (Int32)(fileSize % 1024);

            // Apro il file e leggo e invio chunk per chunk
            while (counter < chunkNumber)
            {
                letti = fs.Read(chunk, 0, Constants.FileTransferChunkSize);
                if (letti != Constants.FileTransferChunkSize)
                    throw new IOException("Impossibile completare lettura di un chunk");

                Utilis.SafeSocketWrite(cl.Client, chunk, Constants.FileTransferChunkSize);
                counter++;
            }

            // Leggo e invio l'ulitmo chunk (che non è necessaramente grande 1024B)
            Array.Clear(chunk, 0, Constants.FileTransferChunkSize);
            letti = fs.Read(chunk, 0, lastChunkSize);
            if (letti != lastChunkSize)
                throw new IOException("Impossibile completare lettura ultimo chunk");

            Utilis.SafeSocketWrite(cl.Client, chunk, lastChunkSize);

            return;
        }

        /// <summary>
        /// Ricevo un file da socket </summary>
        /// <param name="cl">Connessione TCP su cui trasferire il file</param>
        /// <param name="absFname">Nome del file di destinazione (assoluto)</param>
        /// <param name="fileSize">Dimensione del file in byte</param>
        public static void GetFile(TcpClient cl, string absFname, Int64 fileSize)
        {

            using (FileStream fs = new FileStream(absFname, FileMode.CreateNew, FileAccess.Write))
            {
                byte[] chunk = new byte[Constants.FileTransferChunkSize];
                Int32 chunkNumber = (Int32)Math.Floor((double)fileSize / 1024);
                Int32 lastChunkSize = (Int32)(fileSize % 1024);

                int counter = 0;
                while (counter < chunkNumber)
                {
                    Utilis.SafeSocketRead(cl.Client, chunk, Constants.FileTransferChunkSize);
                    fs.Write(chunk, 0, Constants.FileTransferChunkSize);

                    counter++;
                }
                // Leggo e invio l'ulitmo chunk (che non è necessaramente grande 1024B)
                Array.Clear(chunk, 0, chunk.Length);
                Utilis.SafeSocketRead(cl.Client, chunk, lastChunkSize);
                fs.Write(chunk, 0, lastChunkSize);

                fs.Flush();
            }

        }

        /// <summary>
        /// Invio in maniera certa un buffer su un socket</summary>
        /// <param name="s">Socket su cui spedire</param>
        /// <param name="buffer">I dati da inviare</param>
        /// <param name="size">Dimensione dei dati</param>
        /// <exception cref="SocketException">Lanciata quando c'è un errore nella funzione socket</exception>
        public static void SafeSocketWrite(Socket s, byte[] buffer, int size)
        {
            int count = 0;
            SocketError err;
            while (count < size)
            {
                // Provo a mandare i dati
                count += s.Send(buffer, count, size - count, SocketFlags.None, out err);

                // Se ho un errore lancio un'eccezione
                if (err != SocketError.Success)
                    throw new SocketException((int)err);
            }
        }

        /// <summary>
        /// Ricevo in maniera certa su un buffer un determinato numero di byte </summary>
        /// <param name="s">Socket da cui ricevere i dati</param>
        /// <param name="buffer">Buffer (preallocato) su cui salvare i dati</param>
        /// <param name="size">Quanti byte leggere</param>
        /// <exception cref="SocketException">Lanciata quando c'è un errore nella funzione socket</exception>
        public static void SafeSocketRead(Socket s, byte[] buffer, int size)
        {
            int count = 0;
            SocketError err;
            while (count < size)
            {
                // Provo a mandare i dati
                count += s.Receive(buffer, count, size - count, SocketFlags.None, out err);
                //Logger.Info("\n[SafeSocketRead-server]il contenuto del pacchetto ricevuto è: " + System.Text.Encoding.UTF8.GetString(buffer));
                // Se ho un errore lancio un'eccezione
                if (err != SocketError.Success)
                    throw new SocketException((int)err);
            }
        }


        /// <summary>
        /// invio in maniera certa un comando</summary>
        /// <exception cref="SocketException">Errore durante l'invio</exception>
        /// <param name="cl">TcpClient da cui leggere i dati</param>
        /// <param name="comando">comando da inviare</param>
        public static void SendCmdSync(TcpClient cl, Command comando)
        {

            //lenOut   = HEADER_SIZE (command + payload_length_size) + PayloadLength
            //    la dimensione del Payload sarebbe un long ma la converto ad int perchè tanto nei comandi non avrò mai
            //    Payload di 2GB              
            int lenOut = Constants.CommandTypeBytes + Constants.CommandLengthBytes + Convert.ToInt32(comando.PayloadLength);

            //Il buffer in cui vado a preparare i dati
            byte[] bufferOut = new byte[lenOut];

            //TODO ma non conviene usare la comando.ToBytes ?

            //Copio nel buffer il codice del comando
            Buffer.BlockCopy(BitConverter.GetBytes((int)comando.kmd), 0, bufferOut, 0, Constants.CommandTypeBytes);

            //Copio nel buffer la dimensione del Payload
            Buffer.BlockCopy(BitConverter.GetBytes(comando.PayloadLength), 0, bufferOut, Constants.CommandTypeBytes, Constants.CommandLengthBytes);

            //Copio nel buffer il Payload
            Buffer.BlockCopy(Encoding.UTF8.GetBytes(comando.Payload), 0, bufferOut, Constants.CommandTypeBytes + Constants.CommandLengthBytes, Convert.ToInt32(comando.PayloadLength));

            //Mando il pacchetto
            SafeSocketWrite(cl.Client, bufferOut, lenOut);

        }

        /// <summary>
        /// ricevo in maniera certa un comando</summary>
        /// <exception cref="SocketException">Errore durante l'invio</exception>
        /// <param name="cl">TcpClient da cui leggere i dati</param>
        public static Command GetCmdSync(TcpClient cl)
        {
            Command ret = new Command();
            byte[] buffer = new byte[Constants.HeaderSize];
            Int32 p_len = 0;
            try
            {
                // Leggo il tipo di comando
                SafeSocketRead(cl.Client, buffer, Constants.CommandTypeBytes);
                ret.kmd = (CmdType)BitConverter.ToInt32(buffer, 0);

                // Leggo il Payload length
                SafeSocketRead(cl.Client, buffer, Constants.CommandLengthBytes);

                p_len = BitConverter.ToInt32(buffer, 0);

                // Prendo il Payload
                byte[] payload = new byte[p_len];
                SafeSocketRead(cl.Client, payload, p_len);

                ret.Payload = Encoding.UTF8.GetString(payload, 0, p_len);

                return ret;
            }
            catch (Exception e) //TODO ACCROCCHIO TERIBBILE
            {
                Logger.log("Errore ricezione comando: " + e.Message);
                return null;
            }
        }



    }


    public class MyException : Exception
    {
        private string _fileName;
        private int _lineNumber;

        public string FileName
        {
            get
            {
                return _fileName;
            }

            set
            {
                _fileName = value;
            }
        }

        public int LineNumber
        {
            get
            {
                return _lineNumber;
            }

            set
            {
                _lineNumber = value;
            }
        }

        //public int LineNumber { get; set; }
        //public string MethodName { get; set; }

        public MyException()
        {
        }

        public MyException(string message, string fileName, int lineNumber)
            : base(message)
        {
            this.LineNumber = lineNumber;
            this._fileName = fileName;
        }

        public MyException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }
}
