using System;
using System.IO;

namespace Server
{
    /// <summary>
    /// Classe usata come valore del dizionario dei file
    /// </summary>
    class FileAttributeHelper
    {
        public string AbsFilePath;
        public string RelFilePath;

        public DateTime LastModtime;
        public long Size;

        private string _md5;
        private Boolean md5Calculated;

        public string Md5
        {
            get
            {
                if (!md5Calculated)
                    this.calculateMd5();

                return _md5;
                
            }

            private set { }
        }


        /// <summary>
        /// Crea un istanza della classe andando a recuperare i metadati del file passato come parametro
        /// </summary>
        /// <param name="absFilePath"></param>
        public FileAttributeHelper(string absFilePath)
        {
            if (File.Exists(absFilePath))
            {
                this.AbsFilePath = absFilePath;
                this.RelFilePath = Utilis.AbsToRelativePath(absFilePath);

                this.LastModtime = File.GetLastWriteTime(absFilePath);
                this.Size = new FileInfo(absFilePath).Length;
                this.md5Calculated = false;
            }
            else
                throw new FileNotFoundException("Il file non esiste");
        }

        public override bool Equals(System.Object obj)
        {
            if (obj == null)
            {
                return false;
            }

            // Se non posso castarlo ritorno falso
            FileAttributeHelper p = obj as FileAttributeHelper;
            if ((System.Object)p == null)
            {
                return false;
            }

            // Confronto i campi
            if (Size == p.Size && LastModtime.Equals(p.LastModtime))
            {
                return true;
            }
            else
                return false;
        }

        private void calculateMd5()
        {
            try
            {
                this._md5 = Utilis.MD5sum(this.AbsFilePath);
                this.md5Calculated = true;
            }
            catch(Exception e)
            {
                Logger.Error("FileAttributeHelper.calculateMd5(" + this.AbsFilePath + ") " + e.Message );
            }
            
        }

        public override int GetHashCode()
        {
            return base.GetHashCode() ^ Convert.ToInt32(Size) ^ LastModtime.GetHashCode();
        }

    }
}
