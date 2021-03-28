﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Globalization;
using static upatcher;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

namespace UniversalPatcher
{
    public class XDF
    {
        public XDF()
        {

        }
        private PcmFile PCM;
        private List<TableData> tdList;
        private string convertMath(string math)
        {
            string retVal = math.ToLower();

            if (retVal.StartsWith("x+"))
                retVal = retVal.Replace("x+", "x-");
            else if (retVal.StartsWith("x-"))
                retVal = retVal.Replace("x-", "x+");
            else if (retVal.StartsWith("x*"))
                retVal = retVal.Replace("x*", "x/");
            else if (retVal.StartsWith("x/"))
                retVal = retVal.Replace("x/", "x*");
            else
                retVal = "X";

            return retVal;
        }
        private string ConvertXdf(XDocument doc)
        {
            string retVal = "";
            try
            {
                List<string> categories = new List<string>();

                foreach (XElement element in doc.Elements("XDFFORMAT").Elements("XDFHEADER"))
                {
                    foreach (XElement cat in element.Elements("CATEGORY"))
                    {
                        string category = cat.Attribute("name").Value;
                        categories.Add(category);
                        if (!PCM.tableCategories.Contains(category))
                            PCM.tableCategories.Add(category);
                    }
                }
                foreach (XElement element in doc.Elements("XDFFORMAT").Elements("XDFTABLE"))
                {
                    TableData xdf = new TableData();
                    xdf.OS = PCM.OS;
                    xdf.Origin = "xdf";
                    string RowHeaders = "";
                    string ColHeaders = "";
                    string addr = "";
                    string size = "";
                    string math = "";
                    int elementSize = 0;
                    bool Signed = false;
                    bool Floating = false;

                    List<string> multiMath = new List<string>();
                    List<string> multiAddr = new List<string>();

                    foreach (XElement axle in element.Elements("XDFAXIS"))
                    {
                        if (axle.Attribute("id").Value == "x")
                        {
                            xdf.Columns = Convert.ToUInt16(axle.Element("indexcount").Value);
                            foreach (XElement lbl in axle.Elements("LABEL"))
                            {
                                ColHeaders += lbl.Attribute("value").Value + ",";
                            }
                            ColHeaders = ColHeaders.Trim(',');
                            xdf.ColumnHeaders = ColHeaders;
                        }
                        if (axle.Attribute("id").Value == "y")
                        {
                            xdf.Rows = Convert.ToUInt16(axle.Element("indexcount").Value);
                            foreach (XElement lbl in axle.Elements("LABEL"))
                            {
                                RowHeaders += lbl.Attribute("value").Value + ",";
                            }
                            RowHeaders = RowHeaders.Trim(',');
                            xdf.RowHeaders = RowHeaders;
                        }
                        if (axle.Attribute("id").Value == "z")
                        {
                            addr = axle.Element("EMBEDDEDDATA").Attribute("mmedaddress").Value.Trim();
                            xdf.addrInt = Convert.ToUInt32(addr, 16);
                            //xdf.Address = addr;
                            string tmp = axle.Element("EMBEDDEDDATA").Attribute("mmedelementsizebits").Value.Trim();
                            size = (Convert.ToInt32(tmp) / 8).ToString();
                            elementSize = (byte)(Convert.ToInt32(tmp) / 8);
                            math = axle.Element("MATH").Attribute("equation").Value.Trim().Replace("*.", "*0.");
                            xdf.Math = math.Replace("/.", "/0.");
                            xdf.SavingMath = convertMath(xdf.Math);
                            xdf.Decimals = Convert.ToUInt16(axle.Element("decimalpl").Value);
                            if (axle.Element("outputtype") != null)
                                xdf.OutputType = (OutDataType) Convert.ToUInt16(axle.Element("outputtype").Value);
                            foreach (XElement rowMath in axle.Elements("MATH"))
                            {
                                if (rowMath.Element("VAR").Attribute("address") != null)
                                {
                                    //Table have different address for every (?) row
                                    Debug.WriteLine(rowMath.Element("VAR").Attribute("address").Value);
                                    Debug.WriteLine(rowMath.Attribute("equation").Value);
                                    multiAddr.Add(rowMath.Element("VAR").Attribute("address").Value);
                                    multiMath.Add(rowMath.Attribute("equation").Value);
                                }
                            }
                        }
                    }
                    xdf.TableName = element.Element("title").Value;
                    if (element.Element("units") != null)
                        xdf.Units = element.Element("units").Value;
                    /*if (element.Element("datatype") != null)
                        xdf.DataType = Convert.ToByte(element.Element("datatype").Value);*/
                    if (element.Element("EMBEDDEDDATA") != null && element.Element("EMBEDDEDDATA").Attribute("mmedtypeflags") != null)
                    {
                        byte flags = Convert.ToByte(element.Element("EMBEDDEDDATA").Attribute("mmedtypeflags").Value,16);
                        Signed = Convert.ToBoolean(flags & 1);
                        if ((flags & 0x10000) == 0x10000)
                            Floating = true;
                        else
                            Floating = false;
                        if ((flags & 4) == 4)
                            xdf.RowMajor = true;
                        else
                            xdf.RowMajor = false;
                    }
                    if (element.Element("CATEGORYMEM") != null && element.Element("CATEGORYMEM").Attribute("category") != null)
                    {
                        int catid = Convert.ToInt16(element.Element("CATEGORYMEM").Attribute("category").Value);
                        xdf.Category = categories[catid - 1];
                    }
                    if (element.Element("description") != null)
                        xdf.TableDescription = element.Element("description").Value;
                    xdf.DataType = convertToDataType(elementSize, Signed, Floating);

                    if (multiMath.Count > 0 && xdf.Rows == multiMath.Count)
                    {
                        string[] rowHdr = xdf.RowHeaders.Replace(".","").Split(',');
                        for (int m=0; m< multiMath.Count; m++)
                        {
                            TableData tdNew = new TableData();
                            tdNew = xdf.ShallowCopy();
                            tdNew.Rows = 1; //Convert to single-row, multitable
                            tdNew.Math = multiMath[m];
                            tdNew.Address = multiAddr[m];
                            if (rowHdr.Length >= m - 1)
                                tdNew.TableName += "." + rowHdr[m];
                            else
                                tdNew.TableName += "." + m.ToString();
                            tdList.Add(tdNew);
                        }
                    }
                    else
                    {
                        tdList.Add(xdf);
                    }
                }
                foreach (XElement element in doc.Elements("XDFFORMAT").Elements("XDFCONSTANT"))
                {
                    TableData xdf = new TableData();
                    xdf.OS = PCM.OS;
                    xdf.Origin = "xdf";
                    int elementSize = 0;
                    bool Signed = false;
                    bool Floating = false;
                    if (element.Element("EMBEDDEDDATA").Attribute("mmedaddress") != null)
                    {
                        xdf.TableName = element.Element("title").Value;
                        //xdf.AddrInt = Convert.ToUInt32(element.Element("EMBEDDEDDATA").Attribute("mmedaddress").Value.Trim(), 16);
                        xdf.Address = element.Element("EMBEDDEDDATA").Attribute("mmedaddress").Value.Trim();
                        elementSize = (byte)(Convert.ToInt32(element.Element("EMBEDDEDDATA").Attribute("mmedelementsizebits").Value.Trim()) / 8);
                        xdf.Math = element.Element("MATH").Attribute("equation").Value.Trim().Replace("*.", "*0.").Replace("/.", "/0.");
                        xdf.SavingMath = convertMath(xdf.Math);
                        if (element.Element("units") != null)
                            xdf.Units = element.Element("units").Value;
                        if (element.Element("EMBEDDEDDATA").Attribute("mmedtypeflags") != null)
                        {
                            byte flags = Convert.ToByte(element.Element("EMBEDDEDDATA").Attribute("mmedtypeflags").Value,16);
                            Signed = Convert.ToBoolean(flags & 1);
                            if ((flags & 0x10000) == 0x10000)
                                Floating = true;
                            else
                                Floating = false;
                        }
                        xdf.Columns = 1;
                        xdf.Rows = 1;
                        xdf.RowMajor = false;
                        if (element.Element("CATEGORYMEM") != null && element.Element("CATEGORYMEM").Attribute("category") != null)
                        {
                            int catid = Convert.ToInt16(element.Element("CATEGORYMEM").Attribute("category").Value);
                            xdf.Category = categories[catid - 1];
                        }
                        if (element.Element("description") != null)
                            xdf.TableDescription = element.Element("description").Value;
                        xdf.DataType = convertToDataType(elementSize, Signed, Floating);

                        tdList.Add(xdf);

                    }
                }
                foreach (XElement element in doc.Elements("XDFFORMAT").Elements("XDFFLAG"))
                {
                    TableData xdf = new TableData();
                    xdf.OS = PCM.OS;
                    xdf.Origin = "xdf";
                    if (element.Element("EMBEDDEDDATA").Attribute("mmedaddress") != null)
                    {
                        xdf.TableName = element.Element("title").Value;
                        //xdf.AddrInt = Convert.ToUInt32(element.Element("EMBEDDEDDATA").Attribute("mmedaddress").Value.Trim(), 16);
                        xdf.Address = element.Element("EMBEDDEDDATA").Attribute("mmedaddress").Value.Trim();
                        int elementSize = (byte)(Convert.ToInt32(element.Element("EMBEDDEDDATA").Attribute("mmedelementsizebits").Value.Trim()) / 8);
                        xdf.Math = "X";
                        xdf.BitMask = element.Element("mask").Value;
                        xdf.OutputType = OutDataType.Flag;
                        xdf.Columns = 1;
                        xdf.Rows = 1;
                        xdf.RowMajor = false;
                        if (element.Element("CATEGORYMEM") != null && element.Element("CATEGORYMEM").Attribute("category") != null)
                        {
                            int catid = Convert.ToInt16(element.Element("CATEGORYMEM").Attribute("category").Value);
                            xdf.Category = categories[catid - 1];
                        }
                        if (element.Element("description") != null)
                            xdf.TableDescription = element.Element("description").Value;
                        xdf.DataType = convertToDataType(elementSize, false, false);
                        tdList.Add(xdf);

                    }
                }
            }
            catch (Exception ex)
            {
                var st = new StackTrace(ex, true);
                // Get the top stack frame
                var frame = st.GetFrame(st.FrameCount - 1);
                // Get the line number from the stack frame
                var line = frame.GetFileLineNumber();
                retVal +="XdfImport, line " + line + ": " + ex.Message + Environment.NewLine;
            }
            return retVal;

        }

        public string  importXdf(PcmFile PCM1, List<TableData> tdList1)
        {
            string retVal = "";
            try
            {
                PCM = PCM1;
                tdList = tdList1;
                XDocument doc;
                string fname = SelectFile("Select XDF file", "xdf files (*.xdf)|*.xdf|ALL files (*.*| *.*");
                if (fname.Length == 0)
                    return retVal;
                retVal = "Importing file " + fname + "...";

                doc = new XDocument(new XComment(" Written " + DateTime.Now.ToString("G", DateTimeFormatInfo.InvariantInfo)), XElement.Load(fname));
                retVal += ConvertXdf(doc);
                retVal += "Done" + Environment.NewLine;
            }
            catch (Exception ex)
            {
                var st = new StackTrace(ex, true);
                // Get the top stack frame
                var frame = st.GetFrame(st.FrameCount - 1);
                // Get the line number from the stack frame
                var line = frame.GetFileLineNumber();
                retVal += "XdfImport, line " + line + ": " + ex.Message + Environment.NewLine;
            }
            return retVal;
        }

        public string exportXdf(PcmFile basefile, List<TableData> tdList1)
        {
            PCM = basefile;
            tdList = tdList1;
            string retVal = "";
            string tableRows = "";
            String tableText = "";
            string templateTxt = "";
            int lastCategory = 0;
            int dtcCategory = 0;
            int uniqId = 0x100;
            try
            {

                string fName = Path.Combine(Application.StartupPath, "Templates", "xdfheader.txt");
                string xdfText = ReadTextFile(fName);
                xdfText = xdfText.Replace("REPLACE-TIMESTAMP", DateTime.Today.ToString("MM/dd/yyyy H:mm"));
                xdfText = xdfText.Replace("REPLACE-OSID", basefile.OS);
                xdfText = xdfText.Replace("REPLACE-BINSIZE", basefile.fsize.ToString("X"));
                for (int s = 1; s < basefile.tableCategories.Count; s++)
                {
                    tableText += "     <CATEGORY index = \"0x" + (s - 1).ToString("X") + "\" name = \"" + basefile.tableCategories[s] + "\" />" + Environment.NewLine;
                    lastCategory = s;
                }
                dtcCategory = lastCategory + 1;
                tableText += "     <CATEGORY index = \"0x" + (dtcCategory - 1).ToString("X") + "\" name = \"DTC\" />" + Environment.NewLine;
                lastCategory = dtcCategory + 1;
                tableText += "     <CATEGORY index = \"0x" + (lastCategory - 1).ToString("X") + "\" name = \"Other\" />";
                xdfText = xdfText.Replace("REPLACE-CATEGORYNAME", tableText) + Environment.NewLine;

                fName = Path.Combine(Application.StartupPath, "Templates", basefile.configFile + "-checksum.txt");
                xdfText += ReadTextFile(fName);


                //Add OS ID:
                fName = Path.Combine(Application.StartupPath, "Templates", "xdfconstant.txt");
                templateTxt = ReadTextFile(fName);
                tableText = templateTxt.Replace("REPLACE-TABLEID", uniqId.ToString("X"));
                uniqId++;
                tableText = tableText.Replace("REPLACE-TABLEDESCRIPTION", "DON&apos;T MODIFY");
                tableText = tableText.Replace("REPLACE-TABLETITLE", "OS ID - Don&apos;t modify, must match XDF!");
                tableText = tableText.Replace("REPLACE-BITS", "32");
                tableText = tableText.Replace("REPLACE-MINVALUE", basefile.OS);
                tableText = tableText.Replace("REPLACE-MAXVALUE", basefile.OS);
                tableText = tableText.Replace("REPLACE-TABLEADDRESS", basefile.segmentAddressDatas[basefile.OSSegment].PNaddr.Address.ToString("X"));
                tableText = tableText.Replace("REPLACE-CATEGORY", (basefile.OSSegment + 1).ToString("X"));
                xdfText += tableText;

                fName = Path.Combine(Application.StartupPath, "Templates", "xdftableseek.txt");
                templateTxt = ReadTextFile(fName);
                for (int t = 0; t < tdList.Count; t++)
                {
                    //Add all tables
                    if (tdList[t].Rows > 1)
                    {
                        if (tdList[t].TableName != null && tdList[t].TableName.Length > 1)
                            tableText = templateTxt.Replace("REPLACE-TABLETITLE", tdList[t].TableName);
                        else
                            tableText = templateTxt.Replace("REPLACE-TABLETITLE", tdList[t].Address);
                        int s = basefile.GetSegmentNumber(tdList[t].addrInt);
                        if (s == -1) s = lastCategory;
                        if (tdList[t].Category != null && tdList[t].Category != "")
                        {
                            for (int c = 1; c < PCM.tableCategories.Count; c++)
                            {
                                if (PCM.tableCategories[c] == tdList[t].Category)
                                {
                                    tableText = tableText.Replace("REPLACE-CATEGORY", c.ToString());
                                    break;
                                }
                            }
                        }
                        else
                        {
                            tableText = tableText.Replace("REPLACE-CATEGORY", s.ToString());
                        }
                        string descr = tdList[t].TableDescription;
                        /*if (tdList[t].Values.StartsWith("Enum: ") && !descr.ToLower().Contains("enum"))
                        {
                            string[] hParts = tdList[t].Values.Substring(6).Split(',');
                            for (int x = 0; x < hParts.Length; x++)
                            {
                                descr += Environment.NewLine + hParts[x];
                            }
                        }*/

                        tableText = tableText.Replace("REPLACE-TABLEID", uniqId.ToString("X"));
                        uniqId++;
                        tableText = tableText.Replace("REPLACE-UNITS", tdList[t].Units);
                        tableText = tableText.Replace("REPLACE-ROWCOUNT", tdList[t].Rows.ToString());
                        tableText = tableText.Replace("REPLACE-COLCOUNT", tdList[t].Columns.ToString());
                        tableText = tableText.Replace("REPLACE-MATH", tdList[t].Math);
                        tableText = tableText.Replace("REPLACE-BITS", getBits(tdList[t].DataType).ToString());
                        tableText = tableText.Replace("REPLACE-DECIMALS", tdList[t].Decimals.ToString());
                        tableText = tableText.Replace("REPLACE-OUTPUTTYPE", ((ushort)tdList[t].OutputType).ToString());
                        tableText = tableText.Replace("REPLACE-TABLEADDRESS",((uint)(tdList[t].addrInt + tdList[t].Offset)).ToString("X"));
                        tableText = tableText.Replace("REPLACE-TABLEDESCRIPTION", descr);
                        tableText = tableText.Replace("REPLACE-MINVALUE", tdList[t].Min.ToString());
                        tableText = tableText.Replace("REPLACE-MAXVALUE", tdList[t].Max.ToString());
                        int tableFlags = 0;
                        if (getSigned(tdList[t].DataType))
                        {
                            tableFlags++;
                        }
                        if (tdList[t].RowMajor == false)
                        {
                            tableFlags += 4;
                        }
                        tableText = tableText.Replace("REPLACE-TYPEFLAGS", tableFlags.ToString("X2"));

                        tableRows = "";
                        if (tdList[t].RowHeaders == "")
                        {
                            for (int d = 0; d < tdList[t].Rows; d++)
                            {
                                tableRows += "     <LABEL index=\"" + d.ToString() + "\" value=\"" + d.ToString() + "\" />";
                                if (d < tdList[t].Rows - 1)
                                    tableRows += Environment.NewLine;
                            }
                        }
                        else
                        {
                            string[] hParts = tdList[t].RowHeaders.Split(',');
                            for (int row = 0; row < hParts.Length; row++)
                            {
                                tableRows += "     <LABEL index=\"" + row.ToString() + "\" value=\"" + hParts[row] + "\" />";
                                if (row < hParts.Length - 1)
                                    tableRows += Environment.NewLine;
                            }
                        }
                        tableText = tableText.Replace("REPLACE-TABLEROWS", tableRows);
                        string tableCols = "";
                        if (tdList[t].ColumnHeaders == "")
                        {
                            for (int d = 0; d < tdList[t].Columns; d++)
                            {
                                tableCols += "     <LABEL index=\"" + d.ToString() + "\" value=\"" + d.ToString() + "\" />";
                                if (d < tdList[t].Columns - 1)
                                    tableCols += Environment.NewLine;
                            }
                        }                        
                        else
                        {
                            string[] hParts = tdList[t].ColumnHeaders.Split(',');
                            for (int col = 0; col < hParts.Length; col++)
                            {
                                tableCols += "     <LABEL index=\"" + col.ToString() + "\" value=\"" + hParts[col] + "\" />";
                                if (col < hParts.Length - 1)
                                    tableCols += Environment.NewLine;
                            }
                        }
                        tableText = tableText.Replace("REPLACE-TABLECOLS", tableCols);

                        xdfText += tableText;       //Add generated table to end of xdfText
                    }
                }

                fName = Path.Combine(Application.StartupPath, "Templates", "xdfconstant.txt");
                templateTxt = ReadTextFile(fName);
                for (int t = 0; t < tdList.Count; t++)
                {
                    //Add all constants
                    if (tdList[t].Rows < 2 && tdList[t].OutputType != OutDataType.Flag)
                    {
                        if (tdList[t].TableName != null && tdList[t].TableName.Length > 1)
                            tableText = templateTxt.Replace("REPLACE-TABLETITLE", tdList[t].TableName);
                        else
                            tableText = templateTxt.Replace("REPLACE-TABLETITLE", tdList[t].Address);
                        int s = basefile.GetSegmentNumber(tdList[t].addrInt);
                        if (s == -1) s = lastCategory;
                        tableText = tableText.Replace("REPLACE-CATEGORY", (s + 1).ToString("X"));
                        tableText = tableText.Replace("REPLACE-TABLEID", uniqId.ToString("X"));
                        uniqId++;
                        tableText = tableText.Replace("REPLACE-TABLEADDRESS", ((uint)(tdList[t].addrInt + tdList[t].Offset)).ToString("X"));
                        tableText = tableText.Replace("REPLACE-TABLEDESCRIPTION", "");
                        tableText = tableText.Replace("REPLACE-BITS", getBits(tdList[t].DataType).ToString());
                        tableText = tableText.Replace("REPLACE-MINVALUE", tdList[t].Min.ToString());
                        tableText = tableText.Replace("REPLACE-MAXVALUE", tdList[t].Max.ToString());
                        xdfText += tableText;       //Add generated table to end of xdfText
                    }
                }

                fName = Path.Combine(Application.StartupPath, "Templates", "xdfFlag.txt");
                templateTxt = ReadTextFile(fName);
                for (int t = 0; t < tdList.Count; t++)
                {
                    //Add all constants
                    if (tdList[t].OutputType == OutDataType.Flag)
                    {
                        if (tdList[t].TableName != null && tdList[t].TableName.Length > 1)
                            tableText = templateTxt.Replace("REPLACE-TABLETITLE", tdList[t].TableName);
                        else
                            tableText = templateTxt.Replace("REPLACE-TABLETITLE", tdList[t].Address);
                        int s = basefile.GetSegmentNumber(tdList[t].addrInt);
                        if (s == -1) s = lastCategory;
                        tableText = tableText.Replace("REPLACE-CATEGORY", (s + 1).ToString("X"));
                        tableText = tableText.Replace("REPLACE-TABLEID", uniqId.ToString("X"));
                        uniqId++;
                        tableText = tableText.Replace("REPLACE-TABLEADDRESS", ((uint)(tdList[t].addrInt + tdList[t].Offset)).ToString("X"));
                        tableText = tableText.Replace("REPLACE-TABLEDESCRIPTION", "");
                        tableText = tableText.Replace("REPLACE-BITS", getBits(tdList[t].DataType).ToString());
                        tableText = tableText.Replace("REPLACE-MINVALUE", tdList[t].Min.ToString());
                        tableText = tableText.Replace("REPLACE-MAXVALUE", tdList[t].Max.ToString());
                        tableText = tableText.Replace("REPLACE-MASK", tdList[t].BitMask);
                        xdfText += tableText;       //Add generated table to end of xdfText

                    }
                }

                xdfText += "</XDFFORMAT>" + Environment.NewLine;
                string defFname = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Tunerpro Files", "Bin Definitions", basefile.OS + "-generated.xdf");
                string fileName = SelectSaveFile("XDF Files(*.xdf)|*.xdf|ALL Files (*.*)|*.*",defFname);
                if (fileName.Length == 0)
                    return "";
                retVal += "Writing to file: " + Path.GetFileName(fileName);
                WriteTextFile(fileName, xdfText);
                retVal += " [OK]";

            }
            catch (Exception ex)
            {
                var st = new StackTrace(ex, true);
                // Get the top stack frame
                var frame = st.GetFrame(st.FrameCount - 1);
                // Get the line number from the stack frame
                var line = frame.GetFileLineNumber();
                retVal += ("Export XDF, line " + line + ": " + ex.Message);
            }
            return retVal;
        }
    
    }
}
