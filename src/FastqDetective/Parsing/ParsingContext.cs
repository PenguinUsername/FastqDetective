using System.Collections.Generic;

namespace FastqDetective.Parsing
{
    public class ParsingContext
    {
        public List<ParsingResult> parsingResults;

        public double Threshold;

        public string CurrentSequence = string.Empty;
        public string CurrentCertaintySequence = string.Empty;

        public long CurrentSequenceIndex = 0;
        public long TasksStarted = 0;
        public long TasksResultsReceived = 0;

        public int ParseStep = 0;

        public string InstrumentName = string.Empty;
        public string RunId = string.Empty;
        public string FlowcellId = string.Empty;
        public string FlowcellLane = string.Empty;
        public string ClusterCoordinateX = string.Empty;
        public string ClusterCoordinateY = string.Empty;
        public string IndexSequence = string.Empty;

        public ParsingContext()
        {
            parsingResults = new List<ParsingResult>();
        }

        public ParsingContext(List<ParsingResult> results)
        {
            parsingResults = results;
        }
    }
}
