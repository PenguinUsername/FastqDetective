using System;
using System.Collections.Generic;
using System.IO;

namespace FastqDetective.Parsing
{
    public static class MarkerParser
    {
        public static List<string> ParseFile(string path)
        {
            var result = new List<string>();

            try
            {
                using (StreamReader sr = new StreamReader(path))
                {
                    string line;

                    while ((line = sr.ReadLine()) != null)
                    {
                        ParseLine(line, ref result);
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("The file could not be read:");
                Console.WriteLine(e.Message);
            }

            return result;
        }

        private static void ParseLine(string line, ref List<string> result)
        {
            result.Add(RemoveWhitespace(line).ToLower());
        }

        private static string RemoveWhitespace(this string str)
        {
            return string.Join("", str.Split(default(string[]), StringSplitOptions.RemoveEmptyEntries));
        }
    }
}
