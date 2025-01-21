using System.Text.Json;

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
        public required bool replace_original { get; set; }
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
                string? header_line;
                string[]? header_values;
                string? row_line;
                string[]? row_values;

                // Parse header and row data from csv file
                string input_path = Path.Join(ruleset.path, ruleset.input_file);
                try
                {
                    using var reader = new StreamReader(input_path);
                    header_line = reader.ReadLine();

                    if (header_line == null)
                    {
                        return;
                    }

                    header_values = header_line.Split(ruleset.separator);
                    row_line = reader.ReadLine();

                    if (row_line == null)
                    {
                        return;
                    }

                    row_values = row_line.Split(ruleset.separator);

                }
                catch (FileNotFoundException)
                {
                    logger.AppendToLog($"Could not find input file at {input_path} ");
                    continue;
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
                    if (index != -1)
                    {
                        DateTime parsed_date = DateTime.Parse(row_values[index]);
                        found_values.Add(parsed_date);
                    }
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
                    logger.AppendToLog($"The rule for renaming {input_path} is invalid");
                    continue;
                }

                if (ruleset.replace_original)
                {
                    try
                    {
                        File.Move(input_path, output_path, true);
                        continue;
                    }
                    catch (IOException)
                    {
                        logger.AppendToLog($"There was an error renaming {input_path}, creating copy instead");
                    }
                }

                // Create a copy of the input file with the desired output name
                try
                {
                    File.Copy(input_path, output_path, true);
                }
                catch (IOException)
                {
                    logger.AppendToLog($"There was an error copying {input_path}");
                    continue;
                }
            }
        }
    }
}
