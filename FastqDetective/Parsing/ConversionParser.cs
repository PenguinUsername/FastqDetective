using System;
using System.IO;
using System.Text;

namespace FastqDetective.Parsing
{
    public static class ConversionParser
    {
        public static void ConvertFile(string inputPath, string outputPath)
        {
            try
            {
                int parsingStep = 0;

                using (StreamWriter sw = new StreamWriter(outputPath, true, Encoding.ASCII))
                {
                    sw.AutoFlush = true;

                    using (StreamReader sr = new StreamReader(inputPath))
                    {
                        string currentLine;

                        while ((currentLine = sr.ReadLine()) != null)
                        {
                            switch (parsingStep)
                            {
                                case 0:
                                    ++parsingStep;
                                    break;
                                case 1:
                                    ++parsingStep;
                                    sw.Write(currentLine);
                                    break;
                                case 2:
                                    ++parsingStep;
                                    break;
                                case 3:
                                    parsingStep = 0;
                                    break;
                                default:
                                    break;
                            }

                            continue;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("The file could not be converted:");
                Console.WriteLine(e.Message);
            }
        }
    }
}
