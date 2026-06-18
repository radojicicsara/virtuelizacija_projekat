using Common;
using System;
using System.Configuration;
using System.Globalization;
using System.IO;
using System.ServiceModel;
using System.Threading;

namespace Client
{
    class Program
    {
        private const int MaxSamplesPerFile = 39;

        static void Main(string[] args)
        {
            DirectoryInfo clientDir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory).Parent.Parent;
            string fileFolder = Path.Combine(clientDir.FullName, "file");
            string datasetRoot = Directory.GetDirectories(fileFolder)[0];
            int sendDelayMs = int.Parse(ConfigurationManager.AppSettings["SendDelayMs"]);

            foreach (string batteryFolder in Directory.GetDirectories(datasetRoot))
            {
                string batteryId = Path.GetFileName(batteryFolder);
                string eisFolder = Path.Combine(batteryFolder, "EIS measurements");
                if (!Directory.Exists(eisFolder))
                {
                    continue;
                }

                foreach (string testFolder in Directory.GetDirectories(eisFolder))
                {
                    string testId = Path.GetFileName(testFolder);
                    string hiokiFolder = Path.Combine(testFolder, "Hioki");
                    if (!Directory.Exists(hiokiFolder))
                    {
                        continue;
                    }

                    foreach (string csvFile in Directory.GetFiles(hiokiFolder, "*.csv"))
                    {
                        ProcessFile(batteryId, testId, csvFile, sendDelayMs);
                    }
                }
            }

            Console.WriteLine("Prenos zavrsen. Pritisnite Enter.");
            Console.ReadLine();
        }

        private static void ProcessFile(string batteryId, string testId, string csvFile, int sendDelayMs)
        {
            string fileName = Path.GetFileNameWithoutExtension(csvFile);
            int soc = ParseSoC(fileName);
            if (soc < 0)
            {
                LogInvalidFile(csvFile, "Ne moze se parsirati SoC iz naziva fajla.");
                return;
            }

            int totalRows = CountSamplesForTransfer(csvFile);
            if (totalRows == 0)
            {
                LogInvalidFile(csvFile, "Fajl nema validne redove za slanje.");
                return;
            }

            InstanceContext context = new InstanceContext(new BatteryServiceCallback());
            DuplexChannelFactory<IBatteryService> factory = new DuplexChannelFactory<IBatteryService>(context, "BatteryService");
            IBatteryService proxy = factory.CreateChannel();

            EisMeta meta = new EisMeta
            {
                BatteryId = batteryId,
                TestId = testId,
                SoC = soc,
                FileName = Path.GetFileName(csvFile),
                TotalRows = totalRows
            };

            try
            {
                Console.WriteLine($"[KLIJENT] StartSession za {batteryId} / {testId} / SoC={soc}");
                Console.WriteLine($"[KLIJENT] Prenos u toku. Fajl: {Path.GetFileName(csvFile)}");
                Console.WriteLine($"[ACK] {proxy.StartSession(meta)}");

                int sentRows = SendSamples(proxy, csvFile, sendDelayMs);

                Console.WriteLine($"[ACK] {proxy.EndSession()}");
                Console.WriteLine($"[KLIJENT] Prenos zavrsen. Poslato uzoraka: {sentRows}");
            }
            catch (FaultException<DataFormatFault> e)
            {
                Console.WriteLine($"[GRESKA FORMAT] {e.Detail.Message}");
            }
            catch (FaultException<ValidationFault> e)
            {
                Console.WriteLine($"[GRESKA VALIDACIJA] {e.Detail.Message}");
            }
            catch (Exception e)
            {
                Console.WriteLine($"[GRESKA] {e.Message}");
            }
            finally
            {
                try { factory.Close(); }
                catch { factory.Abort(); }
            }
        }

        private static int SendSamples(IBatteryService proxy, string csvFile, int sendDelayMs)
        {
            int rowIndex = 0;
            int sentRows = 0;

            using (StreamReader reader = new StreamReader(csvFile))
            {
                reader.ReadLine();

                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    string[] parts = line.Split(',');
                    if (!TryCreateSample(parts, rowIndex, out EisSample sample))
                    {
                        LogInvalidFile(csvFile, $"Red {rowIndex} je nevalidan: {line}");
                        rowIndex++;
                        continue;
                    }

                    proxy.PushSample(sample);
                    sentRows++;
                    rowIndex++;

                    if (sentRows >= MaxSamplesPerFile)
                    {
                        break;
                    }

                    Thread.Sleep(sendDelayMs);
                }
            }

            return sentRows;
        }

        private static int CountSamplesForTransfer(string csvFile)
        {
            int rowIndex = 0;
            int totalRows = 0;

            using (StreamReader reader = new StreamReader(csvFile))
            {
                reader.ReadLine();

                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    if (TryCreateSample(line.Split(','), rowIndex, out _))
                    {
                        totalRows++;
                        if (totalRows >= MaxSamplesPerFile)
                        {
                            break;
                        }
                    }

                    rowIndex++;
                }
            }

            return totalRows;
        }

        private static bool TryCreateSample(string[] parts, int rowIndex, out EisSample sample)
        {
            sample = null;
            if (parts.Length < 6)
            {
                return false;
            }

            bool ok =
                double.TryParse(parts[0].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out double freq) &
                double.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out double r) &
                double.TryParse(parts[2].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out double x) &
                double.TryParse(parts[4].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out double t) &
                double.TryParse(parts[5].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out double range);

            if (!ok)
            {
                return false;
            }

            sample = new EisSample
            {
                RowIndex = rowIndex,
                FrequencyHz = freq,
                R_ohm = r,
                X_ohm = x,
                T_degC = t,
                Range_ohm = range,
                TimestampLocal = DateTime.Now
            };

            return true;
        }

        private static int ParseSoC(string fileNameWithoutExtension)
        {
            string marker = "SoC_";
            int idx = fileNameWithoutExtension.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                return -1;
            }

            string afterMarker = fileNameWithoutExtension.Substring(idx + marker.Length);
            string socStr = afterMarker.Split('_')[0];

            return int.TryParse(socStr, out int soc) ? soc : -1;
        }

        private static void LogInvalidFile(string csvFile, string reason)
        {
            string entry = $"{DateTime.Now:O} | Fajl: {Path.GetFileName(csvFile)} | Razlog: {reason}";
            File.AppendAllText("invalid_rows.log", entry + Environment.NewLine);
            Console.WriteLine($"[LOG] {entry}");
        }
    }

    public class BatteryServiceCallback : IBatteryServiceCallback
    {
        public void ReceiveNotification(TransferNotification notification)
        {
            Console.WriteLine($"[CALLBACK:{notification.EventType}] {notification.Message}");
        }
    }
}
