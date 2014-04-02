using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FreestyleUltrasound
{
    public class Study
    {
        public string PatientName { get; set; }
        public string PatientID { get; set; }
        public string StudyInstanceUID { get; set; }
        public DateTime TimeStamp { get; set; }
    }
}
