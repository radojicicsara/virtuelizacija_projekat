using System.Runtime.Serialization;

namespace Common
{
    [DataContract]
    public class EisMeta
    {
        [DataMember]
        public string BatteryId { get; set; } // npr. B01, B02...

        [DataMember]
        public string TestId { get; set; }    // npr. Test_1

        [DataMember]
        public int SoC { get; set; }          // State of Charge (npr. 50, 100)

        [DataMember]
        public string FileName { get; set; }  // Naziv originalnog fajla

        [DataMember]
        public int TotalRows { get; set; }    // Koliko redova klijent planira da pošalje
    }
}