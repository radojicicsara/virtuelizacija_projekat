using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Common;
using System.ServiceModel;
using System.IO;

namespace Service
{
    // Ključni deo: ": IBatteryService" znači da ova klasa realizuje tvoj ugovor
    public class BatteryService : IBatteryService, IDisposable
    {

        private StreamWriter _writer;

        public void StartSession(EisMeta meta)
        {
            Console.WriteLine($"[SERVER] Započeta sesija za: {meta.BatteryId}, Test: {meta.TestId}");
            // Ovde ćemo kasnije dodati pravljenje foldera
        }

        public void PushSample(EisSample sample)
        {
            // Zadatak 3: Validacija podataka
            if (sample.FrequencyHz <= 0)
            {
                var fault = new ValidationFault { Message = "Frekvencija mora biti veća od 0!" };
                throw new FaultException<ValidationFault>(fault);
            }

            if (sample.R_ohm <= 0)
            {
                var fault = new ValidationFault { Message = "Otpornost (R_ohm) mora biti realna pozitivna vrednost!" };
                throw new FaultException<ValidationFault>(fault);
            }

            // Ako je sve OK, upiši u fajl
            if (_writer != null)
            {
                _writer.WriteLine($"{sample.RowIndex},{sample.FrequencyHz},{sample.R_ohm},{sample.X_ohm},{sample.T_degC},{sample.Range_ohm},{sample.TimestampLocal}");
                _writer.Flush();
            }
        }

      
        public void EndSession()
        {
            Dispose(); // Zatvaramo resurse kada klijent završi
            Console.WriteLine("[SERVER] Sesija uspešno završena.");
        }

        public void Dispose()
        {
            if (_writer != null)
            {
                _writer.Close();
                _writer.Dispose();
                _writer = null;
            }
        }

    }
}