using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace FastqDetective.Parsing
{
    public class ParsingResult
    {
        public readonly byte[] Marker;
        public readonly string MarkerString;

        public readonly List<Task<MarkerMatch>> ActiveTasks;
        public readonly List<MarkerMatch> Matches;

        public ParsingResult(string marker)
        {
            Marker = Encoding.ASCII.GetBytes(marker);
            MarkerString = marker;
            Matches = new List<MarkerMatch>();
            ActiveTasks = new List<Task<MarkerMatch>>();
        }
    }
}
