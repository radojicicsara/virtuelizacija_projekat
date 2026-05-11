using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using System.Text;
using System.Threading.Tasks;

namespace Service
{
    class Program
    {
        static void Main(string[] args)
        {
            using (ServiceHost host = new ServiceHost(typeof(BatteryService)))
            {
                host.Open();
                Console.WriteLine("Server je pokrenut na adresi: net.tcp://localhost:4000/BatteryService");
                Console.WriteLine("Pritisnite [Enter] za zaustavljanje.");
                Console.ReadLine();
            }
        }
    }
}
