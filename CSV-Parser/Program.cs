using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.VisualBasic.FileIO;

namespace CSV_Parser
{
    internal class Program
    {
        private const string Basepath = @"E:\Development\CSV Parser\CSV Parser\Data\";
        private static string _source = "Wayfair";

        private static void Main()
        {
            // Get the brand name from the user
            //Console.WriteLine("Enter brand name:");
            //_source = Console.ReadLine();

            // Get the input CSV file
            var parser = GetParser(Basepath + _source + @"\input.csv");

            // Read the input file into a datatable, setting the first row to be the header
            var inputTable = new DataTable();
            var outputTable = new DataTable();
            bool isHeader = true;
            while (!parser.EndOfData)
            {
                string[] inputColumns;
                try
                {
                    inputColumns = parser.ReadFields();
                }
                catch
                {
                    continue;
                }

                if (inputColumns == null) continue;

                int colIdx = 0;
                if (isHeader)
                {
                    // This is the header so name the columns and
                    // update the header flag

                    if (inputColumns.Length==1&& inputColumns[0].Contains("\t"))
                        inputColumns = inputColumns[0].Split(new string[] {"\t"}, 1000, StringSplitOptions.None);

                    foreach (string col in inputColumns)
                        inputTable.Columns.Add().ColumnName = col;
                    isHeader = false;
                }
                else
                {
                    // This is a data row so add it to the table
                    DataRow drInput = inputTable.NewRow();
                    foreach (string col in inputColumns)
                    {
                        drInput[colIdx] = col;
                        colIdx++;
                    }
                    inputTable.Rows.Add(drInput);

                    DataRow drOutput = outputTable.NewRow();
                    outputTable.Rows.Add(drOutput);
                }
            }

            foreach (DataColumn inputColumn in inputTable.Columns)
            {
                // Add a column to the output table
                outputTable.Columns.Add(new DataColumn(inputColumn.ColumnName));

                // Check if there are rules for this column and if not
                // output the column to the output file as it is
                string filePath = Basepath + _source + @"\Rules\" + inputColumn.ColumnName + ".csv";
                if (!File.Exists(filePath))
                {
                    for (var i = 0; i < inputTable.Rows.Count; i++)
                    {
                        if (!outputTable.Columns.Contains(inputColumn.ColumnName))
                            outputTable.Columns.Add(new DataColumn(inputColumn.ColumnName));
                        outputTable.Rows[i][inputColumn.ColumnName] = inputTable.Rows[i][inputColumn.ColumnName].ToString();
                    }

                    continue;
                }

                // Get the rules 
                parser = GetParser(filePath);
                var rulesList = new List<Rule>();
                while (!parser.EndOfData)
                {
                    string[] cols = parser.ReadFields();
                    if (cols == null || cols[0] == "Regex") continue;
                    rulesList.Add(new Rule
                    {
                        Regex = cols.Any() ? cols[0] : "",
                        Delimiter = cols.Count() >= 2 ? cols[1] : "",
                        OutputColumnName = cols.Count() >= 3 ? cols[2] : "",
                        GroupNumber = cols.Count() >= 4 ?IntTryParse(cols[3]) : 0,
                        Transform = cols.Count() >= 5 ? cols[4] : "",
                        MappingFile = cols.Count() >= 6 && BoolTryParse(cols[5]),
                        Ignore = cols.Count()>=7 && BoolTryParse(cols[6]),
                        Append = cols.Count() >= 8 && BoolTryParse(cols[7]),
                        Strip = cols.Count() >= 9 ? cols[8] : ""
                    });
                }

                // Create a column for any input that doesn't match the rules
                outputTable.Columns.Add(new DataColumn(inputColumn.ColumnName + "(Not Matched)"));

                // Apply the rules
                for (var i = 0; i < inputTable.Rows.Count; i++)
                {
                    var outputRow = outputTable.NewRow();

                    // Get the contents of the cell
                    var cell = inputTable.Rows[i][inputColumn.ColumnName];
                    var cellContents = cell.ToString().Split(new[] { "  " }, 1000, StringSplitOptions.RemoveEmptyEntries).ToList();

                    // If there are no contents output an empty cell
                    if (!cellContents.Any() || cell.ToString() == "Unavailable" || cell.ToString() == "NULL")
                    {
                        if (!outputRow.Table.Columns.Contains(inputColumn.ColumnName))
                            outputTable.Columns.Add(new DataColumn(inputColumn.ColumnName));
                        outputRow[inputColumn.ColumnName] = "";
                        outputTable.Rows.Add(outputRow);
                        continue;
                    }

                    var notMatched = new List<string>(cellContents);

                    foreach (var rule in rulesList)
                    {
                        foreach (string cellContent in cellContents)
                        {
                            var match = Regex.Match(cellContent, rule.Regex + rule.Delimiter, RegexOptions.IgnoreCase);
                            if (!match.Success)
                            {
                                // try it without the double space ending
                                match = Regex.Match(cellContent, rule.Regex + "$", RegexOptions.IgnoreCase);
                                if (!match.Success) continue;
                            }

                            // Check if it's to be ignored
                            if (rule.Ignore)
                            {
                                notMatched.Remove(match.Value);
                                continue;
                            }

                            // Add a column for this data is there isn't one already
                            if (!outputTable.Columns.Contains(rule.OutputColumnName))
                                outputTable.Columns.Add(new DataColumn(rule.OutputColumnName));

                            // Get the matched value
                            var value = match.Groups[rule.GroupNumber].Value;

                            // Apply mapping
                            if (rule.MappingFile)
                            {
                                var maps = GetMapsFromCsvFile(rule.OutputColumnName, _source);

                                foreach (Mapper t in maps)
                                    if (t.Match == value)
                                    {
                                        value = t.Replace;
                                        break;
                                    }
                            }

                            // Apply transformations
                            // If there is a (*) placeholder in the transformation replace it with the extracted value, 
                            // otherwise replace the entire extracted value with the transformation
                            if (!string.IsNullOrWhiteSpace(rule.Transform))
                                value = rule.Transform.Contains("(*)") ? rule.Transform.Replace("(*)", value) : rule.Transform;
                            
                            // Strip any unwanted characters
                            if (!string.IsNullOrWhiteSpace(rule.Strip))
                                value = Regex.Replace(value, rule.Strip, "");

                            // Add the value to the output row
                            if (rule.Append)
                            {
                                if (!string.IsNullOrWhiteSpace(outputTable.Rows[i][rule.OutputColumnName].ToString()))
                                    value = ", " + value;
                                outputTable.Rows[i][rule.OutputColumnName] += value;
                            }
                            else
                                outputTable.Rows[i][rule.OutputColumnName] = value;

                            // remove the match from the not matched array
                            notMatched.Remove(match.Value);
                        }
                    }

                    outputTable.Rows[i][inputColumn.ColumnName + "(Not Matched)"] = string.Join("|#|", notMatched.ToArray());
                }

            }

            // Output the datatable as a CSV
            var sb = new StringBuilder();
            var columnNames = outputTable.Columns.Cast<DataColumn>().Select(column => "\"" + column.ColumnName.Replace("\"", "\"\"") + "\"").ToArray();
            sb.AppendLine(string.Join(",", columnNames));

            foreach (DataRow row in outputTable.Rows)
            {
                var fields = row.ItemArray.Select(field => "\"" + field.ToString().Replace("\"", "\"\"") + "\"").ToArray();
                sb.AppendLine(string.Join(",", fields));
            }

            File.WriteAllText(@"E:\Development\CSV Parser\CSV Parser\Data\Wayfair\output.csv", sb.ToString(), Encoding.Default);
        }

        internal static TextFieldParser GetParser(string source)
        {
            var parser = new TextFieldParser(source) { TextFieldType = FieldType.Delimited };
            parser.SetDelimiters(",");
            return parser;
        }

        public static List<Mapper> GetMapsFromCsvFile(string fileName, string source)
        {
            var parser = GetParser(Basepath + source + @"\Maps\" + fileName + ".csv");

            var returnList = new List<Mapper>();
            while (!parser.EndOfData)
            {
                string[] cols = parser.ReadFields();
                if (cols == null || cols[0] == "Regex") continue;
                returnList.Add(new Mapper { Match = cols[0], Replace = cols[1] });
            }

            return returnList;
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