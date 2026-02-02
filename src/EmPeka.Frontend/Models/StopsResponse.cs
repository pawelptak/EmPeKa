using System.Collections.Generic;

namespace EmPeka.Frontend.Models
{
    public class StopModel
    {
        public string StopId { get; set; }
        public string StopCode { get; set; }
        public string StopName { get; set; }
    }

    public class StopsResponse
    {
        public List<StopModel> Stops { get; set; }
        public int Total { get; set; }
    }
}