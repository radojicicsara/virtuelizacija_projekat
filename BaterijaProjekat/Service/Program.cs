using System;
using System.ServiceModel;
using Common;

namespace Service
{
    class Program
    {
        static void Main(string[] args)
        {
            BatteryService.OnTransferStarted += LogNotification;
            BatteryService.OnSampleReceived += LogNotification;
            BatteryService.OnTransferCompleted += LogNotification;
            BatteryService.OnWarningRaised += LogNotification;

            using (ServiceHost host = new ServiceHost(typeof(BatteryService)))
            {
                host.Open();
                Console.WriteLine("Server je pokrenut na adresi: net.tcp://localhost:4000/BatteryService");
                Console.WriteLine("Pritisnite [Enter] za zaustavljanje.");
                Console.ReadLine();
            }
        }

        private static void LogNotification(TransferNotification notification)
        {
            Console.WriteLine($"[{notification.EventType}] {notification.Message}");
        }
    }
}
