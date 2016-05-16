using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;


namespace Server
{
    class XmlManager
    {
        #region NOMI DEI CAMPI
        // TODO: mettere per ogni valore il commento ///
        public static readonly string DirectoryElementName = "dir";
        public static readonly string DirectoryAttributeName = "name";

        public static readonly string FileElementName = "file";
        public static readonly string FileAttributeName = "name";
        public static readonly string FileAttributeLastModTime = "modTime";
        public static readonly string FileAttributeSize = "dim";
        public static readonly string FileAttributeChecksum = "md5";

        #endregion

        private XDocument _xmlDoc;

        //Carica l'xml da un file
        public XmlManager(string uri)
        {
            this._xmlDoc = XDocument.Load(uri);
        }

        //Genera l'xml corrispondente a quella cartella
        public XmlManager(DirectoryInfo _dir)
        {
            this._xmlDoc = new XDocument(CreateFileSystemXmlTree(_dir));
            //SaveToFile(@"C:\Users\Utente\Desktop\out.xml");
        }


        private XElement CreateFileSystemXmlTree(DirectoryInfo di)
        {
            return new XElement(DirectoryElementName, new XAttribute(DirectoryAttributeName, di.Name),
                from file in di.GetFiles()
                select new XElement(FileElementName, new XAttribute(FileAttributeName, file.Name), new XAttribute(FileAttributeLastModTime, file.LastWriteTime.ToString(Constants.XmlDateFormat)), new XAttribute(FileAttributeSize, file.Length), new XAttribute(FileAttributeChecksum, Utilis.MD5sum(file))),
                from d in di.GetDirectories()
                select CreateFileSystemXmlTree(d)
            );
        }

        #region Modifiche da FSW

        /// <summary>
        /// Rinomina l'entry di una directory nell'xml 
        /// </summary>
        /// <param name="oldRelPath">Vecchio percorso</param>
        /// <param name="newRelPath">Nuovo Percorso</param>
        public void RenameDirectory(string oldRelPath, string newRelPath)
        {
            // Nuovo nome della cartella
            string newName = newRelPath.Split(Constants.PathSeparator.ToCharArray(), StringSplitOptions.RemoveEmptyEntries).Last();

            XElement dirElement = this.getDirectoryElement(oldRelPath);

            Logger.log("vado a rinominare " + dirElement.Attribute(DirectoryAttributeName));
            // Modifico il nome dell'attributo
            dirElement.Attribute(DirectoryAttributeName).Value = newName;
        }

        /// <summary>
        /// Rinomina l'entry di un file nell'xml 
        /// </summary>
        /// <param name="oldRelPath">Vecchio percorso</param>
        /// <param name="newRelPath">Nuovo Percorso</param>
        public void RenameFile(string oldRelPath, string newRelPath)
        {
            // Nome del nuovo file
            string newName = newRelPath.Split(Constants.PathSeparator.ToCharArray(), StringSplitOptions.RemoveEmptyEntries).Last();

            XElement fileElement = this.getFileElement(oldRelPath);

            Logger.log("vado a rinominare " + fileElement.Attribute(FileAttributeName));
            // Modifico il nome dell'attributo
            fileElement.Attribute(FileAttributeName).Value = newName;
        }

        /// <summary>
        /// Aggiorno l'entry di un file ricalcolandone l'md5 e data di ultima modifica
        /// </summary>
        /// <param name="fa">Classe contenente gli attributi del file</param>
        public void RefreshFile(FileAttributeHelper fa)
        {
            //FileInfo fInfo = new FileInfo(Utilis.RelativeToAbsPath(relPath));
            XElement fileElement = this.getFileElement(fa.RelFilePath);

            // Modifico gli attributi
            fileElement.Attribute(FileAttributeSize).Value = fa.Size.ToString();
            fileElement.Attribute(FileAttributeLastModTime).Value = fa.LastModtime.ToString();

        }

        #endregion


        #region Ricerca Elementi
        /// <summary>
        /// Cerco all'interno del documento xml un elemento di tipo directory </summary>
        /// <param name="relPath">Il percorso RELATIVO alla directory per cui è stato generato il documento della directory da cercare</param>
        /// <returns></returns>
        private XElement getDirectoryElement(string relPath)
        {
            // Estraggo gli elementi del percorso per scendere nell'albero dell'xml
            string[] pathElement = relPath.Split(Constants.PathSeparator.ToCharArray(), StringSplitOptions.RemoveEmptyEntries);

            XElement ret = this.GetRoot();

            // prendo il FIRST dell'array ritornato dalla where perchè il filesystem mi garantisce l'univocità dei nomi
            // degli oggetti (directory o file) presenti in una directory
            foreach (string elem in pathElement)
                ret = ret.Elements(DirectoryElementName).Where(elt => elt.Attribute(DirectoryAttributeName).Value == elem).First();

            return ret;
        }


        /// <summary>
        /// Cerca nel documento corrente l'elemento corrispondente al file il cui percorso è passato come parametro </summary>
        /// <param name="relPath">Percorso RELATIVO alla cartella per cui è stato generato il documento</param>
        /// <returns>L'elemento XML corrispondente</returns>
        private XElement getFileElement(string relPath)
        {
            XElement ret = null;
            XElement dir = this.getDirectoryElement(Path.GetDirectoryName(relPath));

            ret = dir.Elements(FileElementName).Where(elt => elt.Attribute(FileAttributeName).Value == Path.GetFileName(relPath)).First();

            return ret;
        }
        #endregion


        /// <summary>
        /// controlla due XML rendendo l'elenco dei files che sono stati aggiunti/modificati
        /// l'elenco viene memorizzato in res (stringa che deve essere passata vuota)
        /// </summary>
        /// <param name="rc">XElement del client </param>
        /// <param name="rs">XElement del server </param>
        /// <param name="res">stringa (inizializzata vuota) che memorizza l'elenco dei path dei file diversi</param>
        /// <param name="path">stringa per memorizzare il path di ciascun file differente</param>
        public static int checkDiff(XElement rc, XElement rs, List<String> refList, ref string res, string path)
        {
            int counter = 0;
            bool presente = false;
            bool uguali = false;
            bool cartellaesistente = false;


            var filesC = from e in rc.Elements(FileElementName)
                         select e;

            var filesS = from eS in rs.Elements(FileElementName)
                         select eS;

            var subdirsC = from cc in rc.Elements(DirectoryElementName)
                           select cc;

            var subdirsS = from cs in rs.Elements(DirectoryElementName)
                           select cs;


            foreach (XElement e in filesC)
            {
                ///cerco se e (cioè il file i-esimo) sia presente nella cartella considerata
                presente = false;
                uguali = false;
                foreach (XElement eS in filesS)
                {
                    if (e.Attribute("name").ToString().CompareTo(eS.Attribute("name").ToString()) == 0)
                    {
                        //nome presente
                        if (e.Attribute("md5").ToString().CompareTo(eS.Attribute("md5").ToString()) == 0)
                        {
                            //file identico in nome e in checksum
                            uguali = true;
                        }
                        presente = true;
                    }
                }

                if (presente == false)
                {
                    counter++;
                    res += "PUSH" + path + "\\" + e.Attribute("name").Value + "\n";
                    refList.Add(path + "\\" + e.Attribute("name").Value);
                }
                else if (uguali == false)
                {
                    counter++;
                    res += "PUSH* " + path + "\\" + e.Attribute("name").Value + "\n";
                    refList.Add(path + "\\" + e.Attribute("name").Value);
                }
            }

            ///chiamo ricorsivamente la funzione per ogni sottocartella che sia presente anche lato server
            foreach (XElement cc in subdirsC)
            {
                cartellaesistente = false;
                foreach (XElement cs in subdirsS)
                {
                    if (cc.Attribute("name").ToString().CompareTo(cs.Attribute("name").ToString()) == 0)
                    {
                        //cartella esistente, ricorro
                        cartellaesistente = true;
                        counter += checkDiff(cc, cs, refList, ref res, path + "\\" + cc.Attribute("name").Value);
                    }
                }
                if (cartellaesistente == false)
                {
                    //cartella inesistente, stampo e NON RICORRO
                    //res += ("cartella NUOVA " + path + "\\" + cc.Attribute("name").Value + "\n");

                    //TO DO cercare una funzione che renda il path completo dato un XElement, metterla come stringa di partenza al posto di path
                    counter += getSubdirs(cc, refList, ref res, path + "\\" + cc.Attribute("name").Value);
                }
            }

            return counter;
        }

        /// <summary>
        /// funzione che elenca ricorsivamente tutti i files e tutte le cartelle presenti 
        /// all'interno della cartella interessata (root)
        /// </summary>
        /// <param name="root">XElement cartella interessata</param>
        /// <param name="subElements">stringa che memorizza l'elenco dei path dei file diversi</param>
        /// <param name="path">path temporaneo</param>
        public static int getSubdirs(XElement root, List<String> refList, ref string subElements, string path)
        {
            int counter = 0;
            var files = from f in root.Elements(FileElementName)
                        select f;
            var dirs = from d in root.Elements(DirectoryElementName)
                       select d;

            foreach (XElement e in files)
            {
                subElements += "PUSH" + path + "\\" + e.Attribute("name").Value + "\n";
                refList.Add(path + "\\" + e.Attribute("name").Value);
                counter++;
            }
            foreach (XElement e in dirs)
            {
                //subElements += path + "\\" + e.Attribute("name").Value + "\n";
                counter += getSubdirs(e, refList, ref subElements, path + "\\" + e.Attribute("name").Value);
            }

            return counter;
        }

        /// <summary>
        /// Ritorna l'md5 dell'XML NORMALIZZATO</summary>
        public string XMLDigest()
        {
            // "Normalizzo" l'xml andando a cancellare l'attributo della root
            XDocument doc = new XDocument(this._xmlDoc);
            XElement root = XmlManager.GetRoot(doc);

            root.Attribute(XmlManager.DirectoryAttributeName).Value = "";

            return Utilis.Md5String(doc.ToString());
        }

        public override string ToString()
        {
            return _xmlDoc.ToString();
        }

        public XDocument getXDocument()
        {
            return _xmlDoc;
        }

        public XElement GetRoot()
        {
            return _xmlDoc.Element(DirectoryElementName);
        }

        public static XElement GetRoot(XDocument xDoc)
        {
            return xDoc.Root;
        }

        public void SaveToFile(string path)
        {
            StreamWriter output = new StreamWriter(path);

            output.Write(this.ToString());

            output.Flush();
            output.Close();
        }

    }
}
