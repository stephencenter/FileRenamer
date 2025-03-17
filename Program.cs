using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Linq;

namespace FileRenamer
{
    public class Logger
    {
        public string LogPath { get; set; }
        public Logger(string log_path) { LogPath = log_path; }

        public void AppendToLog(string message)
        {
            var to_write = $"{DateTime.Now} | {Program.VERSION_NUMBER}: {message}";
            try
            {
                using var stream = new StreamWriter(LogPath, true);
                stream.WriteLine(to_write);
            }

            catch (Exception ex)
            {
                // If we can't write to the log file, write to console instead
                if (ex is IOException || ex is UnauthorizedAccessException)
                {
                    Console.WriteLine(to_write);
                    return;
                }

                throw;
            }
        }
    }

    public class Ruleset
    {
        public string path { get; set; }
        public string input_file { get; set; }
        public string output_file { get; set; }
        public string[] column_names { get; set; }
        public string[] date_formats { get; set; }
        public string separator { get; set; }
        public string[] columns_to_delete { get; set; }
        public bool delete_original { get; set; }
        public bool log_successes { get; set; }
    }

    public class Program
    {
        public const string VERSION_NUMBER = "v1.0.1";

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

                List<string> formatted_values = new();
                List<string> csv_lines = new();
                if (ruleset.column_names.Length > 0)
                {
                    // Parse header and row data from csv file
                    string[]? header_values = null;
                    string[]? row_values = null; 
                    try
                    {
                        using var filestream = new FileStream(input_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        using var reader = new StreamReader(filestream);
                    
                        List<string> current_values = new();
                        string? current_line = reader.ReadLine();
                        while (!string.IsNullOrWhiteSpace(current_line))
                        {
                            // We only need data for the header and the first row, unless we're deleting columns.
                            // Deleting columns requires us to rebuild the .csv entirely, which means we need to 
                            // continue looping until we get all of the row data
                            if (ruleset.columns_to_delete.Length == 0 && header_values != null && row_values != null)
                            {
                                break;
                            }

                            current_values.Clear();
                            var columns = current_line.Split(ruleset.separator);

                            // Keep track of the data in the first non-header row separately
                            if (header_values != null)
                            {
                                row_values ??= columns;
                            }

                            // Keep track of the data in the header separately
                            header_values ??= columns;

                            for (int i = 0; i < columns.Length; i++)
                            {
                                // You can specify which columns should be deleted in the ruleset
                                // This allows you to use a column's data to rename the file, without 
                                // including the column in the final .csv file
                                if (!ruleset.columns_to_delete.Contains(header_values[i]))
                                {
                                    current_values.Add(columns[i]);
                                }
                            }

                            var new_line = string.Join(ruleset.separator, current_values);
                            csv_lines.Add(new_line);
                            current_line = reader.ReadLine();
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

                    // Gather the values for each column specified in the ruleset
                    int counter = 0;
                    foreach (string column_name in ruleset.column_names)
                    {
                        // Find where in the .csv file data the desired column is located
                        int index = Array.IndexOf(header_values, column_name);
                        if (index == -1)
                        {
                            logger.AppendToLog($"Couldn't find column name '{column_name}' in input file {input_path}");
                            continue;
                        }

                        string new_value;
                        try
                        {
                            new_value = row_values[index];
                        }
                        catch (IndexOutOfRangeException)
                        {
                            logger.AppendToLog($"Row data for {input_path} ends before reaching required column '{column_name}'");
                            continue;
                        }

                        if (ruleset.date_formats[counter] != null)
                        {
                            try
                            {
                                DateTime parsed_date = DateTime.Parse(new_value);
                                formatted_values.Add(parsed_date.ToString(ruleset.date_formats[counter]));
                            }
                            catch (FormatException)
                            {
                                logger.AppendToLog($"The provided date format for {column_name} is invalid for renaming {input_path}");
                                continue;
                            }
                        }
                    
                        else
                        {
                            formatted_values.Add(new_value);
                        }

                        counter++;
                    }

                    if (formatted_values.Count != ruleset.column_names.Length)
                    {
                        logger.AppendToLog($"Failed to gather all required values for renaming {input_path}");
                        continue;
                    }

                }

                // Convert the datetimes back into string using the specified format in the rules file,
                // then insert them into the output path template
                string output_path;
                try
                {
                    string output_name = string.Format(ruleset.output_file, formatted_values.ToArray());
                    output_name = Regex.Replace(output_name, "[\\/:*?\"<>|]", "_");
                    output_path = Path.Join(ruleset.path, output_name);
                }
                catch (FormatException)
                {
                    logger.AppendToLog($"The rule defined for renaming {input_path} needs more values than were provided");
                    continue;
                }

                string renaming_copying = ruleset.delete_original ? "renaming" : "copying";
                string renamed_copied = ruleset.delete_original ? "renamed" : "copied";

                // Try to rename file
                try
                {
                    if (ruleset.columns_to_delete.Length > 0)
                    {
                        // We have to set encoding to UTF-16 or it won't be automatically openable in Excel
                        using StreamWriter writer = new(output_path, false, Encoding.Unicode);
                        foreach (var line in csv_lines)
                        {
                            writer.WriteLine(line);
                        }
                    }

                    else
                    {
                        if (ruleset.delete_original)
                        {
                            File.Move(input_path, output_path);
                        }

                        else
                        {
                            File.Copy(input_path, output_path, true);
                        }
                    }
                }

                catch (Exception ex)
                {
                    if (ex is IOException || ex is UnauthorizedAccessException)
                    {
                        logger.AppendToLog($"There was an error {renaming_copying} {input_path} to {output_path}");
                        continue;
                    }

                    throw;
                }

                if (ruleset.log_successes)
                {
                    logger.AppendToLog($"Successfully {renamed_copied} {input_path} to {output_path}");
                }

                if (ruleset.columns_to_delete.Length > 0 && ruleset.delete_original)
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
