using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.VisualBasic.FileIO;

namespace SplitSkus
{
    internal class Split
    {
        private const string CsvFilepath = @"E:\Development\CSV-Parser\CSV-Data\";
        private const string AppDataFilepath = @"E:\Development\CSV-Parser\Split-Skus\AppData\";
        private const string Source = "Wayfair";

        private static void Main()
        {
            // Get the input CSV file
            var parser = GetParser(CsvFilepath + Source + @"\raw.csv");

            var inputTable = new DataTable();
            bool isHeader = true;

            // Read the input file into a datatable, setting the first row to be the header
            while (!parser.EndOfData)
            {
                // Try to read the next row
                string[] inputRows;
                try
                {
                    inputRows = parser.ReadFields();
                }
                catch
                {
                    continue;
                }

                // If we didn't get any data move on to the next row
                if (inputRows == null) continue;

                int colIdx = 0;
                if (isHeader)
                {
                    // If there is just one column but it contains tabs then split on the tabs
                    // (this can happen if you cut-and-paste from SSMS)
                    if (inputRows.Length == 1 && inputRows[0].Contains("\t"))
                        inputRows = inputRows[0].Split(new[] { "\t" }, 1000, StringSplitOptions.None);

                    // This is the header so name the columns
                    foreach (string col in inputRows)
                        inputTable.Columns.Add().ColumnName = col;

                    // Update the header flag
                    isHeader = false;
                }
                else
                {
                    // This is a data row so add it to the table
                    // First create a new row in the imput table
                    DataRow drInput = inputTable.NewRow();

                    // Add each string in the input columns array to the datatable
                    foreach (string col in inputRows)
                    {
                        drInput[colIdx] = col;
                        colIdx++;
                    }

                    // Add the row to the datatable
                    inputTable.Rows.Add(drInput);
                }
            }

            // If the input table doesn't contain a Brand Item Codes column 
            // output the input table as it is
            if (!inputTable.Columns.Contains("Brand Item Codes"))
            {
                OutputDataTableAsCSV(inputTable);
                return;
            }

            var outputTable = inputTable.Clone();

            foreach (DataRow inputRow in inputTable.Rows)
            {
                // Get the Brand Item Code and check if there are more than 1
                var bics = inputRow["Brand Item Codes"].ToString().Split(new[] { @" / " }, 1000, StringSplitOptions.None);

                // If there are not multiple SKUs just copy the row into the output table
                if (bics.Length <= 1)
                {
                    outputTable.ImportRow(inputRow);
                    continue;
                }

                foreach (string bic in bics)
                {
                    // Copy the row and update the brand item code column to the current split bic
                    var newRow = inputRow;
                    newRow["Brand Item Codes"] = bic;

                    // Add this row to the output table
                    outputTable.ImportRow(newRow);
                }
            }

            OutputDataTableAsCSV(outputTable);
        }

        internal static TextFieldParser GetParser(string source)
        {
            var parser = new TextFieldParser(source) { TextFieldType = FieldType.Delimited };
            parser.SetDelimiters(",");
            return parser;
        }

        public static List<Mapper> GetMapsFromCsvFile(string fileName, string source)
        {
            var parser = GetParser(AppDataFilepath + source + @"\Maps\" + fileName + ".csv");

            var returnList = new List<Mapper>();
            while (!parser.EndOfData)
            {
                string[] cols = parser.ReadFields();
                if (cols == null || cols[0] == "Regex") continue;
                returnList.Add(new Mapper { Match = cols[0], Replace = cols[1] });
            }

            return returnList;
        }

        internal static void OutputDataTableAsCSV(DataTable dt)
        {
            // Output the datatable as a CSV
            var sb = new StringBuilder();
            var columnNames = dt.Columns.Cast<DataColumn>().Select(column => "\"" + column.ColumnName.Replace("\"", "\"\"") + "\"").ToArray();
            sb.AppendLine(string.Join(",", columnNames));

            foreach (DataRow row in dt.Rows)
            {
                var fields = row.ItemArray.Select(field => "\"" + field.ToString().Replace("\"", "\"\"") + "\"").ToArray();
                sb.AppendLine(string.Join(",", fields));
            }

            File.WriteAllText(CsvFilepath + Source + @"\splitInput.csv", sb.ToString(), Encoding.Default);
        }

        public class Rule
        {
            public string Regex { get; set; }
            public string Delimiter { get; set; }
            public string OutputColumnName { get; set; }
            public int GroupNumber { get; set; }
            public string Transform { get; set; }
            public bool MappingFile { get; set; }
            public bool Ignore { get; set; }
            public bool Append { get; set; }
            public string Strip { get; set; }
        }

        public class Mapper
        {
            public string Match { get; set; }
            public string Replace { get; set; }
        }

        public static bool BoolTryParse(string s)
        {
            bool result;
            return bool.TryParse(s, out result) && result;
        }

        public static int IntTryParse(string s)
        {
            int result;
            int.TryParse(s, out result);
            return result;
        }
    }
}