using System.ServiceModel;

namespace Common
{
    public interface IBatteryServiceCallback
    {
        [OperationContract(IsOneWay = true)]
        void ReceiveNotification(TransferNotification notification);
    }
}
