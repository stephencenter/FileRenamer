﻿using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows.Markup;

namespace FileRenamer
{
    public class Logger
    {
        public string LogPath { get; set; }
        public Logger(string log_path) { LogPath = log_path; }

        public void AppendToLog(string message)
        {
            using var stream = new StreamWriter(LogPath, true);
            stream.WriteLine($"{DateTime.Now}: {message}");
        }
    }

    public class Ruleset
    {
        public required string path { get; set; }
        public required string input_file { get; set; }
        public required string output_file { get; set; }
        public required string[] column_names { get; set; }
        public required string[] date_formats { get; set; }
        public required string separator { get; set; }
        public required string[] columns_to_delete { get; set; }
        public required bool delete_original { get; set; }
        public required bool log_successes { get; set; }
    }

    public class Program
    {
        public static void Main()
        {
            // Initialize logging
            Logger logger = new("log.txt");

            string rules_file = "renaming_rules.json";
            Ruleset[]? ruleset_list;

            // Load the renaming rules from a json file. The file is a list, each element is one
            // rule for how to rename a specific file
            string json_string;
            try
            {
                json_string = File.ReadAllText(rules_file);
                ruleset_list = JsonSerializer.Deserialize<Ruleset[]>(json_string);
            }
            catch (FileNotFoundException)
            {
                logger.AppendToLog($"Rules file could not be found at {rules_file}");
                return;
            }
            catch (IOException)
            {
                logger.AppendToLog($"There was an error loading rules file at {rules_file}");
                return;
            }
            catch (JsonException)
            {
                logger.AppendToLog($"Rules file at {rules_file} is in an invalid format");
                return;
            }
            if (ruleset_list == null)
            {
                logger.AppendToLog($"Rules file at {rules_file} does not contain any valid renaming rules");
                return;
            }

            // Iterate through each renamer and apply its rules to rename a specific file
            foreach (Ruleset ruleset in ruleset_list)
            {
                string input_path = Path.Join(ruleset.path, ruleset.input_file);

                if (!File.Exists(input_path))
                {
                    logger.AppendToLog($"Could not find input file at {input_path}");
                    continue;
                }

                if (ruleset.column_names.Length > ruleset.date_formats.Length)
                {
                    logger.AppendToLog($"The rule defined for renaming {input_path} has too few date formats listed");
                    continue;
                }

                else if (ruleset.column_names.Length < ruleset.date_formats.Length)
                {
                    logger.AppendToLog($"The rule defined for renaming {input_path} has too many date formats listed");
                    continue;
                }

                // Parse header and row data from csv file
                string[]? header_values = null;
                string[]? row_values = null; 
                List<string> csv_lines = new();
                try
                {
                    using var filestream = new FileStream(input_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var reader = new StreamReader(filestream);
                    
                    List<string> current_values = new();
                    string? current_line = reader.ReadLine();
                    int row_number = 0;
                    while (!string.IsNullOrWhiteSpace(current_line))
                    {
                        current_values.Clear();
                        var columns = current_line.Split(ruleset.separator);

                        // Keep track of the data in the header separately
                        if (row_number == 0 || header_values == null)
                        {
                            header_values = columns;
                        }

                        // Keep track of the data in the first non-header row separately
                        if (row_number > 0 && row_values == null)
                        {
                            row_values = columns;
                        }

                        // You can specify which columns should be deleted in the ruleset
                        // This allows you to use a column's data to rename the file, without 
                        // including the column in the final .csv file
                        for (int i = 0; i < columns.Length; i++)
                        {
                            if (!ruleset.columns_to_delete.Contains(header_values[i]))
                            {
                                current_values.Add(columns[i]);
                            }
                        }

                        var new_line = string.Join(ruleset.separator, current_values);
                        csv_lines.Add(new_line);
                        current_line = reader.ReadLine();
                        row_number++;
                    };

                    if (header_values == null)
                    {
                        logger.AppendToLog($"Input file at {input_path} has no header data");
                        continue;
                    }

                    if (row_values == null)
                    {
                        logger.AppendToLog($"Input file at {input_path} has no row data");
                        continue;
                    }
                }
                catch (IOException)
                {
                    logger.AppendToLog($"There was an error loading input file at {input_path}");
                    continue;
                }

                // Get the columns specified in the renaming rule, convert them to datetimes 
                List<DateTime> found_values = new();
                foreach (string column in ruleset.column_names)
                {
                    int index = Array.IndexOf(header_values, column);
                    if (index == -1)
                    {
                        logger.AppendToLog($"The rule defined for renaming {input_path} has an invalid column name '{column}'");
                        continue;
                    }

                    DateTime parsed_date;
                    try
                    {
                        parsed_date = DateTime.Parse(row_values[index]);
                    }
                    catch (IndexOutOfRangeException)
                    {
                        logger.AppendToLog($"Row data for {input_path} ends before reaching required column '{column}'");
                        continue;
                    }

                    found_values.Add(parsed_date);
                }

                if (found_values.Count != ruleset.column_names.Length)
                {
                    continue;
                }

                // Convert the datetimes back into string using the specified format in the rules file,
                // then insert them into the output path template
                string output_path;
                try
                {
                    string[] date_inserts = new string[found_values.Count];
                    for (int i = 0; i < found_values.Count; i++)
                    {
                        date_inserts[i] = found_values[i].ToString(ruleset.date_formats[i]);
                    }

                    output_path = Path.Join(ruleset.path, string.Format(ruleset.output_file, date_inserts));
                }
                catch (FormatException)
                {
                    logger.AppendToLog($"The rule defined for renaming {input_path} is invalid");
                    continue;
                }

                // Write the .csv data to the new filename
                try
                {
                    // We have to set encoding to UTF-16 or it won't be automatically openable in Excel
                    using StreamWriter writer = new(output_path, false, Encoding.Unicode);
                    foreach (var line in csv_lines)
                    {
                        writer.WriteLine(line);
                    }
                }

                catch (Exception ex)
                {
                    if (ex is IOException || ex is UnauthorizedAccessException)
                    {
                        logger.AppendToLog($"There was an error renaming {input_path} to {output_path}");
                        continue;
                    }

                    throw;
                }

                if (ruleset.log_successes)
                {
                    logger.AppendToLog($"Successfully copied {input_path} to {output_path}");
                }

                if (ruleset.delete_original)
                {
                    try
                    {
                        File.Delete(input_path);
                    }

                    catch (IOException)
                    {
                        if (File.Exists(input_path))
                        {
                            logger.AppendToLog($"Failed to delete leftover file {input_path} as requested by rules file");
                        }
                    }
                }
            }
        }
    }
}
