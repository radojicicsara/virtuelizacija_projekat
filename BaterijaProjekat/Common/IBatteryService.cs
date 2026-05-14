using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ServiceModel;

namespace Common
{
    [ServiceContract] // Ovo kaže WCF-u da je ovo ugovor za servis
    public interface IBatteryService
    {
        [OperationContract]
        void StartSession(EisMeta meta);


        [OperationContract]
        [FaultContract(typeof(DataFormatFault))]
        [FaultContract(typeof(ValidationFault))]
        void PushSample(EisSample sample);

        [OperationContract]
        void EndSession();
    }
}
